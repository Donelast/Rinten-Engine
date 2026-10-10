using System;
using System.IO;

namespace EngineTests.Core;

//Our block encoder round-trips through the decoder close to the source image
[TestClass]
[DoNotParallelize]
public class BlockEncodingTests
{
	private const int Size = 64;

	//A picture worth compressing: smooth where the format is good at it and flat where it should be exact.
	private static byte[] Picture( bool opaque )
	{
		var pixels = new byte[Size * Size * 4];

		for ( var y = 0; y < Size; y++ )
		for ( var x = 0; x < Size; x++ )
		{
			var at = ( ( y * Size ) + x ) * 4;

			//The left half is a smooth ramp in two channels; the right is four large squares of one colour each.
			if ( x < Size / 2 )
			{
				pixels[at] = (byte)( x * 4 );
				pixels[at + 1] = (byte)( y * 4 );
				pixels[at + 2] = 32;
			}
			else
			{
				var square = ( ( x / 16 ) + ( y / 16 ) ) % 4;

				pixels[at] = (byte)( square * 60 );
				pixels[at + 1] = (byte)( 255 - ( square * 60 ) );
				pixels[at + 2] = 128;
			}

			pixels[at + 3] = opaque ? (byte)255 : (byte)( y * 4 );
		}

		return pixels;
	}

	//The same pixels as a file stb_image reads: an uncompressed TGA.
	private static string WriteTarga( byte[] rgba )
	{
		var path = Path.Combine( Path.GetTempPath(), $"rinten-block-{Guid.NewGuid():N}.tga" );

		using var file = File.Create( path );

		Span<byte> header = stackalloc byte[18];

		header[2] = 2;                                  //uncompressed true colour
		header[12] = (byte)( Size & 0xFF );
		header[13] = (byte)( Size >> 8 );
		header[14] = (byte)( Size & 0xFF );
		header[15] = (byte)( Size >> 8 );
		header[16] = 32;                                //bits a pixel
		header[17] = 0x28;                              //eight bits of alpha, top row first

		file.Write( header );

		//Theirs is blue, green, red, alpha.
		var bgra = new byte[rgba.Length];

		for ( var i = 0; i < rgba.Length; i += 4 )
		{
			bgra[i] = rgba[i + 2];
			bgra[i + 1] = rgba[i + 1];
			bgra[i + 2] = rgba[i];
			bgra[i + 3] = rgba[i + 3];
		}

		file.Write( bgra );

		return path;
	}

	//Our format numbers to the decoder's kinds.
	private static CoreBlockCompression.Kind DecoderFormat( int stored )
		=> stored == 1 ? CoreBlockCompression.Kind.Bc1 : CoreBlockCompression.Kind.Bc3;

	private static void RoundTrip( bool opaque, int expectedFormat, int allowedAverage, int allowedWorst )
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		var wanted = Picture( opaque );
		var source = WriteTarga( wanted );
		var compiled = source + ".tex_c";

