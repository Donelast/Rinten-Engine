using System.IO;
using Rinten.Porting.SBox;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests;

//Ported model materials come from the surface table or from the use_global_default switch
[TestClass]
public class SBoxModelMaterialsTest
{
	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-vmdl-{System.Guid.NewGuid():N}{extension}" );

	private static string Document( string group ) => $$"""
		<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc29:version{3cec427c-1b0e-4d48-a90a-0436f33a6041} -->
		{
			rootNode =
			{
				_class = "RootNode"
				children =
				[
					{
						_class = "MaterialGroupList"
						children =
						[
							{{group}}
						]
					},
				]
			}
		}
		""";

	//The knife's, and the grenade's world model: no table at all, one material for everything.
	private const string GlobalOnly = """
		{
			_class = "DefaultMaterialGroup"
			remaps = [  ]
			use_global_default = true
			global_default_material = "weapons/pallete_wood.vmat"
		},
		""";

	//The AWP's: a table, and a global default sitting there switched off.
	private const string TableOnly = """
		{
			_class = "DefaultMaterialGroup"
			remaps =
			[
				{
					from = "lowpoly_metal.vmat"
					to = "weapons/pallete_metal.vmat"
				},
				{
					from = "lowpoly_glow.vmat"
					to = "weapons/emission.vmat"
				},
			]
			use_global_default = false
			global_default_material = "materials/default.vmat"
		},
		""";

	//The doors': switched on, and pointed at the legacy format's own default.
	private const string GlobalIsTheirs = """
		{
			_class = "DefaultMaterialGroup"
			remaps = [  ]
			use_global_default = true
			global_default_material = "materials/default.vmat"
		},
		""";

	private static SBoxMaterials.Assignment Read( string group )
	{
		var file = TempPath( ".vmdl" );

		File.WriteAllText( file, Document( group ) );

		try
		{
			return SBoxMaterials.Read( file );
		}
		finally
		{
			File.Delete( file );
		}
	}

	[TestMethod]
	public void AModelThatWearsOneMaterialThroughoutSaysSoWithTheSwitch()
	{
		var read = Read( GlobalOnly );

		Assert.AreEqual( 0, read.Remaps.Count, "there is no table on this one" );
		Assert.AreEqual( "weapons/pallete_wood.mat", read.Default );
	}

	[TestMethod]
	public void ADefaultNobodySwitchedOnIsNotADefault()
	{
		var read = Read( TableOnly );

		//Every document writes the field. Reading it without the switch puts materials/default.mat on every rifle in the project.
		Assert.AreEqual( "", read.Default );

		Assert.AreEqual( "weapons/pallete_metal.mat", read.Remaps["lowpoly_metal.vmat"] );
		Assert.AreEqual( "weapons/pallete_metal.mat", read.Remaps["lowpoly_metal"], "by bare name too" );
		Assert.AreEqual( "weapons/emission.mat", read.Remaps["lowpoly_glow"] );
	}

	[TestMethod]
	public void TheirOwnDefaultIsNoMaterialAtAll()
	{
		var read = Read( GlobalIsTheirs );

		//It is not carried across, so writing it down is a model asking for a file the
		//project does not have. Empty is our own default, which is the same picture.
		Assert.AreEqual( "", read.Default );
	}

	//And what the compile does with it: a surface the table does not name wears the default rather than its own name.
	[TestMethod]
	public unsafe void ASurfaceNothingNamesWearsTheDefault()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var smd = TempPath( ".smd" );

		File.WriteAllText( smd, """
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
			end
			""" );

		var builder = new CoreAssets.ModelBuilder();

		Assert.IsNotNull( builder );

		try
		{
			var added = CoreSmdImport.Import( builder, smd, 1.0f, null, addSkeleton: true,
				fallback: "weapons/pallete_wood.mat" );

			Assert.AreEqual( 1, added );

			var material = builder.MeshMaterial( 0 );

			//Without it the surface name goes through as written and the model is drawn asking for a file called lowpoly_metal.
			Assert.AreEqual( "weapons/pallete_wood.mat", material );
		}
		finally
		{
			builder.Dispose();
			File.Delete( smd );
		}
	}
}
