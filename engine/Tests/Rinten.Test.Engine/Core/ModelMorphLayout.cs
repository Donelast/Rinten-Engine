using System;
using System.IO;
using System.Text.RegularExpressions;
using Rinten;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests;

//Morph delta byte layout agrees across model_format.h, CoreModel's stride and frame.slang
[TestClass]
[DoNotParallelize]
public class ModelMorphLayoutTests
{
	//Core and engine agree on the delta struct size
	[TestMethod]
	public void TheCoreAndTheEngineAgreeOnWhatADeltaIs()
	{
		var (delta, run) = Strides();

		Assert.AreEqual( CoreModel.MorphDeltaStride, delta,
			$"the format writes a delta at {delta} bytes and this build reads it at "
			+ $"{CoreModel.MorphDeltaStride} - CoreModel and frame.slang have to move with the struct" );

		Assert.AreEqual( CoreModel.MorphRunStride, run,
			$"the format writes a run at {run} bytes and this build reads it at {CoreModel.MorphRunStride}" );
	}

	//The strides the core reports, or inconclusive where there is no core. Deliberately not gated on
	//CoreNative.Available, the way the rest of these are: nothing native is asked, and the sizes are the format's own.
	private static (int Delta, int Run) Strides()
		=> (System.Runtime.CompilerServices.Unsafe.SizeOf<ModelFormat.MorphDelta>(),
			System.Runtime.CompilerServices.Unsafe.SizeOf<ModelFormat.MorphRun>());

	//Each delta field reads back at the shader's offsets: index 0, position 4, normal 16
	[TestMethod]
	public unsafe void EveryFieldOfADeltaIsWhereItIsReadFrom()
	{
		ThreadSafe.MarkMainThread();

		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var builder = new CoreAssets.ModelBuilder();
		Assert.IsNotNull( builder );

		CoreAssets.ModelFile model = null;

		try
		{
			var vertices = new Vertex[3];
			for ( var i = 0; i < 3; i++ )
				vertices[i] = new Vertex { Position = new Vector3( i, 0, 0 ), Normal = Vector3.Up };

			int first;
			fixed ( Vertex* v = vertices )
				first = builder.AddVertices( v, vertices.Length );

			var indices = new uint[] { 0, 1, 2 };
			fixed ( uint* i = indices )
				builder.AddIndices( i, indices.Length );

			//Deliberately unlike each other, and none of them nought: a field read out of the wrong place has to land on a different number.
			var moved = new[] { first };
			var firstPosition = new[] { 0.125f, 0.25f, 0.375f };
			var firstNormal = new[] { 0.5f, 0.625f, 0.75f };
			var secondPosition = new[] { -0.125f, -0.25f, -0.375f };
			var secondNormal = new[] { -0.5f, -0.625f, -0.75f };

			fixed ( int* m = moved )
			fixed ( float* p = firstPosition )
			fixed ( float* n = firstNormal )
				Assert.AreEqual( 0, builder.AddMorph( "one", m, p, n, 1 ) );

			fixed ( int* m = moved )
			fixed ( float* p = secondPosition )
			fixed ( float* n = secondNormal )
				Assert.AreEqual( 1, builder.AddMorph( "two", m, p, n, 1 ) );

			model = builder.Build();
			Assert.IsNotNull( model, "the model should build" );

			var stride = System.Runtime.CompilerServices.Unsafe.SizeOf<ModelFormat.MorphDelta>();
			Assert.AreEqual( CoreModel.MorphDeltaStride, stride );

			Assert.AreEqual( 2, model.MorphDeltaCount,
				"one vertex in two shapes is two deltas" );

			var bytes = new byte[2 * stride];
			fixed ( byte* d = bytes )
				Assert.AreEqual( 2, model.CopyMorphDeltas( d, 2 ) );

			//The morph index at nought. Both deltas belong to the same vertex, so they are its whole run and they arrive in shape order.
			Assert.AreEqual( 0u, BitConverter.ToUInt32( bytes, 0 ),
				"the morph index should be the first four bytes of a delta" );
			Assert.AreEqual( 1u, BitConverter.ToUInt32( bytes, stride ),
				"and the second delta should say it belongs to the second shape" );

			Field( bytes, 0, 4, firstPosition, "the position should be four bytes into a delta" );
			Field( bytes, 0, 16, firstNormal, "the normal should be sixteen bytes into a delta" );
			Field( bytes, stride, 4, secondPosition, "the second delta's position too" );
			Field( bytes, stride, 16, secondNormal, "and the second delta's normal" );

			//And the run, which is the other half of reading the block: the vertex that moves owns both deltas and the two that do not own none.
			var runStride = System.Runtime.CompilerServices.Unsafe.SizeOf<ModelFormat.MorphRun>();
			Assert.AreEqual( CoreModel.MorphRunStride, runStride );

			var runs = new byte[3 * runStride];
			fixed ( byte* r = runs )
				Assert.AreEqual( 3, model.CopyMorphRuns( r, 3 ) );

			Assert.AreEqual( 0u, BitConverter.ToUInt32( runs, 0 ), "the moved vertex's run starts at nought" );
			Assert.AreEqual( 2u, BitConverter.ToUInt32( runs, 4 ), "and is both of the deltas" );
			Assert.AreEqual( 0u, BitConverter.ToUInt32( runs, ( 1 * runStride ) + 4 ), "a vertex in no shape has none" );
			Assert.AreEqual( 0u, BitConverter.ToUInt32( runs, ( 2 * runStride ) + 4 ), "nor has the third" );
		}
		finally
		{
			if ( model is not null ) model.Dispose();
			builder.Dispose();
		}
	}

