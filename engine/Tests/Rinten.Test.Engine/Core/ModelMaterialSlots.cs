using System;
using System.IO;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests.Core;

//Material-to-mesh mapping: explicit pair, then file named after the surface, then list order
[TestClass]
[DoNotParallelize]
public class ModelMaterialSlotsTests
{
	//Four meshes over two surfaces, in the shape the weapons here are: the surfaces
	//alternate, so anything counting meshes rather than surfaces goes wrong on the third one.
	private const string Parts = """
		version 1
		nodes
		0 "root" -1
		end
		skeleton
		time 0
		0 0 0 0 0 0 0
		end
		triangles
		lowpoly_metal
		0  0 0 0  0 0 1  0 0
		0  1 0 0  0 0 1  1 0
		0  0 1 0  0 0 1  0 1
		lowpoly_plastic
		0  2 0 0  0 0 1  0 0
		0  3 0 0  0 0 1  1 0
		0  3 1 0  0 0 1  0 1
		lowpoly_metal.005
		0  4 0 0  0 0 1  0 0
		0  5 0 0  0 0 1  1 0
		0  5 1 0  0 0 1  0 1
		ammo1
		0  6 0 0  0 0 1  0 0
		0  7 0 0  0 0 1  1 0
		0  7 1 0  0 0 1  0 1
		end
		""";

	private static string TempPath( string directory, string name )
		=> Path.Combine( directory, name );

	//The material of every mesh of a compiled model, in order.
	private static string[] MaterialsOf( string compiled )
	{
		var model = CoreAssets.ModelFile.Load( compiled );

		Assert.IsNotNull( model, $"{compiled} should load" );

		try
		{
			var count = model.MeshCount;
			var found = new string[count];

			for ( var i = 0; i < count; i++ )
			{
				found[i] = model.MeshMaterial( i );
			}

			return found;
		}
		finally
		{
			model.Dispose();
		}
	}

	private static string Build( string json )
	{
		var directory = Path.Combine( Path.GetTempPath(), $"rinten-slots-{Guid.NewGuid():N}" );

		Directory.CreateDirectory( directory );

		File.WriteAllText( TempPath( directory, "parts.smd" ), Parts );
		File.WriteAllText( TempPath( directory, "thing.mdl" ), json );

		return directory;
	}

	[TestMethod]
	public void ASurfaceKeepsItsMaterialHoweverManyMeshesWearIt()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		//Named pairs for two of the surfaces, a material named after the third, and nothing at all for the fourth.
		var directory = Build( """
			{
			  "Meshes": [ { "File": "parts.smd", "Scale": 1 } ],
			  "Materials": [ "surfaces/ammo1.mat" ],
			  "MaterialRemaps": [
			    { "Surface": "lowpoly_metal", "Material": "surfaces/metal.mat" },
			    { "Surface": "lowpoly_plastic", "Material": "surfaces/plastic.mat" }
			  ]
			}
			""" );

