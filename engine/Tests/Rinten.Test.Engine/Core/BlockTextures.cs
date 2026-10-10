using System;
using System.IO;

namespace EngineTests.Core;

//Block-compressed textures upload as-is; checks pixels, since a wrong format mapping still succeeds
[TestClass]
[DoNotParallelize]
public class BlockTextureTests
{
	//A shader that draws the texture it is given over the whole view.
	private const string Source = """
		struct VIn
		{
			[[vk::location(0)]] float3 position : POSITION;
		};

		[[vk::binding(0,0)]] Sampler2D g_color;

		struct V2F
		{
			float4 position : SV_Position;
			float2 uv : TEXCOORD0;
		};

		[shader("vertex")]
		V2F MainVs( uint id : SV_VertexID )
		{
			V2F o;
			o.uv = float2( ( id << 1 ) & 2, id & 2 );
			o.position = float4( ( o.uv * 2.0 ) - 1.0, 0.5, 1.0 );
			return o;
		}

		[shader("fragment")]
		float4 MainPs( V2F i ) : SV_Target
		{
			return g_color.Sample( i.uv );
		}
		""";

	//One all-white BC1 block, so the result is white regardless of the second colour
	private static byte[] WhiteBc1Block() => new byte[]
	{
		0xFF, 0xFF, //colour 0: white
		0x00, 0x00, //colour 1: black
		0x00, 0x00, 0x00, 0x00, //every pixel takes colour 0
	};

