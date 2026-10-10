using System;
using Rinten;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests;

//Blend shape deltas get the model's whole-model transform like the rest of the mesh
[TestClass]
[DoNotParallelize]
public class ModelMorphSpaceTests
{
	//A quarter turn about Z, which takes X to Y.
	private static readonly float[] QuarterTurn =
	{
		0.0f, 0.0f, 0.70710678f, 0.70710678f,
	};

	[TestMethod]
	public unsafe void AShapeIsTurnedWithTheModel()
	{
		ThreadSafe.MarkMainThread();

		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
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

			Assert.IsTrue( first >= 0 );

			var indices = new uint[] { 0, 1, 2 };
			fixed ( uint* i = indices )
				builder.AddIndices( i, indices.Length );

			//A small 0.025m delta, since unit-length deltas survive wrongly being renormalized
			var moved = new[] { first };
			var deltas = new[] { 0.025f, 0.0f, 0.0f };
			var normals = new[] { 0.0f, 0.0f, 0.0f };

			fixed ( int* m = moved )
			fixed ( float* p = deltas )
			fixed ( float* n = normals )
			{
				Assert.AreEqual( 0, builder.AddMorph( "along_x", m, p, n, 1 ) );
			}

			fixed ( float* q = QuarterTurn )
				builder.ApplyTransform( null, q, null );

			model = builder.Build();
			Assert.IsNotNull( model, "the model should build" );

			Assert.AreEqual( 1, model.MorphDeltaCount );

			var bytes = new byte[28];
			fixed ( byte* d = bytes )
				Assert.AreEqual( 1, model.CopyMorphDeltas( d, 1 ) );

			var x = BitConverter.ToSingle( bytes, 4 );
			var y = BitConverter.ToSingle( bytes, 8 );
			var z = BitConverter.ToSingle( bytes, 12 );

			//A quarter turn about Z takes X to Y, and the length is the whole of what a delta says - so it comes out the same length it went in.
			Assert.AreEqual( 0.0f, x, 0.0005f, $"the delta should have turned with the model - got {x},{y},{z}" );
			Assert.AreEqual( 0.025f, y, 0.0005f, $"the delta should have turned with the model - got {x},{y},{z}" );
			Assert.AreEqual( 0.0f, z, 0.0005f, $"the delta should have turned with the model - got {x},{y},{z}" );
		}
		finally
		{
			if ( model is not null ) model.Dispose();
			builder.Dispose();
		}
	}

	//A model built at twice the size moves its vertices twice as far. The scale a delta takes is the geometry's own, not the inverse one a normal
	//takes. Through the normal path a model made bigger had its shapes move things less, which is the opposite of what scaling a model means.
	[TestMethod]
	public unsafe void AShapeIsScaledWithTheModel()
	{
		ThreadSafe.MarkMainThread();

		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
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

			var moved = new[] { first };
			var deltas = new[] { 0.0f, 0.0f, 0.25f };
			var normals = new[] { 0.0f, 0.0f, 0.0f };

			fixed ( int* m = moved )
			fixed ( float* p = deltas )
			fixed ( float* n = normals )
			{
				Assert.AreEqual( 0, builder.AddMorph( "up", m, p, n, 1 ) );
			}

			var scale = new[] { 2.0f, 2.0f, 2.0f };

			fixed ( float* s = scale )
				builder.ApplyTransform( null, null, s );

			model = builder.Build();
			Assert.IsNotNull( model );

			var bytes = new byte[28];
			fixed ( byte* d = bytes )
				Assert.AreEqual( 1, model.CopyMorphDeltas( d, 1 ) );

			var z = BitConverter.ToSingle( bytes, 12 );

			Assert.AreEqual( 0.5f, z, 0.0005f, $"twice the model should move things twice as far - got {z}" );
		}
		finally
		{
			if ( model is not null ) model.Dispose();
			builder.Dispose();
		}
	}
}
