using System.IO;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests;

//SMD V runs bottom-up and must be flipped on import, or palette textures sample wrong colours
[TestClass]
public class SmdTexCoordsTest
{
	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-smduv-{System.Guid.NewGuid():N}{extension}" );

	//Three corners with three different V, none of them symmetric about the middle - a triangle
	//whose V read 0, 0.25 and 1 the same turned over as it does the right way up would prove nothing.
	private const string Triangle = """
		version 1
		nodes
		0 "root" -1
		end
		skeleton
		time 0
		0 0 0 0 0 0 0
		end
		triangles
		test
		0  0 0 0  0 0 1  0.125 0
		0  1 0 0  0 0 1  0.375 0.25
		0  0 1 0  0 0 1  0.625 1
		end
		""";

	[TestMethod]
	public unsafe void VComesInTurnedOver()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var smd = TempPath( ".smd" );
		var compiled = TempPath( ".mdl_c" );

		File.WriteAllText( smd, Triangle );

		try
		{
			var builder = new CoreAssets.ModelBuilder();

			try
			{
				Assert.AreEqual( 1, CoreSmdImport.Import( builder, smd, 1.0f ) );
				Assert.AreEqual( 1, builder.Save( compiled ) );
			}
			finally
			{
				builder.Dispose();
			}

			var model = CoreAssets.ModelFile.Load( compiled );
			Assert.IsNotNull( model, "the compiled model should load" );

			try
			{
				Assert.AreEqual( 3, model.VertexCount );

				var vertices = new Vertex[3];

				fixed ( Vertex* v = vertices )
				{
					model.CopyVertices( v, vertices.Length );
				}

				//U is untouched: only the axis whose origin the two conventions disagree about moves.
				Assert.AreEqual( 0.125f, vertices[0].TexCoord0.x, 0.0005f, "U should come through as written" );
				Assert.AreEqual( 0.375f, vertices[1].TexCoord0.x, 0.0005f );
				Assert.AreEqual( 0.625f, vertices[2].TexCoord0.x, 0.0005f );

				Assert.AreEqual( 1.0f, vertices[0].TexCoord0.y, 0.0005f,
					"the bottom of the file's V is the top of ours" );

				Assert.AreEqual( 0.75f, vertices[1].TexCoord0.y, 0.0005f,
					"a quarter up the file is three quarters down here" );

				Assert.AreEqual( 0.0f, vertices[2].TexCoord0.y, 0.0005f,
					"and the top of the file's V is the bottom of ours" );
			}
			finally
			{
				model.Dispose();
			}
		}
		finally
		{
			if ( File.Exists( smd ) ) File.Delete( smd );
			if ( File.Exists( compiled ) ) File.Delete( compiled );
		}
	}

	//A surface name starting with a digit (e.g. "12gauge") must not be parsed as a vertex line
	[TestMethod]
	public void ASurfaceNamedLikeANumberIsStillASurface()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var smd = TempPath( ".smd" );

		File.WriteAllText( smd, Triangle.Replace( "test", "12gauge" ) );

		try
		{
			var builder = new CoreAssets.ModelBuilder();

			try
			{
				Assert.AreEqual( 1, CoreSmdImport.Import( builder, smd, 1.0f ),
					"the triangle went nowhere - its surface was read as a broken corner" );

				Assert.AreEqual( "12gauge", builder.MeshMaterial( 0 ) );
			}
			finally
			{
				builder.Dispose();
			}
		}
		finally
		{
			if ( File.Exists( smd ) ) File.Delete( smd );
		}
	}

	//Compiled models record their importer version so importer fixes invalidate old builds
	[TestMethod]
	public void ACompiledModelSaysWhichImportersBuiltIt()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var smd = TempPath( ".smd" );
		var compiled = TempPath( ".mdl_c" );

		File.WriteAllText( smd, Triangle );

		try
		{
			var builder = new CoreAssets.ModelBuilder();

			try
			{
				Assert.AreEqual( 1, CoreSmdImport.Import( builder, smd, 1.0f ) );
				Assert.AreEqual( 1, builder.Save( compiled ) );
			}
			finally
			{
				builder.Dispose();
			}

			var mine = CoreAssets.ModelFile.ImportGeneration;

			Assert.AreNotEqual( 0u, mine, "a core with importers in it has a generation" );

			Assert.AreEqual( mine, CoreAssets.ModelFile.ImportGenerationOf( compiled ),
				"a model just built should carry the generation that built it" );

			//What one from before the field meant anything looks like - and what anything that is not one of ours looks like.
			var bytes = File.ReadAllBytes( compiled );

			bytes[12] = 0;
			bytes[13] = 0;
			bytes[14] = 0;
			bytes[15] = 0;

			var older = TempPath( ".mdl_c" );

			File.WriteAllBytes( older, bytes );

			try
			{
				Assert.AreEqual( 0u, CoreAssets.ModelFile.ImportGenerationOf( older ),
					"a model from before the stamp should read as needing a rebuild" );

				//And it still loads: this is a stamp, not a version.
				var stale = CoreAssets.ModelFile.Load( older );

				Assert.IsNotNull( stale, "an older stamp must not make the file unreadable" );

				stale.Dispose();
			}
			finally
			{
				if ( File.Exists( older ) ) File.Delete( older );
			}

			Assert.AreEqual( 0u, CoreAssets.ModelFile.ImportGenerationOf( smd ),
				"something that is not one of ours has no generation" );
		}
		finally
		{
			if ( File.Exists( smd ) ) File.Delete( smd );
			if ( File.Exists( compiled ) ) File.Delete( compiled );
		}
	}
}
