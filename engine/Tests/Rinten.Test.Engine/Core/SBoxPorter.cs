using System.IO;
using System.Text.Json.Nodes;
using Rinten.Porting;
using Rinten.Porting.SBox;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests;

//The porter's guardrails. Porting itself needs a loaded project, so it is checked by running it. What is checked here is what it
//refuses to do - because the failure that matters is not "the port did not work", it is "the port quietly wrote over something".
[TestClass]
public class ProjectPorterTest
{
	private static string TempDirectory( string name )
	{
		var path = Path.Combine( Path.GetTempPath(), $"rinten-porter-{name}-{System.Guid.NewGuid():N}" );
		Directory.CreateDirectory( path );
		return path;
	}

	//A port never writes over assets that are already there. Whatever went wrong, the thing already in
	//the destination has to survive it - even where the source has a file at the very same Assets path.
	[TestMethod]
	public void RefusesToPortOverExistingAssets()
	{
		var source = TempDirectory( "source" );
		var destination = TempDirectory( "destination" );

		try
		{
			var assets = Path.Combine( destination, "Assets" );
			Directory.CreateDirectory( assets );
			File.WriteAllText( Path.Combine( assets, "already-here.txt" ), "do not lose me" );

			Directory.CreateDirectory( Path.Combine( source, "Assets" ) );
			File.WriteAllText( Path.Combine( source, "Assets", "already-here.txt" ), "overwrite me if you dare" );

			SBoxPorter.Port( source, destination );

			Assert.AreEqual( "do not lose me", File.ReadAllText( Path.Combine( assets, "already-here.txt" ) ),
				"what was there should be untouched" );
		}
		finally
		{
			Directory.Delete( source, true );
			Directory.Delete( destination, true );
		}
	}

	//A re-port into a filled destination still copies code added since the last port
	[TestMethod]
	public void FillsInCodeAddedSinceTheLastPort()
	{
		var source = TempDirectory( "source" );
		var destination = TempDirectory( "destination" );

		try
		{
			Directory.CreateDirectory( Path.Combine( destination, "Assets" ) );
			File.WriteAllText( Path.Combine( destination, "Assets", "already-here.txt" ), "already ported" );

			var libraryCode = Path.Combine( source, "Libraries", "someone.tool", "Code", "Tool.cs" );
			Directory.CreateDirectory( Path.GetDirectoryName( libraryCode ) );
			File.WriteAllText( libraryCode, "public class Tool {}" );

			var editorCode = Path.Combine( source, "Editor", "Panel.cs" );
			Directory.CreateDirectory( Path.GetDirectoryName( editorCode ) );
			File.WriteAllText( editorCode, "public class Panel {}" );

			var report = SBoxPorter.Port( source, destination );

			Assert.IsTrue( File.Exists( Path.Combine( destination, "Libraries", "someone.tool", "Code", "Tool.cs" ) ),
				"a library the first port never saw should still come across" );
			Assert.IsTrue( File.Exists( Path.Combine( destination, "Editor", "Panel.cs" ) ),
				"and so should editor-only code" );
			Assert.IsTrue( report.FilesCopied > 0, "the report should say something was written" );
		}
		finally
		{
			Directory.Delete( source, true );
			Directory.Delete( destination, true );
		}
	}

	//A destination that has a project file but no assets is the normal case - the project creator makes one
	//and then asks for the port. That must be allowed, and the project file it wrote must be left alone.
	[TestMethod]
	public void PortsIntoAFreshlyCreatedProject()
	{
		var source = TempDirectory( "source" );
		var destination = TempDirectory( "created" );

		try
		{
			var config = Path.Combine( destination, "myproject.rinproj" );
			File.WriteAllText( config, "{ \"Ident\": \"myproject\" }" );

			File.WriteAllBytes( Path.Combine( source, "art.png" ), new byte[] { 1, 2 } );

			var report = SBoxPorter.Port( source, destination );

			Assert.AreEqual( 1, report.FilesCopied, "the art should have come across" );
			Assert.AreEqual( config, report.ProjectFile, "the creator's project file should be kept" );
			Assert.AreEqual( "{ \"Ident\": \"myproject\" }", File.ReadAllText( config ),
				"and not written over" );

			Assert.AreEqual( 1, Directory.EnumerateFiles( destination, "*.rinproj" ).Count(),
				"and there should not be a second one" );
		}
		finally
		{
			Directory.Delete( source, true );
			Directory.Delete( destination, true );
		}
	}