		try
		{
			Assert.IsTrue( CoreTextureFile.Compile( source, compiled, false, false, true ),
				"the image should compile into blocks" );

			var texture = CoreTextureFile.Load( compiled );
			Assert.IsNotNull( texture, "the compiled texture should load" );

			Assert.AreEqual( expectedFormat, (int)texture.Format,
				"the encoder chose the wrong format for this picture" );

			var info = texture.MipInfo( 0 );

			Assert.AreEqual( Size, info.Width );
			Assert.AreEqual( Size, info.Height );

			//Four by four blocks, eight bytes for BC1 and sixteen for BC3.
			var blockBytes = expectedFormat == 1 ? 8 : 16;
			var expectedBytes = ( Size / 4 ) * ( Size / 4 ) * blockBytes;

			Assert.AreEqual( expectedBytes, info.ByteCount, "the level is not the size its format makes it" );

			var blocks = texture.Mip( 0 ).ToArray();

			Assert.AreEqual( expectedBytes, blocks.Length );

			var got = CoreBlockCompression.Decode( DecoderFormat( expectedFormat ), blocks, Size, Size );

			Assert.IsNotNull( got, "what the encoder wrote would not decode" );

			long total = 0;
			var worst = 0;

			for ( var i = 0; i < wanted.Length; i++ )
			{
				//Alpha is exact in BC1 - the format has none - so it is only worth comparing where there is one.
				if ( !opaque || ( i % 4 ) != 3 )
				{
					var difference = Math.Abs( wanted[i] - got[i] );

					total += difference;
					worst = Math.Max( worst, difference );
				}
			}

			var average = total / (double)wanted.Length;

			Console.WriteLine( $"BLOCKS format {expectedFormat}, average error {average:F2}, worst {worst}" );

			Assert.IsTrue( average <= allowedAverage,
				$"the average error is {average:F2}, which is more than a block format explains" );

			Assert.IsTrue( worst <= allowedWorst, $"one channel came back {worst} away from what went in" );

			//And the flat squares on the right, which a block format has no excuse for getting wrong: one colour across a block is one endpoint.
			for ( var y = 4; y < Size - 4; y += 16 )
			for ( var x = ( Size / 2 ) + 4; x < Size - 4; x += 16 )
			{
				var at = ( ( y * Size ) + x ) * 4;

				for ( var channel = 0; channel < 3; channel++ )
				{
					Assert.AreEqual( wanted[at + channel], got[at + channel], 6,
						$"the flat square at {x},{y} came back a different colour" );
				}
			}
		}
		finally
		{
			try { File.Delete( source ); } catch { }
			try { File.Delete( compiled ); } catch { }
		}
	}

	//A picture with no alpha goes to BC1, four bits a pixel.
	[TestMethod]
	public void AnOpaquePictureGoesToBc1AndComesBack() => RoundTrip( true, 1, 4, 40 );

	//One with alpha goes to BC3, and the alpha survives.
	[TestMethod]
	public void APictureWithAlphaGoesToBc3AndComesBack() => RoundTrip( false, 2, 4, 40 );

	//The setting turns it off, and what comes out then is pixels.
	[TestMethod]
	public void WithoutItTheyStayPixels()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		var source = WriteTarga( Picture( true ) );
		var compiled = source + ".plain.tex_c";

		try
		{
			Assert.IsTrue( CoreTextureFile.Compile( source, compiled, false, false, false ) );

			var texture = CoreTextureFile.Load( compiled );
			Assert.IsNotNull( texture );

			Assert.AreEqual( CoreTextureFile.TextureFormat.Rgba8, texture.Format,
				"asked for pixels and got blocks" );

			Assert.AreEqual( Size * Size * 4, texture.MipInfo( 0 ).ByteCount );
		}
		finally
		{
			try { File.Delete( source ); } catch { }
			try { File.Delete( compiled ); } catch { }
		}
	}

	//A picture too small for a block is left as pixels whatever is asked. Under four pixels a side the
	//whole level is one block either way, so compressing it saves nothing and costs an icon its edges.
	[TestMethod]
	public void SomethingSmallerThanABlockStaysPixels()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		var path = Path.Combine( Path.GetTempPath(), $"rinten-block-{Guid.NewGuid():N}.tga" );
		var compiled = path + ".tex_c";

		try
		{
			using ( var file = File.Create( path ) )
			{
				Span<byte> header = stackalloc byte[18];

				header[2] = 2;
				header[12] = 2;
				header[14] = 2;
				header[16] = 32;
				header[17] = 0x28;

				file.Write( header );
				file.Write( new byte[2 * 2 * 4] );
			}

			Assert.IsTrue( CoreTextureFile.Compile( path, compiled, false, false, true ) );

			var texture = CoreTextureFile.Load( compiled );
			Assert.IsNotNull( texture );

			Assert.AreEqual( CoreTextureFile.TextureFormat.Rgba8, texture.Format,
				"a two by two picture was packed into blocks" );
		}
		finally
		{
			try { File.Delete( path ); } catch { }
			try { File.Delete( compiled ); } catch { }
		}
	}
}