	//Three floats out of the block, at a delta and a field offset.
	private static void Field( byte[] bytes, int delta, int offset, float[] expected, string because )
	{
		for ( var axis = 0; axis < 3; axis++ )
		{
			var got = BitConverter.ToSingle( bytes, delta + offset + ( axis * 4 ) );
			Assert.AreEqual( expected[axis], got, 0.0005f, $"{because} - axis {axis} came back {got}" );
		}
	}

	//frame.slang's LoadMorphDelta stride, read from the source file, matches the core's
	[TestMethod]
	public void TheShaderReadsADeltaWhereTheCoreWroteIt()
	{
		var (stride, _) = Strides();

		var game = Environment.GetEnvironmentVariable( EngineEnvironment.Folder );
		Assert.IsNotNull( game, $"{EngineEnvironment.Folder} is not set" );

		var path = Path.Combine( game, "addons", "base", "Assets", "shaders", "frame.slang" );
		Assert.IsTrue( File.Exists( path ), $"frame.slang is not at {path}" );

		var source = File.ReadAllText( path );

		Assert.AreEqual( stride, Constant( source, "MorphDeltaStride" ),
			"the shader walks the delta buffer in a stride the core does not write" );

		//These are the core's, held by the offsetof asserts beside the struct in model_format.h. Nothing
		//exports them, because nothing at runtime reads a delta on this side - only the shader does.
		Assert.AreEqual( 4, Constant( source, "MorphDeltaPosition" ),
			"the shader reads the position somewhere the core did not write it" );

		Assert.AreEqual( 16, Constant( source, "MorphDeltaNormal" ),
			"the shader reads the normal somewhere the core did not write it" );
	}

	//One static const uint out of the shader, by name.
	private static int Constant( string source, string name )
	{
		var found = Regex.Match( source, $@"static\s+const\s+uint\s+{Regex.Escape( name )}\s*=\s*(\d+)\s*;" );

		Assert.IsTrue( found.Success,
			$"frame.slang no longer declares {name} - the delta layout it reads with cannot be checked" );

		return int.Parse( found.Groups[1].Value );
	}
}