	//Texture paths and the mesh a model was built from only exist in the text the artist wrote - by the time the engine has loaded
	//either, they are compiled objects that no longer know where they came from. So the porter reads the files, and this is what it reads.
	[TestMethod]
	public void ReadsWhatTheArtistWrote()
	{
		var directory = TempDirectory( "authored" );

		try
		{
			var material = Path.Combine( directory, "asphalt.vmat" );

			//The shape a real material is in: quoted values, comment banners, tabs, and parameters we do not care about mixed in.
			File.WriteAllText( material, """
				// THIS FILE IS AUTO-GENERATED

				Layer0
				{
					shader "map/road_material.shader"

					//---- Color ----
					g_flOpacity "1.000"
					TextureColor "map/materials/asphalt_color.jpg"
					TextureNormal "map/materials/asphalt_normalgl.jpg"
					TextureRoughness "map/materials/asphalt_roughness.jpg"
				}
				""" );

			var values = SBoxPorter.ReadAuthoredValues( material );

			Assert.AreEqual( "map/road_material.shader", SBoxPorter.Authored( values, "shader" ) );
			Assert.AreEqual( "map/materials/asphalt_color.jpg", SBoxPorter.Authored( values, "TextureColor" ) );
			Assert.AreEqual( "map/materials/asphalt_normalgl.jpg", SBoxPorter.Authored( values, "TextureNormal" ) );
			Assert.AreEqual( "", SBoxPorter.Authored( values, "TextureEmissiveMask" ),
				"a parameter that is not there is empty, not a guess" );

			//Map files quote both the name and the path, with tabs between them.
			var quoted = Path.Combine( directory, "library.vmat" );

			File.WriteAllText( quoted, """
				Layer0
				{
					"shader"		"shaders/complex.shader"
					"TextureColor"		"materials/default/default_color.tga"
					"TextureNormal"		"materials/default/default_normal.tga"
				}
				""" );

			var quotedValues = SBoxPorter.ReadAuthoredValues( quoted );

			Assert.AreEqual( "shaders/complex.shader", SBoxPorter.Authored( quotedValues, "shader" ) );
			Assert.AreEqual( "materials/default/default_color.tga", SBoxPorter.Authored( quotedValues, "TextureColor" ) );
			Assert.AreEqual( "materials/default/default_normal.tga", SBoxPorter.Authored( quotedValues, "TextureNormal" ) );

			//A model names several meshes - one per level of detail. The first is what it is made of; the rest are reductions of it.
			var model = Path.Combine( directory, "light_pole.vmdl" );

			File.WriteAllText( model, """
				<!-- kv3 -->
				{
					rootNode =
					{
						children =
						[
							{
								filename = "map/models/light_pole/light_pole.fbx"
							},
							{
								filename = "map/models/light_pole/light_pole_lod1.fbx"
							},
						]
					}
				}
				""" );

			Assert.AreEqual( "map/models/light_pole/light_pole.fbx", SBoxPorter.AuthoredMesh( model ),
				"the first mesh, not a reduction of it" );

			Assert.AreEqual( "", SBoxPorter.AuthoredMesh( material ),
				"a material names no mesh" );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	//Resolve a model's export path here; a missing mesh file fails the build and shows the error model
	[TestMethod]
	public void FindsTheExportAModelNamesHoweverItNamesIt()
	{
		var root = TempDirectory( "exports" );

		try
		{
			var assets = Path.Combine( root, "Assets" );
			var models = Path.Combine( assets, "models", "pole" );
			Directory.CreateDirectory( models );

			var model = Path.Combine( models, "pole.vmdl" );
			File.WriteAllText( model, "{}" );

			void Art( string relative )
			{
				var path = Path.Combine( assets, relative );
				Directory.CreateDirectory( Path.GetDirectoryName( path ) );
				File.WriteAllBytes( path, new byte[] { 1 } );
			}

			//The usual: named from the root of the project's assets.
			Art( Path.Combine( "models", "pole", "pole.fbx" ) );

			Assert.AreEqual(
				Path.Combine( models, "pole.fbx" ),
				SBoxPorter.FindExport( "models/pole/pole.fbx", model, assets ),
				"a name from the assets root is the common one" );

			//And named from the model's own folder, which some exporters write.
			Art( Path.Combine( "models", "pole", "lod1.fbx" ) );

			Assert.AreEqual(
				Path.Combine( models, "lod1.fbx" ),
				SBoxPorter.FindExport( "lod1.fbx", model, assets ),
				"a name from the model's own folder should be found too" );

			//A name that means nothing is nothing - not a path that happens to be somewhere, which is what gets written into the ported model.
			Assert.IsNull( SBoxPorter.FindExport( "models/pole/missing.fbx", model, assets ) );
			Assert.IsNull( SBoxPorter.FindExport( "", model, assets ) );

			//The walk up stops at the assets root. A file outside the project that happens to match the name is
			//a coincidence, and pointing the new project at it makes a project that only builds on this machine.
			var outside = Path.Combine( root, "elsewhere" );
			Directory.CreateDirectory( outside );
			File.WriteAllBytes( Path.Combine( outside, "stray.fbx" ), new byte[] { 1 } );

			Assert.IsNull( SBoxPorter.FindExport( "elsewhere/stray.fbx", model, assets ),
				"nothing above the assets root should be found by walking" );

			//Unless it says so outright: an absolute path is somebody's own machine, and either it is still there or the name is worth nothing.
			var absolute = Path.Combine( outside, "stray.fbx" );

			Assert.AreEqual( absolute, SBoxPorter.FindExport( absolute, model, assets ) );
			Assert.IsNull( SBoxPorter.FindExport( Path.Combine( outside, "gone.fbx" ), model, assets ) );
		}
		finally
		{
			Directory.Delete( root, true );
		}
	}

	//A model does not contain its animations, it declares them - usually in a list living in
	//another file. Those names are what the game asks for, so they have to come across exactly.
	[TestMethod]
	public void ReadsTheAnimationsAModelDeclares()
	{
		var assets = TempDirectory( "animations" );

		try
		{
			var model = Path.Combine( assets, "citizen.vmdl" );

			//The shape a real model is in: an animation list that is mostly a reference to somewhere else, with one clip written inline.
			File.WriteAllText( model, """
				<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} -->
				{
					rootNode =
					{
						children =
						[
							{
								_class = "AnimationList"
								children =
								[
									{
										_class = "Folder"
										name = "Imported"
										children =
										[
											{
												_class = "AnimFile"
												name = "idle_01"
												activity_name = ""
												looping = true
												delta = false
												source_filename = "models/citizen/animations/Citizen@Idle_01.fbx"
												start_frame = -1
												end_frame = -1
												take = 0
												reverse = false
											},
										]
									},
								]
								target_file = "models/citizen/prefabs/list.vmdl_prefab"
							},
						]
					}
				}
				""" );

			var prefabs = Path.Combine( assets, "models", "citizen", "prefabs" );
			Directory.CreateDirectory( prefabs );

			File.WriteAllText( Path.Combine( prefabs, "list.vmdl_prefab" ), """
				<!-- kv3 -->
				{
					rootNode =
					{
						children =
						[
							{
								_class = "AnimationList"
								children =
								[
									{
										_class = "AnimFile"
										name = "walk_N"
										looping = true
										delta = false
										source_filename = "models/citizen/animations/Citizen@Walk_N.fbx"
										take = 0
									},
									{
										_class = "1DBlend"
										name = "aim_pitch"
										looping = false
										blendList =
										[
											{
												name = "up"
												weight = 0.0
											},
										]
									},
									{
										_class = "AnimFile"
										name = "lean_add"
										looping = false
										delta = true
										source_filename = "models/citizen/animations/Citizen@Lean.fbx"
										start_frame = 4
										end_frame = 20
									},
								]
							},
						]
					}
				}
				""" );

			var (found, composites) = SBoxPorter.ReadAnimations( model, assets );

			//The blend is a sequence too, and there is no file behind it - so it is named rather than ported, and not lost.
			Assert.AreEqual( 1, composites.Count );
			Assert.IsTrue( composites[0].StartsWith( "aim_pitch" ), composites[0] );

			Assert.AreEqual( 3, found.Count,
				$"three clips, and not the blend that is made of them - got {string.Join( ", ", found.Select( x => x.Name ) )}" );

			var idle = found.First( x => x.Name == "idle_01" );
			Assert.AreEqual( "models/citizen/animations/Citizen@Idle_01.fbx", idle.Source );
			Assert.IsTrue( idle.Looping );
			Assert.IsFalse( idle.Additive );

			//The list the model points at is where nearly all of them live.
			var walk = found.First( x => x.Name == "walk_N" );
			Assert.AreEqual( "models/citizen/animations/Citizen@Walk_N.fbx", walk.Source );

			//A difference from a pose rather than a pose, and a slice of its export - both things the definition has to carry.
			var lean = found.First( x => x.Name == "lean_add" );
			Assert.IsTrue( lean.Additive );
			Assert.IsFalse( lean.Looping );
			Assert.AreEqual( 4, lean.FirstFrame );
			Assert.AreEqual( 20, lean.LastFrame );

			//A model with no animation list has no animations, rather than every AnimFile it can see.
			var prop = Path.Combine( assets, "crate.vmdl" );
			File.WriteAllText( prop, "{ rootNode = { children = [ { _class = \"RenderMeshList\" } ] } }" );

			Assert.AreEqual( 0, SBoxPorter.ReadAnimations( prop, assets ).Clips.Count );
		}
		finally
		{
			Directory.Delete( assets, true );
		}
	}

	//A ported project has none of the legacy format files in it. What can be read becomes ours; the rest
	//is counted, and the graphs are named one by one because each is somebody's work to redo.
	[TestMethod]
	public void LeavesSourceFormatsBehindAndSaysSo()
	{
		var source = TempDirectory( "leftovers" );
		var destination = TempDirectory( "cleaned" );

		try
		{
			void Write( string relative )
			{
				var path = Path.Combine( source, relative );
				Directory.CreateDirectory( Path.GetDirectoryName( path ) );
				File.WriteAllText( path, "{}" );
			}

			Write( @"Assets\models\citizen\citizen.vanmgrph" );
			Write( @"Assets\models\citizen\citizen_human_f.vanmgrph" );
			Write( @"Assets\models\citizen\prefabs\list.vmdl_prefab" );
			Write( @"Assets\models\citizen\animations\walk.vanim" );
			Write( @"Assets\textures\brick.vtex" );

			//Art and code are nobody's but the project's, and come across.
			Write( @"Assets\textures\brick.png" );
			Write( @"Code\Player.cs" );

			var report = SBoxPorter.Port( source, destination );

			var assets = Path.Combine( destination, "Assets" );

			Assert.IsTrue( File.Exists( Path.Combine( assets, "textures", "brick.png" ) ), "art comes across" );
			Assert.IsTrue( File.Exists( Path.Combine( destination, "Code", "Player.cs" ) ), "and code" );

			Assert.IsFalse( File.Exists( Path.Combine( assets, "textures", "brick.vtex" ) ),
				"a s&box texture description has nothing to open it" );
			Assert.IsFalse( Directory.Exists( Path.Combine( assets, "models", "citizen", "prefabs" ) ),
				"nor a model prefab" );

			Assert.AreEqual( 2, report.FilesCopied, "the png and the cs, and nothing else" );

			Assert.AreEqual( 2, report.Graphs.Count, "both graphs should be named" );
			Assert.IsTrue( report.Graphs.Any( x => x.EndsWith( "citizen.vanmgrph" ) ) );

			Assert.AreEqual( 1, report.LeftBehind[".vtex"] );
			Assert.AreEqual( 1, report.LeftBehind[".vanim"] );
			Assert.AreEqual( 1, report.LeftBehind[".vmdl_prefab"] );
		}
		finally
		{
			Directory.Delete( source, true );
			Directory.Delete( destination, true );
		}
	}

	[TestMethod]
	public void RefusesASourceThatIsNotThere()
	{
		var destination = TempDirectory( "nowhere" );

		try
		{
			var report = SBoxPorter.Port( Path.Combine( destination, "no-such-source" ), destination );

			Assert.IsTrue( report.Skipped.Any( x => x.Contains( "no such directory" ) ) );
			Assert.AreEqual( 0, report.FilesCopied );
		}
		finally
		{
			Directory.Delete( destination, true );
		}
	}

	//A project is more than its assets. Code, shaders and settings come across too, each
	//keeping where it was - a file that lands somewhere else is a file nothing can find.
	[TestMethod]
	public void PortsTheWholeProjectNotJustAssets()
	{
		var source = TempDirectory( "project" );
		var destination = Path.Combine( TempDirectory( "ported" ), "new-project" );

		try
		{
			void Write( string relative, params byte[] content )
			{
				var path = Path.Combine( source, relative );
				Directory.CreateDirectory( Path.GetDirectoryName( path ) );
				File.WriteAllBytes( path, content );
			}

			//Assets keep their place relative to the Assets directory.
			Write( @"Assets\textures\walls\brick.png", 1, 2, 3 );
			Write( @"Assets\shaders\water.shader", 4 );

			//Everything else keeps its place relative to the project.
			Write( @"Code\Player\Movement.cs", 5 );
			Write( @"Editor\Tool.cs", 6 );
			Write( @"ProjectSettings\Input.json", 7 );

			//Build output and editor state are not part of the project.
			Write( @"Code\obj\Debug\Movement.dll", 8 );
			Write( @"Assets\brick.vtex_c", 9 );
			Write( @".vs\state.bin", 10 );
			Write( @"old.sbproj", 11 );

			//Libraries are found by our project extension; left as .sbproj they never register or compile
			var library = Path.Combine( source, @"Libraries\someone.tool\tool.sbproj" );
			Directory.CreateDirectory( Path.GetDirectoryName( library ) );
			File.WriteAllText( library,
				"""{ "Title": "Tool", "Type": "library", "Org": "someone", "Ident": "tool" }""" );

			Write( @"Libraries\someone.tool\Code\Tool.cs", 13 );

			var report = SBoxPorter.Port( source, destination );

			var brick = Path.Combine( destination, "Assets", "textures", "walls", "brick.png" );
			Assert.IsTrue( File.Exists( brick ), "art should keep the folders it was in" );
			CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, File.ReadAllBytes( brick ), "byte for byte" );

			Assert.IsTrue( File.Exists( Path.Combine( destination, "Assets", "shaders", "water.shader" ) ),
				"the project's own shaders are its own code and come across" );

			Assert.IsTrue( File.Exists( Path.Combine( destination, "Code", "Player", "Movement.cs" ) ),
				"code should come across" );
			Assert.IsTrue( File.Exists( Path.Combine( destination, "Editor", "Tool.cs" ) ) );
			Assert.IsTrue( File.Exists( Path.Combine( destination, "ProjectSettings", "Input.json" ) ) );

			Assert.IsFalse( File.Exists( Path.Combine( destination, "Code", "obj", "Debug", "Movement.dll" ) ),
				"build output should not" );
			Assert.IsFalse( File.Exists( Path.Combine( destination, "Assets", "brick.vtex_c" ) ),
				"nor compiled assets" );
			Assert.IsFalse( Directory.Exists( Path.Combine( destination, ".vs" ) ),
				"nor editor state" );

			//One project file at the root, the one written for the new project - not the original project's file.
			Assert.AreEqual( 1, Directory.EnumerateFiles( destination, "*.rinproj" ).Count(),
				"the new project file should be the only one at the root" );

			Assert.AreEqual( 0, Directory.EnumerateFiles( destination, "*.sbproj" ).Count(),
				"the old project file should not have been carried over" );

			//A library's own comes with it, written as one of ours - which is the whole of what makes its code compile rather than just sit there.
			Assert.IsTrue( File.Exists( Path.Combine( destination, "Libraries", "someone.tool", "tool.rinproj" ) ),
				"a library's project file must come across as one this engine looks for" );
			Assert.IsFalse( File.Exists( Path.Combine( destination, "Libraries", "someone.tool", "tool.sbproj" ) ),
				"and not as the one nothing reads" );

			Assert.AreEqual( "library",
				JsonNode.Parse( File.ReadAllText( Path.Combine( destination, "Libraries", "someone.tool", "tool.rinproj" ) ) )
					?["Type"]?.GetValue<string>(),
				"and it is still a library, so Project.Libraries picks it up" );

			Assert.IsTrue( File.Exists( Path.Combine( destination, "Libraries", "someone.tool", "Code", "Tool.cs" ) ) );

			Assert.AreEqual( 7, report.FilesCopied, "seven files, and none of the four that were skipped" );
		}
		finally
		{
			Directory.Delete( source, true );
			Directory.Delete( Path.GetDirectoryName( destination ), true );
		}
	}

	//A colour value like TextureColor "[1 1 1 0]" must not be read as a texture path
	[TestMethod]
	public void AColourInATextureSlotIsNotAPath()
	{
		var directory = TempDirectory( "slots" );

		try
		{
			var material = Path.Combine( directory, "wall.vmat" );

			File.WriteAllText( material, """
				Layer0
				{
					shader "complex.shader"
					TextureColor "[1.000000 0.500000 0.250000 1.000000]"
					TextureNormal "materials/wall_normal.tga"
					TextureRoughness "[0.700000 0.700000 0.700000 0.000000]"
				}
				""" );

			var authored = SBoxPorter.ReadAuthoredValues( material );
			var slots = SBoxMaterialSlots.Read( authored );

			var colour = SBoxMaterialSlots.Texture( slots, SBoxMaterialSlots.Slot.Colour, out var constant );

			Assert.AreEqual( "", colour, "a constant is not a texture" );
			Assert.IsNotNull( constant, "and it is not thrown away either" );
			Assert.AreEqual( 1.0f, constant.Value.r, 0.001f );
			Assert.AreEqual( 0.5f, constant.Value.g, 0.001f );
			Assert.AreEqual( 0.25f, constant.Value.b, 0.001f );

			var normal = SBoxMaterialSlots.Texture( slots, SBoxMaterialSlots.Slot.Normal, out var none );

			Assert.AreEqual( "materials/wall_normal.tga", normal, "a path still comes through" );
			Assert.IsNull( none );

			Assert.AreEqual( "", SBoxMaterialSlots.Texture( slots, SBoxMaterialSlots.Slot.Roughness, out _ ) );

			var missing = SBoxMaterialSlots.Texture( slots, SBoxMaterialSlots.Slot.Emission, out var absent );

			Assert.AreEqual( "", missing, "a slot nobody wrote is empty, not a guess" );
			Assert.IsNull( absent );

			//A material written for a shader somebody wrote names its slots whatever that shader declares, and the standard names are not in it at all. Read by
			//those alone it comes out with no maps, and the model draws with whatever happens to be bound - which looks like a corrupted texture and is an empty one.
			var swordFile = Path.Combine( directory, "sword.vmat" );

			File.WriteAllText( swordFile, """
				Layer0
				{
					shader "shaders/pixel_shader.shader"

					AO "[1.000000 1.000000 1.000000 0.000000]"
					Color "models/weapons/sword/sword_color.png"
					Metal "[0.000000 0.000000 0.000000 0.000000]"
					Normal "materials/default/default_normal.tga"
					Rough "[1.000000 1.000000 1.000000 0.000000]"
					SelfIllum "materials/default/default_selfillum.tga"
				}
				""" );

			var custom = SBoxMaterialSlots.Read( SBoxPorter.ReadAuthoredValues( swordFile ) );

			Assert.AreEqual( "models/weapons/sword/sword_color.png",
				SBoxMaterialSlots.Texture( custom, SBoxMaterialSlots.Slot.Colour, out _ ),
				"the colour a custom shader names was not found" );

			Assert.AreEqual( "materials/default/default_normal.tga",
				SBoxMaterialSlots.Texture( custom, SBoxMaterialSlots.Slot.Normal, out _ ) );

			Assert.AreEqual( "materials/default/default_selfillum.tga",
				SBoxMaterialSlots.Texture( custom, SBoxMaterialSlots.Slot.Emission, out _ ),
				"a self illumination map is an emission map" );

			//And a slot that holds a constant under either name is still a constant rather than somebody else's path.
			Assert.AreEqual( "", SBoxMaterialSlots.Texture( custom, SBoxMaterialSlots.Slot.Roughness, out var white ) );
			Assert.IsNotNull( white );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	//Types renamed (not just moved) by this engine get renamed in ported code, or it won't build
	[TestMethod]
	public void ATypeThisEngineRenamedIsRenamedInCode()
	{
		var directory = TempDirectory( "renamed-types" );

		try
		{
			var path = Path.Combine( directory, "IconEditor.cs" );

			File.WriteAllText( path, """
				using Sandbox;
				using Editor;

				public class IconEditor : Widget
				{
					void Build()
					{
						var renderer = Layout.Add( new NativeRenderingWidget( this )
						{
							Camera = _camera,
							TranslucentBackground = true,
						}, 1 );
					}
				}
				""" );

			var report = new PortReport();

			Assert.IsTrue( PortNames.RewriteCodeFile( path, "Editor/IconEditor.cs", report ) );

			var text = File.ReadAllText( path );

			Assert.IsTrue( text.Contains( "new SceneRenderingWidget( this )" ),
				"the widget this engine has under its own name: " + text );

			Assert.IsFalse( text.Contains( "NativeRenderingWidget" ), text );

			//And what it was being given still applies - ours has both.
			Assert.IsTrue( text.Contains( "Camera = _camera" ) );
			Assert.IsTrue( text.Contains( "TranslucentBackground = true" ) );

			Assert.IsTrue( text.Contains( "using Rinten;" ), "the namespace is still rewritten too" );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}


	//Ported code and scenes drop the Sandbox namespace; scenes would otherwise silently lose components
	[TestMethod]
	public void RenamesTheSourceEngineOutOfWhatItCopies()
	{
		var source = TempDirectory( "named" );
		var destination = Path.Combine( TempDirectory( "renamed" ), "new-project" );

		try
		{
			void Write( string relative, string content )
			{
				var path = Path.Combine( source, relative );
				Directory.CreateDirectory( Path.GetDirectoryName( path ) );
				File.WriteAllText( path, content );
			}

			Write( @"Code\Player.cs", """
				using Sandbox;
				using Sandbox.Diagnostics;

				namespace MyGame.Sandboxed;

				public sealed class Player : Sandbox.Component
				{
					// Written against Sandbox, and MySandboxHelper is not ours.
					private MySandboxHelper helper;
				}
				""" );

			//A library's own project file comes with it, and its root namespace is what every type in it lands in.
			Write( @"Libraries\someone.tool\tool.csproj", """
				<Project Sdk="Microsoft.NET.Sdk">
				  <PropertyGroup>
				    <RootNamespace>Sandbox</RootNamespace>
				  </PropertyGroup>
				  <ItemGroup>
				    <Using Include="Sandbox.Internal.GlobalGameNamespace" Static="true" />
				  </ItemGroup>
				</Project>
				""" );

			Write( @"Assets\scenes\level.scene", """
				{
					"GameObjects": [
						{
							"Name": "Prop",
							"Position": "0,0,39.3701",
							"Components": [
								{ "__type": "Sandbox.ModelRenderer", "Tint": "1,1,1,1" },
								{ "__type": "Sandbox.BoxCollider", "Scale": "39.3701,39.3701,39.3701" }
							]
						}
					]
				}
				""" );

			var report = SBoxPorter.Port( source, destination );

			var code = File.ReadAllText( Path.Combine( destination, "Code", "Player.cs" ) );

			StringAssert.Contains( code, "using Rinten;", "the root namespace moves" );
			StringAssert.Contains( code, "using Rinten.Diagnostics;", "and so do the ones under it" );
			StringAssert.Contains( code, "Rinten.Component", "including qualified type names" );

			StringAssert.Contains( code, "namespace MyGame.Sandboxed;",
				"but not a name that merely starts the same way" );
			StringAssert.Contains( code, "MySandboxHelper",
				"nor somebody else's identifier that contains it" );
			StringAssert.Contains( code, "// Written against Sandbox,",
				"nor the word in a comment - the porter cannot tell prose from the engine" );

			var csproj = File.ReadAllText( Path.Combine( destination, "Libraries", "someone.tool", "tool.csproj" ) );

			StringAssert.Contains( csproj, "<RootNamespace>Rinten</RootNamespace>",
				"the root namespace is an element's whole text, which neither of the other two forms sees" );
			StringAssert.Contains( csproj, "Rinten.Internal.GlobalGameNamespace" );

			var scene = File.ReadAllText( Path.Combine( destination, "Assets", "scenes", "level.scene" ) );

			StringAssert.Contains( scene, "Rinten.ModelRenderer", "component types are renamed" );
			StringAssert.Contains( scene, "Rinten.BoxCollider" );
			Assert.IsFalse( scene.Contains( "Sandbox." ), "and none of the old ones are left" );

			Assert.AreEqual( 2, report.CodeFilesRenamed, "the source file and the project file" );
			Assert.AreEqual( 2, report.TypeNamesRenamed, "the two components in the scene" );

			//The rename has to land before the units pass, which is keyed by this engine's
			//type names. One metre where there were 39.3701 inches is what says it did.
			Assert.AreEqual( 1, report.ScenesConverted, "the scene should also have been rescaled" );

			var converted = System.Text.Json.Nodes.JsonNode.Parse( scene );
			var collider = converted["GameObjects"][0]["Components"][1];

			//39.3701 inches is a metre to within what a float's worth of the port's own inch can say, which is the point - not the digits.
			foreach ( var axis in collider["Scale"].GetValue<string>().Split( ',' ) )
			{
				Assert.AreEqual( 1.0, double.Parse( axis, System.Globalization.CultureInfo.InvariantCulture ), 0.0001,
					"a length known only by its component's type name - so the rename came first" );
			}
		}
		finally
		{
			Directory.Delete( source, true );
			Directory.Delete( Path.GetDirectoryName( destination ), true );
		}
	}

	//Their model document says what a model is collided with. Every ported model used to be handed the same made-up
	//answer - "hulls the artist made" - which is not a thing their editor has and not a thing this one could build.
	[TestMethod]
	public void ReadsTheShapesTheirDocumentNames()
	{
		var directory = TempDirectory( "hulls" );

		try
		{
			var model = Path.Combine( directory, "door.vmdl" );
			File.WriteAllText( model, DoorDocument );

			var hulls = SBoxHulls.Read( model, Rinten.ForeignBasis.Quake );

			Assert.AreEqual( 2, hulls.Count );

			var file = hulls[0] as ModelDefinition.RenderHull;
			Assert.IsNotNull( file, "a hull built from a file is a RenderHull naming it" );
			Assert.AreEqual( "door", file.Name );
			Assert.AreEqual( "handle", file.Bone );
			Assert.AreEqual( "wood", file.Surface );
			Assert.AreEqual( "models/door/door_phys.fbx", file.File );
			//Their two, in our units: the whole of the inch lives on the port's side, and what a
			//definition carries is a plain multiplier against the art as our own importer reads it.
			Assert.AreEqual( 2.0f * PortUnits.SBoxUnit, file.Scale, 0.0001f );

			var render = hulls[1] as ModelDefinition.RenderHull;
			Assert.IsNotNull( render, "a hull built from the render mesh is a RenderHull" );
			Assert.AreEqual( 16, render.MaxVertices );
			Assert.AreEqual( "", render.Surface, "their default surface is our nothing-said" );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	//A model whose artist never gave it a shape has no collision, and is not handed one.
	[TestMethod]
	public void AModelWithNoShapeListHasNoHulls()
	{
		var directory = TempDirectory( "nohulls" );

		try
		{
			var model = Path.Combine( directory, "leaf.vmdl" );
			File.WriteAllText( model, LeafDocument );

			Assert.AreEqual( 0, SBoxHulls.Read( model, Rinten.ForeignBasis.Quake ).Count );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public void ReadsPrimitiveShapesAndConicalJoints()
	{
		var directory = TempDirectory( "guyphys" );

		try
		{
			var model = Path.Combine( directory, "guy.vmdl" );
			File.WriteAllText( model, GuyPhysicsDocument );

			var hulls = SBoxHulls.Read( model, Rinten.ForeignBasis.Quake );
			Assert.AreEqual( 2, hulls.Count );

			var capsule = hulls[0] as ModelDefinition.CapsuleHull;
			Assert.IsNotNull( capsule );
			Assert.AreEqual( "torso", capsule.Bone );
			Assert.AreEqual( 20.0f * PortUnits.SBoxUnit, capsule.Radius, 0.0001f );
			Assert.AreEqual( -10.5716f * PortUnits.SBoxUnit, capsule.PointA.x, 0.0001f );

			var sphere = hulls[1] as ModelDefinition.SphereHull;
			Assert.IsNotNull( sphere );
			Assert.AreEqual( "head", sphere.Bone );
			Assert.AreEqual( 30.0f * PortUnits.SBoxUnit, sphere.Radius, 0.0001f );

			var joints = SBoxJoints.Read( model, Rinten.ForeignBasis.Quake );
			Assert.AreEqual( 1, joints.Count );
			Assert.AreEqual( ModelDefinition.JointKind.Ball, joints[0].Type );
			Assert.AreEqual( "torso", joints[0].BoneA );
			Assert.AreEqual( "head", joints[0].BoneB );
			//Their anchors are written in the parent body's own bone, so the compile puts them there as they are - see SBoxJoints.
			Assert.IsFalse( joints[0].ModelSpace );
			Assert.AreEqual( -52.62532f * PortUnits.SBoxUnit, joints[0].Position.x, 0.0001f );
			Assert.AreEqual( 47.169998f, joints[0].SwingLimit, 0.001f );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	//A RenderMeshFile can share its export with others - a whole body's worth of meshes in one file, with each
	//model naming which of them it actually draws. This is that filter, read both ways their editor can write it.
	[TestMethod]
	public void ReadsWhichMeshesARenderFileWants()
	{
		var directory = TempDirectory( "meshfilter" );

		try
		{
			//"Nothing, except these" - the shape basic_guy.vmdl actually uses.
			var allowList = Path.Combine( directory, "allow.vmdl" );
			File.WriteAllText( allowList, """
				{
					rootNode =
					{
						children =
						[
							{
								_class = "RenderMeshFile"
								filename = "models/guy/basic_guy.fbx"
								import_filter =
								{
									exclude_by_default = true
									exception_list =
									[
										"eyes",
										"torso",
										"mouth",
									]
								}
							},
						]
					}
				}
				""" );

			var allowed = SBoxMeshes.Read( allowList );
			Assert.AreEqual( 1, allowed.Count );
			Assert.IsTrue( allowed[0].FilterExcludesByDefault );
			CollectionAssert.AreEqual( new[] { "eyes", "torso", "mouth" }, allowed[0].FilterNames.ToList() );

			//"Everything, except these" - the other shape their editor allows.
			var denyList = Path.Combine( directory, "deny.vmdl" );
			File.WriteAllText( denyList, """
				{
					rootNode =
					{
						children =
						[
							{
								_class = "RenderMeshFile"
								filename = "models/guy/basic_guy.fbx"
								import_filter =
								{
									exclude_by_default = false
									exception_list =
									[
										"mantle",
									]
								}
							},
						]
					}
				}
				""" );

			var denied = SBoxMeshes.Read( denyList );
			Assert.AreEqual( 1, denied.Count );
			Assert.IsFalse( denied[0].FilterExcludesByDefault );
			CollectionAssert.AreEqual( new[] { "mantle" }, denied[0].FilterNames.ToList() );

			//A part naming no filter at all wants the whole file, and says so by naming nothing rather than by an empty list somebody has to notice.
			var whole = Path.Combine( directory, "whole.vmdl" );
			File.WriteAllText( whole, """
				{
					rootNode =
					{
						children =
						[
							{
								_class = "RenderMeshFile"
								filename = "models/prop.fbx"
							},
						]
					}
				}
				""" );

			Assert.IsNull( SBoxMeshes.Read( whole )[0].FilterNames );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	//A ragdoll: a capsule, a sphere, and a conical joint, as their editor writes them.
	private const string GuyPhysicsDocument = @"
{
	rootNode =
	{
		_class = ""RootNode""
		children =
		[
			{
				_class = ""PhysicsShapeList""
				children =
				[
					{
						_class = ""PhysicsShapeCapsule""
						parent_bone = ""torso""
						surface_prop = ""flesh""
						radius = 20.0
						point0 = [ 0.0, 10.5716, 0.0 ]
						point1 = [ 0.0, 23.718468, 0.0 ]
					},
					{
						_class = ""PhysicsShapeSphere""
						parent_bone = ""head""
						surface_prop = ""flesh""
						radius = 30.0
						center = [ 0.0, 23.0, 0.0 ]
					}
				]
			},
			{
				_class = ""PhysicsJointList""
				children =
				[
					{
						_class = ""PhysicsJointConical""
						parent_body = ""torso""
						child_body = ""head""
						anchor_origin = [ -0.000036, 52.62532, -0.000004 ]
						anchor_angles = [ -0.000001, 89.999992, -90.0 ]
						collision_enabled = false
						enable_swing_limit = true
						swing_limit = 47.169998
						enable_twist_limit = true
						min_twist_angle = -30.0
						max_twist_angle = 62.41
					}
				]
			}
		]
	}
}
";

	//A door: one hull from a file, one worked out from what it draws.
	private const string DoorDocument = @"
{
	rootNode =
	{
		_class = ""RootNode""
		children =
		[
			{
				_class = ""RenderMeshList""
				children =
				[
					{
						_class = ""RenderMeshFile""
						filename = ""models/door/door.fbx""
					}
				]
			}
			{
				_class = ""PhysicsShapeList""
				children =
				[
					{
						_class = ""PhysicsHullFile""
						name = ""door""
						parent_bone = ""handle""
						surface_prop = ""wood""
						filename = ""models/door/door_phys.fbx""
						import_scale = 2.0
						maxHullVertices = 0
					}
					{
						_class = ""PhysicsHullFromRender""
						parent_bone = """"
						surface_prop = ""default""
						maxHullVertices = 16
					}
				]
			}
		]
	}
}
";

	//A leaf: something to look at and nothing to bump into.
	private const string LeafDocument = @"
{
	rootNode =
	{
		_class = ""RootNode""
		children =
		[
			{
				_class = ""RenderMeshFile""
				filename = ""models/foliage/leaf.fbx""
			}
		]
	}
}
";




	//The furthest bone any vertex is weighted to - nought when the skin was lost.
	private static unsafe int HighestSkinBone( string compiled )
	{
		var handle = CoreAssets.ModelFile.Load( compiled );
		if ( handle is null ) return -1;

		var count = handle.SkinCount;
		if ( count <= 0 ) return -1;

		var skin = new byte[count * 8];
		fixed ( byte* s = skin ) handle.CopySkin( s, count );

		var highest = 0;

		for ( var vertex = 0; vertex < count; vertex++ )
			for ( var lane = 0; lane < 4; lane++ )
				if ( skin[( vertex * 8 ) + lane] > highest ) highest = skin[( vertex * 8 ) + lane];

		return highest;
	}

	[TestMethod]
	public void AuthoredMeshesFindsEveryFbxTheModelNames()
	{
		var directory = TempDirectory( "meshes" );

		try
		{
			var model = Path.Combine( directory, "weapon.vmdl" );
			File.WriteAllText( model, """
				<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} -->
				{
					rootNode =
					{
						children =
						[
							{
								_class = "RenderMeshFile"
								filename = "models/weapons/mp5/body.fbx"
							}
							{
								_class = "RenderMeshFile"
								filename = "models/weapons/mp5/el.fbx"
							}
							{
								_class = "RenderMeshFile"
								filename = "models/weapons/mp5/body.fbx"
							}
						]
					}
				}
				""" );

			var found = SBoxPorter.AuthoredMeshes( model );

			Assert.AreEqual( 2, found.Count, "the same file named twice is still one mesh" );
			Assert.AreEqual( "models/weapons/mp5/body.fbx", found[0] );
			Assert.AreEqual( "models/weapons/mp5/el.fbx", found[1] );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}
}
