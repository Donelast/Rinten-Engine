using System;
using System.IO;

namespace EngineTests.Core;

//Batched texture uploads share one staging buffer; a colour per texture catches offset mixups
[TestClass]
[DoNotParallelize]
public class TextureUploadBatchTests
{
	//A shader that draws the texture it is given over the whole view.
	private const string Source = """
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

	//How many pictures a burst is. Far more than the ring has slots.
	private const int Many = 200;

	//A solid picture, in a colour nothing else in the burst is wearing. Eight by eight rather than one by one
	//so that a copy landing at the wrong offset lands somewhere visible rather than inside a row of padding.
	private static byte[] Solid( int which )
	{
		var pixels = new byte[8 * 8 * 4];

		//Spread over the range rather than the index itself: two textures a byte apart come back the same colour
		//through the sRGB curve, and a test that cannot tell them apart is a test that passes when they are swapped.
		var red = (byte)( 8 + ( which % 25 * 10 ) );
		var green = (byte)( 8 + ( which / 25 * 30 ) );

		for ( var i = 0; i < 8 * 8; i++ )
		{
			pixels[( i * 4 ) + 0] = red;
			pixels[( i * 4 ) + 1] = green;
			pixels[( i * 4 ) + 2] = 128;
			pixels[( i * 4 ) + 3] = 255;
		}

		return pixels;
	}

	[TestMethod]
	public unsafe void EveryPictureOfABurstKeepsItsOwnPixels()
	{
		if ( !CoreNative.Available || !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-batch-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		var made = new ulong[Many];

		var view = 0ul;
		var pipeline = 0ul;

		try
		{
			if ( !CoreRenderDevice.Initialize( validation: true ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			var path = Path.Combine( directory, "sample.shader" );
			File.WriteAllText( path, Source );

			var stages = CoreShaderFile.Compile( path );

			if ( stages is null )
			{
				Assert.Inconclusive( $"the shader would not compile: {CoreRenderDevice.ShaderError}" );
				return;
			}

			view = CoreRenderDevice.CreateView( 32, 32 );
			Assert.AreNotEqual( 0ul, view );

			pipeline = CoreRenderDevice.CreatePipeline(
				stages[(int)CoreShaderFile.Stage.Vertex],
				stages[(int)CoreShaderFile.Stage.Pixel] );

			Assert.AreNotEqual( 0ul, pipeline, $"the pipeline should bake: {CoreRenderDevice.Description}" );

			var errorsBefore = CoreErrors.Count;

			//The burst: everything made before anything is drawn, which is the shape of an interface rebuilding itself between two frames.
			var started = System.Diagnostics.Stopwatch.GetTimestamp();

			for ( var i = 0; i < Many; i++ )
			{
				made[i] = CoreRenderDevice.CreateTextureRgba( Solid( i ), 8, 8, srgb: true );

				Assert.AreNotEqual( 0ul, made[i], $"picture {i} of the burst should upload" );
			}

			var ms = ( System.Diagnostics.Stopwatch.GetTimestamp() - started ) * 1000.0 /
				System.Diagnostics.Stopwatch.Frequency;

			//Printed rather than asserted: what it costs is a number for a person to compare between one build and another - and between
			//RINTEN_UPLOAD_BATCH=0 and not, which is the same binary measured
			//both ways. A threshold here would only fail on somebody's laptop.
			Console.WriteLine( $"{Many} pictures uploaded in {ms:F1} ms " +
				$"({ms * 1000.0 / Many:F0} us each), batching " +
				$"{( Environment.GetEnvironmentVariable( "RINTEN_UPLOAD_BATCH" ) == "0" ? "off" : "on" )}" );

			//The first, one from the middle, and the last - the offsets that a batch gets wrong are the ones after the first.
			foreach ( var which in new[] { 0, Many / 2, Many - 1 } )
			{
				Sampled( view, pipeline, made[which], out var red, out var green, out var blue );

				var wanted = Solid( which );

				Assert.AreEqual( wanted[0], red, 8, $"red of picture {which}" );
				Assert.AreEqual( wanted[1], green, 8, $"green of picture {which}" );
				Assert.AreEqual( wanted[2], blue, 8, $"blue of picture {which}" );
			}

			//And again from nothing, in different colours. A picture whose texture is destroyed is kept and handed to the next one of its shape rather than freed,
			//so this round is the same images with somebody else's pixels written into them - which is the way that can go wrong now, and it goes wrong silently.
			for ( var i = 0; i < Many; i++ )
			{
				CoreRenderDevice.DestroyTexture( made[i] );
				made[i] = 0;
			}

			for ( var i = 0; i < Many; i++ )
			{
				made[i] = CoreRenderDevice.CreateTextureRgba( Solid( i + 3 ), 8, 8, srgb: true );

				Assert.AreNotEqual( 0ul, made[i], $"picture {i} of the second burst should upload" );
			}

			foreach ( var which in new[] { 0, Many / 2, Many - 1 } )
			{
				Sampled( view, pipeline, made[which], out var red, out var green, out var blue );

				var wanted = Solid( which + 3 );

				Assert.AreEqual( wanted[0], red, 8, $"red of reused picture {which}" );
				Assert.AreEqual( wanted[1], green, 8, $"green of reused picture {which}" );
				Assert.AreEqual( wanted[2], blue, 8, $"blue of reused picture {which}" );
			}

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			foreach ( var texture in made ) CoreRenderDevice.DestroyTexture( texture );

			CoreRenderDevice.DestroyPipeline( pipeline );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();

			Directory.Delete( directory, true );
		}
	}

	//A texture created mid-frame must be uploaded before that frame's draws sample it
	[TestMethod]
	public unsafe void APictureMadeInsideAFrameIsDrawnByIt()
	{
		if ( !CoreNative.Available || !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-batch-{Guid.NewGuid():N}" );
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

			var path = Path.Combine( directory, "sample.shader" );
			File.WriteAllText( path, Source );

			var stages = CoreShaderFile.Compile( path );

			if ( stages is null )
			{
				Assert.Inconclusive( $"the shader would not compile: {CoreRenderDevice.ShaderError}" );
				return;
			}

			view = CoreRenderDevice.CreateView( 32, 32 );
			pipeline = CoreRenderDevice.CreatePipeline(
				stages[(int)CoreShaderFile.Stage.Vertex],
				stages[(int)CoreShaderFile.Stage.Pixel] );

			Assert.AreNotEqual( 0ul, view );
			Assert.AreNotEqual( 0ul, pipeline );

			var errorsBefore = CoreErrors.Count;

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ),
				$"the view would not begin: {CoreRenderDevice.Description}" );

			texture = CoreRenderDevice.CreateTextureRgba( Solid( 7 ), 8, 8, srgb: true );

			Assert.AreNotEqual( 0ul, texture );
			Assert.IsTrue( CoreRenderDevice.Draw( pipeline, 3, default, texture ) );
			Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );

			var pixels = new byte[32 * 32 * 4];
			Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

			var middle = ( ( 16 * 32 ) + 16 ) * 4;
			var wanted = Solid( 7 );

			Assert.AreEqual( wanted[0], pixels[middle + 0], 8, "red" );
			Assert.AreEqual( wanted[1], pixels[middle + 1], 8, "green" );
			Assert.AreEqual( wanted[2], pixels[middle + 2], 8, "blue" );

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

	//Prints (not asserts) burst upload cost without validation; compare with RINTEN_UPLOAD_BATCH=0
	[TestMethod]
	public void WhatABurstOfPicturesCosts()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var made = new ulong[Many];

		try
		{
			if ( !CoreRenderDevice.Initialize( validation: false ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			//Once through first, so that what is measured is not the first call into a driver that has never made a texture.
			var warm = CoreRenderDevice.CreateTextureRgba( Solid( 0 ), 8, 8, srgb: true );

			CoreRenderDevice.DestroyTexture( warm );

			var started = System.Diagnostics.Stopwatch.GetTimestamp();

			for ( var i = 0; i < Many; i++ )
			{
				made[i] = CoreRenderDevice.CreateTextureRgba( Solid( i ), 8, 8, srgb: true );
			}

			var making = ( System.Diagnostics.Stopwatch.GetTimestamp() - started ) * 1000.0 /
				System.Diagnostics.Stopwatch.Frequency;

			//And the same pictures written over, which is what a texture that already exists costs when its widget repaints.
			started = System.Diagnostics.Stopwatch.GetTimestamp();

			for ( var i = 0; i < Many; i++ )
			{
				CoreRenderDevice.UpdateTextureRgba( made[i], Solid( i + 1 ), 8, 8 );
			}

			var writing = ( System.Diagnostics.Stopwatch.GetTimestamp() - started ) * 1000.0 /
				System.Diagnostics.Stopwatch.Frequency;

			//And the whole lot thrown away and made again, which is what an inspector does
			//when the selection changes: the same shapes, from nothing, twice a second.
			for ( var i = 0; i < Many; i++ )
			{
				CoreRenderDevice.DestroyTexture( made[i] );
				made[i] = 0;
			}

			started = System.Diagnostics.Stopwatch.GetTimestamp();

			for ( var i = 0; i < Many; i++ )
			{
				made[i] = CoreRenderDevice.CreateTextureRgba( Solid( i ), 8, 8, srgb: true );
			}

			var again = ( System.Diagnostics.Stopwatch.GetTimestamp() - started ) * 1000.0 /
				System.Diagnostics.Stopwatch.Frequency;

			Console.WriteLine( $"{Many} pictures: made in {making:F1} ms ({making * 1000.0 / Many:F0} us each), " +
				$"written over in {writing:F1} ms ({writing * 1000.0 / Many:F0} us each), " +
				$"thrown away and made again in {again:F1} ms ({again * 1000.0 / Many:F0} us each), batching " +
				$"{( Environment.GetEnvironmentVariable( "RINTEN_UPLOAD_BATCH" ) == "0" ? "off" : "on" )}" );

			Assert.AreNotEqual( 0ul, made[Many - 1] );
		}
		finally
		{
			foreach ( var texture in made ) CoreRenderDevice.DestroyTexture( texture );

			CoreRenderDevice.Shutdown();
		}
	}

	//Draws one picture over a view and hands back its middle pixel.
	private static void Sampled( ulong view, ulong pipeline, ulong texture,
		out byte red, out byte green, out byte blue )
	{
		red = green = blue = 0;

		Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ),
			$"the view would not begin: {CoreRenderDevice.Description}" );

		Assert.IsTrue( CoreRenderDevice.Draw( pipeline, 3, default, texture ) );
		Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );

		var pixels = new byte[32 * 32 * 4];

		Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

		var middle = ( ( 16 * 32 ) + 16 ) * 4;

		red = pixels[middle + 0];
		green = pixels[middle + 1];
		blue = pixels[middle + 2];
	}
}
