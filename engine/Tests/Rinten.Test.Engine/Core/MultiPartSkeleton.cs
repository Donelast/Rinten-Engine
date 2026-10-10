using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinten;
using Rinten.CoreAnimation;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests.Core;

//A model from several files sharing a rig keeps one merged skeleton, even if one file has none
[TestClass]
[DoNotParallelize]
public class MultiPartSkeletonTests
{
	//A reference SMD: the named bones, and one triangle weighted to the last.
	private static string Reference( params string[] bones )
	{
		var text = new StringBuilder();

		text.AppendLine( "version 1" );
		text.AppendLine( "nodes" );

		for ( var i = 0; i < bones.Length; i++ )
			text.AppendLine( $"{i} \"{bones[i]}\" {i - 1}" );

		text.AppendLine( "end" );
		text.AppendLine( "skeleton" );
		text.AppendLine( "time 0" );

		for ( var i = 0; i < bones.Length; i++ )
			text.AppendLine( $"{i} 0.000000 0.000000 {i}.000000 0.000000 0.000000 0.000000" );

		text.AppendLine( "end" );
		text.AppendLine( "triangles" );

		var last = bones.Length - 1;

		for ( var corner = 0; corner < 3; corner++ )
		{
			text.AppendLine( "test.vmt" );
			text.AppendLine( $"0 {corner}.000000 0.000000 0.000000 0.000000 0.000000 1.000000 0.000000 0.000000 1 {last} 1.000000" );
		}

		text.AppendLine( "end" );

		return text.ToString();
	}

	private static string Write( string directory, string name, string text )
	{
		var path = Path.Combine( directory, name );
		File.WriteAllText( path, text );

		return path;
	}

	[TestMethod]
	public unsafe void PartsOfOneModelShareOneSkeleton()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-parts-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		var compiled = Path.Combine( directory, "weapon.mdl_c" );

		var builder = new CoreAssets.ModelBuilder();
		Assert.IsNotNull( builder );

		try
		{
			//The body, and a magazine exported with the same rig plus a bone of its own - which is what a Source weapon's reference meshes are.
			var body = Write( directory, "body.smd", Reference( "root", "spine", "grip" ) );
			var magazine = Write( directory, "mag.smd", Reference( "root", "spine", "grip", "round1" ) );

			Assert.IsTrue( CoreSmdImport.Import( builder, body, 1.0f ) > 0, "the body would not import" );
			Assert.IsTrue( CoreSmdImport.Import( builder, magazine, 1.0f ) > 0, "the magazine would not import" );

			//A sight with no rig at all, added the way an unskinned export is: vertices and indices, and no skin beside them.
			var loose = new Vertex[3];
			for ( var i = 0; i < loose.Length; i++ ) loose[i] = new Vertex { Position = new Vector3( i, 0, 4 ) };

			var indices = new uint[] { 0, 1, 2 };

			int firstVertex, firstIndex;

			fixed ( Vertex* v = loose ) firstVertex = builder.AddVertices( v, loose.Length );
			fixed ( uint* i = indices ) firstIndex = builder.AddIndices( i, indices.Length );

			builder.AddMesh( "sight", "", firstIndex, indices.Length, firstVertex, loose.Length );

			Assert.AreNotEqual( 0, builder.Save( compiled ), "the model would not write" );

			var model = CoreAssets.ModelFile.Load( compiled );
			Assert.IsNotNull( model, "what was written is not a model this core reads" );

			//Four bones, not seven: the magazine's copy of the rig is the body's rig, and only the bone it adds is new.
			Assert.AreEqual( 4, model.BoneCount,
				"the parts brought their own skeletons instead of sharing one" );

			var named = new string[4];
			for ( var i = 0; i < named.Length; i++ )
				named[i] = model.BoneName( i );

			CollectionAssert.AreEqual( new[] { "root", "spine", "grip", "round1" }, named );

			var skeleton = CoreAnimRuntime.CreateSkeleton( model );
			Assert.IsNotNull( skeleton, "the model came out with no skeleton, so nothing can animate it" );
			Assert.AreEqual( 4, skeleton.BoneCount );

			model.Dispose();
		}
		finally
		{
			builder.Dispose();

			if ( Directory.Exists( directory ) ) Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public unsafe void AMeshWithNoRigDoesNotCostTheModelItsBones()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-rigid-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		var compiled = Path.Combine( directory, "weapon.mdl_c" );

		var builder = new CoreAssets.ModelBuilder();
		Assert.IsNotNull( builder );

		try
		{
			//The unskinned part first, which is the order a definition that names a sight before its gun
			//compiles in - and the order that used to leave the skin lined up against the wrong vertices.
			var loose = new Vertex[3];
			for ( var i = 0; i < loose.Length; i++ ) loose[i] = new Vertex { Position = new Vector3( i, 0, 4 ) };

			var indices = new uint[] { 0, 1, 2 };

			int firstVertex, firstIndex;

			fixed ( Vertex* v = loose ) firstVertex = builder.AddVertices( v, loose.Length );
			fixed ( uint* i = indices ) firstIndex = builder.AddIndices( i, indices.Length );

			builder.AddMesh( "sight", "", firstIndex, indices.Length, firstVertex, loose.Length );

			var body = Write( directory, "body.smd", Reference( "root", "spine", "grip" ) );
			Assert.IsTrue( CoreSmdImport.Import( builder, body, 1.0f ) > 0, "the body would not import" );

			Assert.AreNotEqual( 0, builder.Save( compiled ), "the model would not write" );

			var model = CoreAssets.ModelFile.Load( compiled );
			Assert.IsNotNull( model, "what was written is not a model this core reads" );

			Assert.AreEqual( 3, model.BoneCount,
				"a part with no rig took the whole model's skeleton down with it" );

			//The rigid part is weighted onto the root and the body's own vertices keep the weights they were
			//imported with, which is what says the padding landed where the unskinned run is rather than at the end.
			var skin = new byte[6 * 8];

			fixed ( byte* s = skin )
			{
				Assert.AreEqual( 6, model.CopySkin( s, 6 ) );
			}

			Assert.AreEqual( 0, skin[0], "the sight should be bound to the root" );
			Assert.AreEqual( 255, skin[4], "the sight should be bound whole rather than not at all" );

			Assert.AreEqual( 2, skin[( 3 * 8 ) + 0], "the body's vertices lost the bone they were weighted to" );
			Assert.AreEqual( 255, skin[( 3 * 8 ) + 4] );

			model.Dispose();
		}
		finally
		{
			builder.Dispose();

			if ( Directory.Exists( directory ) ) Directory.Delete( directory, true );
		}
	}
}