	[TestMethod]
	public unsafe void BlocksReachTheDeviceWithoutBeingUnpacked()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		if ( !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "no shader compiler" );
			return;
		}

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-blocks-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		var view = 0ul;
		var pipeline = 0ul;
		var texture = 0ul;

		try
		{
			if ( !CoreRenderDevice.Initialize( validation: true ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			if ( CoreRender.SupportsBc() == 0 )
			{
				Assert.Inconclusive( "this device does not take block compressed textures" );
				return;
			}

			var compiled = Path.Combine( directory, "white.tex_c" );
			var block = WhiteBc1Block();

			//Four by four, one mip, BC1, sRGB - the shape an imported colour map arrives in.
			var written = CoreTextureFile.Write( compiled, 4, 4, CoreTextureFile.TextureFormat.Bc1, true,
				new[] { block.Length }, block );

			Assert.IsTrue( written, "the compiled texture should have been written" );
			Assert.IsTrue( File.Exists( compiled ) );

			var errorsBefore = CoreErrors.Count;

			texture = CoreRenderDevice.CreateTexture( compiled );

			Assert.AreNotEqual( 0ul, texture,
				$"blocks should upload as they are: {CoreRenderDevice.Description}" );

			//And what it looks like when something samples it.
			var path = Path.Combine( directory, "sample.shader" );
			File.WriteAllText( path, Source );

			var stages = CoreShaderFile.Compile( path );

			if ( stages is null )
			{
				Assert.Inconclusive( $"the shader would not compile: {CoreRenderDevice.ShaderError}" );
				return;
			}

			view = CoreRenderDevice.CreateView( 64, 64 );
			Assert.AreNotEqual( 0ul, view );

			pipeline = CoreRenderDevice.CreatePipeline(
				stages[(int)CoreShaderFile.Stage.Vertex],
				stages[(int)CoreShaderFile.Stage.Pixel] );

			Assert.AreNotEqual( 0ul, pipeline, $"the pipeline should bake: {CoreRenderDevice.Description}" );

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, new Color( 0.0f, 0.0f, 1.0f ) ),
				$"the view would not begin: {CoreRenderDevice.Description}" );

			Assert.IsTrue( CoreRenderDevice.Draw( pipeline, 3, default, texture ),
				"the full-screen triangle should record" );

			Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );

			var pixels = new byte[64 * 64 * 4];
			Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

			var middle = ( ( 32 * 64 ) + 32 ) * 4;

			//White, not the blue it was cleared to and not the black the second colour of the block would give.
			Assert.AreEqual( 255, pixels[middle + 0], 6, "red" );
			Assert.AreEqual( 255, pixels[middle + 1], 6, "green" );
			Assert.AreEqual( 255, pixels[middle + 2], 6, "blue" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			CoreRenderDevice.DestroyTexture( texture );
			CoreRenderDevice.DestroyPipeline( pipeline );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();

			Directory.Delete( directory, true );
		}
	}

	//A quality limit skips uploading top mips: white 8x8 over black 4x4, limit 4 samples black
	[TestMethod]
	public unsafe void ALimitDropsTheLevelsAboveIt()
	{
		if ( !CoreNative.Available || !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-limit-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		var view = 0ul;
		var pipeline = 0ul;

		try
		{
			if ( !CoreRenderDevice.Initialize( validation: true ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			if ( CoreRender.SupportsBc() == 0 )
			{
				Assert.Inconclusive( "this device does not take block compressed textures" );
				return;
			}

			//Eight by eight is four blocks, four by four is one.
			var white = WhiteBc1Block();
			var black = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

			var pixels = new byte[( 4 * white.Length ) + black.Length];

			for ( var block = 0; block < 4; block++ )
				Array.Copy( white, 0, pixels, block * white.Length, white.Length );

			Array.Copy( black, 0, pixels, 4 * white.Length, black.Length );

			var compiled = Path.Combine( directory, "levels.tex_c" );
			var sizes = new[] { 4 * white.Length, black.Length };

			Assert.IsTrue( CoreTextureFile.Write( compiled, 8, 8, CoreTextureFile.TextureFormat.Bc1, true, sizes, pixels ),
				"the two-level texture should have been written" );

			var path = Path.Combine( directory, "sample.shader" );
			File.WriteAllText( path, Source );

			var stages = CoreShaderFile.Compile( path );

			if ( stages is null )
			{
				Assert.Inconclusive( $"the shader would not compile: {CoreRenderDevice.ShaderError}" );
				return;
			}

			view = CoreRenderDevice.CreateView( 64, 64 );

			pipeline = CoreRenderDevice.CreatePipeline(
				stages[(int)CoreShaderFile.Stage.Vertex],
				stages[(int)CoreShaderFile.Stage.Pixel] );

			Assert.AreNotEqual( 0ul, pipeline );

			var errorsBefore = CoreErrors.Count;

			Assert.AreEqual( 255, Sampled( view, pipeline, compiled, limit: 0 ), 6,
				"with no limit the top level is what is sampled" );

			Assert.AreEqual( 0, Sampled( view, pipeline, compiled, limit: 4 ), 6,
				"with a limit of four the eight by eight level was never uploaded" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			CoreRender.TextureLimit( 0 );

			CoreRenderDevice.DestroyPipeline( pipeline );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();

			Directory.Delete( directory, true );
		}
	}

	//The red of the middle pixel, with this texture drawn over the whole view at this upload limit.
	private static int Sampled( ulong view, ulong pipeline, string compiled, int limit )
	{
		CoreRender.TextureLimit( limit );

		var texture = CoreRenderDevice.CreateTexture( compiled );
		Assert.AreNotEqual( 0ul, texture, $"the texture should upload: {CoreRenderDevice.Description}" );

		try
		{
			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, new Color( 0.0f, 0.0f, 1.0f ) ) );
			Assert.IsTrue( CoreRenderDevice.Draw( pipeline, 3, default, texture ) );
			Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );

			var pixels = new byte[64 * 64 * 4];
			Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

			return pixels[( ( 32 * 64 ) + 32 ) * 4];
		}
		finally
		{
			CoreRenderDevice.DestroyTexture( texture );
		}
	}

	//Changing the sampler rewrites every material set before destroying the old one
	[TestMethod]
	public unsafe void MaterialsSurviveTheSamplerChanging()
	{
		if ( !CoreNative.Available || !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-aniso-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		var view = 0ul;
		var pipeline = 0ul;
		var texture = 0ul;

		try
		{
			if ( !CoreRenderDevice.Initialize( validation: true ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			if ( CoreRender.SupportsBc() == 0 )
			{
				Assert.Inconclusive( "this device does not take block compressed textures" );
				return;
			}

			var compiled = Path.Combine( directory, "white.tex_c" );
			var block = WhiteBc1Block();
			var sizes = new[] { block.Length };

			Assert.IsTrue( CoreTextureFile.Write( compiled, 4, 4, CoreTextureFile.TextureFormat.Bc1, true, sizes, block ) );

			texture = CoreRenderDevice.CreateTexture( compiled );
			Assert.AreNotEqual( 0ul, texture );

			var path = Path.Combine( directory, "sample.shader" );
			File.WriteAllText( path, Source );

			var stages = CoreShaderFile.Compile( path );

			if ( stages is null )
			{
				Assert.Inconclusive( $"the shader would not compile: {CoreRenderDevice.ShaderError}" );
				return;
			}

			view = CoreRenderDevice.CreateView( 64, 64 );

			pipeline = CoreRenderDevice.CreatePipeline(
				stages[(int)CoreShaderFile.Stage.Vertex],
				stages[(int)CoreShaderFile.Stage.Pixel] );

			Assert.AreNotEqual( 0ul, pipeline );

			var errorsBefore = CoreErrors.Count;

			//Drawn at each setting, including back up again: the material was made before any of them and has to survive all of them.
			foreach ( var taps in new[] { 16.0f, 1.0f, 8.0f } )
			{
				Assert.AreNotEqual( 0, CoreRender.Anisotropy( taps ),
					$"the sampler should be remade at {taps}: {CoreRenderDevice.Description}" );

				Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, new Color( 0.0f, 0.0f, 1.0f ) ) );
				Assert.IsTrue( CoreRenderDevice.Draw( pipeline, 3, default, texture ) );
				Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );

				var pixels = new byte[64 * 64 * 4];
				Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

				var middle = ( ( 32 * 64 ) + 32 ) * 4;

				Assert.AreEqual( 255, pixels[middle], 6,
					$"the material should still draw at {taps} taps" );
			}

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			CoreRenderDevice.DestroyTexture( texture );
			CoreRenderDevice.DestroyPipeline( pipeline );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();

			Directory.Delete( directory, true );
		}
	}
}
