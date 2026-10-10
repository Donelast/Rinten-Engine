using System;
using System.IO;

namespace EngineTests.Core;

//Shader-declared parameters get laid into the material constant buffer; checked via pixel colour
[TestClass]
[DoNotParallelize]
public class ShaderParamTests
{
	private const int Size = 32;

	//A shader with a slider of its own invention.
	private const string Source = """
		PARAMETERS
		{
			float g_flShade < Default( 0.25 ); Range( 0, 1 ); UiGroup( "Test" ); >;
			float3 g_vSomewhere < Default3( 0.0, 1.0, 0.0 ); UiGroup( "Test" ); >;
		}

		struct VIn
		{
			[[vk::location(0)]] float3 position : POSITION;
		};

		[shader("vertex")]
		float4 MainVs( VIn i ) : SV_Position
		{
			return float4( i.position.xy * 4.0, 0.5, 1.0 );
		}

		[shader("fragment")]
		float4 MainPs() : SV_Target
		{
			// The declared number, and a declared vector under it, so that a
			// slot written into the wrong place shows as the wrong colour
			// rather than as nothing at all.
			return float4( g_flShade, g_vSomewhere.y, 0.0, 1.0 );
		}
		""";

	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void ADeclarationIsReadTheWaySourceTwoWritesIt()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"rinten-params-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		try
		{
			var path = Path.Combine( directory, "declared.shader" );
			File.WriteAllText( path, Source );

			var declared = CoreShaderParams.Of( path );

			Assert.AreEqual( 2, declared.Length, "both numbers should have been read" );

			Assert.AreEqual( "g_flShade", declared[0].Name );
			Assert.AreEqual( "Shade", declared[0].Title, "the prefix is theirs, not the reader's" );
			Assert.AreEqual( "Test", declared[0].Group );
			Assert.AreEqual( 0.25f, declared[0].Default.x, 0.0001f );
			Assert.AreEqual( 1.0f, declared[0].Most, 0.0001f );
			Assert.IsTrue( declared[0].HasRange, "a range was written, so there is a slider" );

			Assert.AreEqual( "g_vSomewhere", declared[1].Name );
			Assert.AreEqual( "Somewhere", declared[1].Title );
			Assert.AreEqual( 3, declared[1].Width, "a float3 is three wide" );
			Assert.AreEqual( 1.0f, declared[1].Default.y, 0.0001f );

			//And the block written for them names both and defines both.
			var block = CoreShaderParams.Declare( declared );

			StringAssert.Contains( block, "ENGINE_MATERIAL_BLOCK" );
			StringAssert.Contains( block, "#define g_flShade" );
			StringAssert.Contains( block, "#define g_vSomewhere" );
		}
		finally
		{
			try { Directory.Delete( directory, true ); } catch { }
		}
	}

	//Attribute(...) params read from the draw's push block, not the shared material buffer
	[TestMethod]
	public void ANumberTheDrawSetsIsNotTheMaterials()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"rinten-attributes-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		try
		{
			var path = Path.Combine( directory, "wet.shader" );

			File.WriteAllText( path, """
				PARAMETERS
				{
					float g_flRoughness < Default( 0.5 ); Range( 0, 1 ); UiGroup( "Maps" ); >;
					float g_flGloss < Default( 0.25 ); Range( 0, 1 ); UiGroup( "Maps" ); >;
					float g_flWetness < Attribute( "Wetness" ); Default( 0 ); Range( 0, 1 ); UiGroup( "Wet" ); >;
					float3 g_vSplash < Attribute( "Splash" ); Default3( 1, 1, 1 ); UiGroup( "Wet" ); >;
				}
				""" );

			var declared = CoreShaderParams.Of( path );

			Assert.AreEqual( 4, declared.Length );
			Assert.IsFalse( declared[1].IsAttribute, "a plain number is the material's" );
			Assert.IsTrue( declared[2].IsAttribute );
			Assert.AreEqual( "Wetness", declared[2].Attribute, "and the draw calls it what the shader said" );

			//The material's block holds the material's numbers and nothing else: a slot for a number the draw sets would be a second place to write the same thing.
			var block = CoreShaderParams.Declare( declared );

			StringAssert.Contains( block, "g_flGloss_slot" );
			Assert.IsFalse( block.Contains( "g_flWetness_slot" ),
				"a number the draw sets takes no room in the material" );

			StringAssert.Contains( block, "#define g_flWetness (g_draw0.x)" );
			StringAssert.Contains( block, "#define g_vSplash (g_draw1.xyz)" );

			//And what a draw comes to, in the order the shader declared them rather than the order anybody set them.
			var attributes = new RenderAttributes();

			attributes.Set( "Wetness", 0.75f );
			attributes.Set( "Splash", new Vector3( 0.1f, 0.2f, 0.3f ) );

			Span<float> drawn = stackalloc float[CoreShaderParams.DrawSlots * 4];

			CoreShaderParams.Draw( drawn, declared,
				name => attributes.TryGetDeclared( name, out var value ) ? value : null );

			Assert.AreEqual( 0.75f, drawn[0], 0.0001f, "wetness is the first slot the shader declared" );
			Assert.AreEqual( 0.2f, drawn[5], 0.0001f, "and the splash is the second" );

			//What a draw never mentioned is what the shader said it should be.
			attributes.Clear();

			CoreShaderParams.Draw( drawn, declared,
				name => attributes.TryGetDeclared( name, out var value ) ? value : null );

			Assert.AreEqual( 0.0f, drawn[0], 0.0001f );
			Assert.AreEqual( 1.0f, drawn[4], 0.0001f, "the shader's own default, not nought" );
		}
		finally
		{
			try { Directory.Delete( directory, true ); } catch { }
		}
	}

	//Shader-declared textures use the bindless table via an index in material data, plus a fallback
	[TestMethod]
	public void AShaderMayAskForAPictureOfItsOwn()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"rinten-pictures-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		try
		{
			var path = Path.Combine( directory, "pictures.shader" );

			File.WriteAllText( path, """
				PARAMETERS
				{
					float g_flDepth < Default( 0.1 ); Range( 0, 1 ); UiGroup( "Test" ); >;
					Texture2D g_tHeight < Default( 0.5 ); UiGroup( "Test" ); >;
				}
				""" );

			var declared = CoreShaderParams.Of( path );

			Assert.AreEqual( 2, declared.Length );

			Assert.AreEqual( "g_tHeight", declared[1].Name );
			Assert.IsTrue( declared[1].IsTexture, "Texture2D is a picture, not a number" );
			Assert.AreEqual( 2, declared[1].Slots, "where it is, and what to read instead" );
			Assert.AreEqual( 1, declared[0].Slots, "a number is one slot as it always was" );
			Assert.AreEqual( 0.5f, declared[1].Default.x, 0.0001f );

			var block = CoreShaderParams.Declare( declared );

			StringAssert.Contains( block, "g_tHeight_where" );
			StringAssert.Contains( block, "g_tHeight_instead" );
			StringAssert.Contains( block, "EngineTexture(" );

			//And the numbers laid out for a material that named a picture for it: the number first, then the two slots of the picture behind it.
			Span<float> numbers = stackalloc float[CoreRenderDevice.MaterialParamFloats];

			numbers.Clear();

			CoreShaderParams.Into( numbers, declared, _ => null, name => name == "g_tHeight" ? 7u : 0u );

			Assert.AreEqual( 0.1f, numbers[4], 0.0001f, "the number keeps the first slot after the surface's" );
			Assert.AreEqual( 7.0f, numbers[8], 0.0001f, "and the picture says where it is" );
			Assert.AreEqual( 0.5f, numbers[12], 0.0001f, "and what to read where there is none" );
		}
		finally
		{
			try { Directory.Delete( directory, true ); } catch { }
		}
	}

	[TestMethod]
	public void ADeclaredNumberArrivesInTheShader()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		if ( !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "no shader compiler on this host" );
			return;
		}

		//Nothing included: the block the numbers are read from is written by the engine, so this shader needs no file beside it.
		var directory = Path.Combine( Path.GetTempPath(), $"rinten-params-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		var path = Path.Combine( directory, "declared.shader" );

		File.WriteAllText( path, Source );

		var view = 0ul;
		var pipeline = 0ul;
		var vertices = 0ul;
		var indices = 0ul;
		var white = 0ul;
		var material = 0ul;

		try
		{
			var stages = CoreShaderFile.Compile( path );

			if ( stages is null )
			{
				Assert.Inconclusive( $"the shader would not compile: {CoreRenderDevice.ShaderError}" );
				return;
			}

			if ( !CoreRenderDevice.Initialize( validation: true ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( Size, Size );
			Assert.AreNotEqual( 0ul, view );

			pipeline = CoreRenderDevice.CreatePipeline( stages[(int)CoreShaderFile.Stage.Vertex],
				stages[(int)CoreShaderFile.Stage.Pixel], CoreRender.VertexLayoutKind.Engine );

			Assert.AreNotEqual( 0ul, pipeline, $"the pipeline should bake: {CoreRenderDevice.Description}" );

			CoreRenderDemo.BuildCubeMesh( out var mesh, out var indexList );

			vertices = CoreRenderDevice.CreateBuffer(
				System.Runtime.InteropServices.MemoryMarshal.AsBytes( mesh.AsSpan() ) );
			indices = CoreRenderDevice.CreateBuffer(
				System.Runtime.InteropServices.MemoryMarshal.AsBytes( indexList.AsSpan() ) );

			var one = new byte[] { 255, 255, 255, 255 };

			white = CoreRenderDevice.CreateTextureRgba( one, 1, 1, srgb: false );
			Assert.AreNotEqual( 0ul, white );

			var errorsBefore = CoreErrors.Count;

			//The first slot is the four the engine has always written; a declared number gets one of its own after them.
			int Red( float shade, float somewhere )
			{
				var numbers = new float[CoreRenderDevice.MaterialParamFloats];

				numbers[4] = shade;
				numbers[8 + 1] = somewhere;

				CoreRenderDevice.DestroyTexture( material );
				material = CoreRenderDevice.CreateMaterial( white, 0, 0, 0, numbers );

				Assert.AreNotEqual( 0ul, material, $"the material should be made: {CoreRenderDevice.Description}" );

				var pixels = new byte[Size * Size * 4];

				Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ) );

				try
				{
					Assert.IsTrue( CoreRenderDevice.DrawIndexed( pipeline, vertices, indices,
						indexList.Length, default, 0, 0, material ),
						$"the draw was refused: {CoreRenderDevice.Description}" );
				}
				finally
				{
					CoreRenderDevice.EndViewFrame( view );
				}

				Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

				var at = ( ( ( Size / 2 ) * Size ) + ( Size / 2 ) ) * 4;

				return pixels[at];
			}

			var dark = Red( 0.1f, 1.0f );
			var bright = Red( 0.9f, 1.0f );

			//Read back through the view's own encoding, so a tenth of the way up is not a tenth of two hundred
			//and fifty-five - what matters is that the number arrived at all and arrived the right way round.
			Assert.IsTrue( dark < 120, $"a tenth should be dark, not {dark}" );
			Assert.IsTrue( bright > 200, $"nine tenths should be bright, not {bright}" );
			Assert.IsTrue( bright > dark * 2, $"{dark} and {bright} are the same number" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			CoreRenderDevice.DestroyTexture( material );
			CoreRenderDevice.DestroyTexture( white );
			CoreRenderDevice.DestroyBuffer( vertices );
			CoreRenderDevice.DestroyBuffer( indices );
			CoreRenderDevice.DestroyPipeline( pipeline );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();

			try { Directory.Delete( directory, true ); } catch { }
		}
	}
}