		try
		{
			var definition = TempPath( directory, "thing.mdl" );

			Assert.IsTrue( Rinten.ModelDefinitionCompiler.CompileToFile( definition ),
				"the definition should compile" );

			var materials = MaterialsOf( Path.ChangeExtension( definition, ".mdl_c" ) );

			Assert.AreEqual( 4, materials.Length, "one mesh per surface in the export" );

			Assert.AreEqual( "surfaces/metal.mat", materials[0], "the pair the definition names" );
			Assert.AreEqual( "surfaces/plastic.mat", materials[1] );

			Assert.AreEqual( "surfaces/metal.mat", materials[2],
				"lowpoly_metal.005 is lowpoly_metal - Blender's duplicate suffix is not a surface" );

			Assert.AreEqual( "surfaces/ammo1.mat", materials[3],
				"a material named after the surface belongs to it, wherever it sits in the list" );
		}
		finally
		{
			try { Directory.Delete( directory, true ); } catch { }
		}
	}

	[TestMethod]
	public void WithoutNamesTheListIsOnePerSurfaceNotOnePerMesh()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		//Three materials, four meshes, three surfaces once the duplicate suffix is discounted. One per mesh would leave the last one bare.
		var directory = Build( """
			{
			  "Meshes": [ { "File": "parts.smd", "Scale": 1 } ],
			  "Materials": [ "surfaces/one.mat", "surfaces/two.mat", "surfaces/three.mat" ]
			}
			""" );

		try
		{
			var definition = TempPath( directory, "thing.mdl" );

			Assert.IsTrue( Rinten.ModelDefinitionCompiler.CompileToFile( definition ) );

			var materials = MaterialsOf( Path.ChangeExtension( definition, ".mdl_c" ) );

			Assert.AreEqual( "surfaces/one.mat", materials[0], "first surface, first material" );
			Assert.AreEqual( "surfaces/two.mat", materials[1], "second surface, second material" );

			Assert.AreEqual( "surfaces/one.mat", materials[2],
				"the third mesh is the first surface again, so it takes the first material - " +
				"counted by mesh it would have taken the third" );

			Assert.AreEqual( "surfaces/three.mat", materials[3], "third surface, third material" );
		}
		finally
		{
			try { Directory.Delete( directory, true ); } catch { }
		}
	}

	//Nor is the same model again with fewer triangles in it. An export carries its levels of detail as files of their own, and the definitions
	//here list them all as meshes - so the shape and its own coarser copy were drawn in the same place, showing through wherever the two disagree.
	[TestMethod]
	public void ALevelOfDetailIsNotDrawnOverTheModel()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		var directory = Build( """
			{
			  "Meshes": [
			    { "File": "parts.smd", "Scale": 1 },
			    { "File": "partslod.smd", "Scale": 1 },
			    { "File": "parts_lod1.smd", "Scale": 1 }
			  ],
			  "Materials": []
			}
			""" );

		try
		{
			File.WriteAllText( TempPath( directory, "partslod.smd" ), Parts );
			File.WriteAllText( TempPath( directory, "parts_lod1.smd" ), Parts );

			var definition = TempPath( directory, "thing.mdl" );

			Assert.IsTrue( Rinten.ModelDefinitionCompiler.CompileToFile( definition ) );

			var materials = MaterialsOf( Path.ChangeExtension( definition, ".mdl_c" ) );

			Assert.AreEqual( 4, materials.Length,
				"the coarser copies were drawn as well, so the model came out three times over" );
		}
		finally
		{
			try { Directory.Delete( directory, true ); } catch { }
		}
	}

	//Hulls survive a rebuild from the definition, and hull-only meshes are not drawn
	[TestMethod]
	public void AMeshThatIsOnlyAHullIsNotDrawn()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		var directory = Build( """
			{
			  "Meshes": [ { "File": "parts.smd", "Scale": 1 }, { "File": "shape.smd", "Scale": 1 } ],
			  "Materials": [],
			  "Hulls": [
			    { "__type": "RenderHull", "File": "shape.smd", "Scale": 1, "MaxVertices": 0 }
			  ]
			}
			""" );

		try
		{
			File.WriteAllText( TempPath( directory, "shape.smd" ), Parts );

			var definition = TempPath( directory, "thing.mdl" );

			Assert.IsTrue( Rinten.ModelDefinitionCompiler.CompileToFile( definition ) );

			var materials = MaterialsOf( Path.ChangeExtension( definition, ".mdl_c" ) );

			Assert.AreEqual( 4, materials.Length,
				"the hull mesh was drawn as well, so the model came out twice over" );
		}
		finally
		{
			try { Directory.Delete( directory, true ); } catch { }
		}
	}

	[TestMethod]
	public void AHullNamedByTheDefinitionIsBuiltWithIt()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		var directory = Build( """
			{
			  "Meshes": [ { "File": "parts.smd", "Scale": 1 } ],
			  "Materials": [],
			  "Hulls": [
			    { "__type": "RenderHull", "File": "parts.smd", "Scale": 1, "MaxVertices": 0 }
			  ]
			}
			""" );

		try
		{
			var definition = TempPath( directory, "thing.mdl" );

			Assert.IsTrue( Rinten.ModelDefinitionCompiler.CompileToFile( definition ) );

			var model = CoreAssets.ModelFile.Load( Path.ChangeExtension( definition, ".mdl_c" ) );

			Assert.IsNotNull( model );

			try
			{
				Assert.IsTrue( model.BodyCount > 0 &&
					model.ShapeCount( 0 ) > 0,
					"the definition names a hull and the model came back with none" );
			}
			finally
			{
				model.Dispose();
			}
		}
		finally
		{
			try { Directory.Delete( directory, true ); } catch { }
		}
	}
}
