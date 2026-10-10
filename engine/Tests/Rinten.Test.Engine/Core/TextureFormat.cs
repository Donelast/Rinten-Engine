using System.IO;

namespace EngineTests;

//Compiling an image into a texture, and reading it back.
[TestClass]
public class TextureFormatTest
{
	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-texture-{System.Guid.NewGuid():N}{extension}" );

	//4x4 half-black half-white TGA; split so an averaged mip can't match a copied corner
	private static string WriteImage( int size )
	{
		var header = new byte[18];
		header[2] = 2; //uncompressed true colour
		header[12] = (byte)(size & 0xFF);
		header[13] = (byte)((size >> 8) & 0xFF);
		header[14] = (byte)(size & 0xFF);
		header[15] = (byte)((size >> 8) & 0xFF);
		header[16] = 32; //bits per pixel
		header[17] = 8 | 0x20; //eight alpha bits, top-down

		var pixels = new byte[size * size * 4];

		for ( var y = 0; y < size; ++y )
		{
			for ( var x = 0; x < size; ++x )
			{
				var white = x >= size / 2;
				var at = ((y * size) + x) * 4;

				//TGA is BGRA.
				pixels[at + 0] = white ? (byte)255 : (byte)0;
				pixels[at + 1] = white ? (byte)255 : (byte)0;
				pixels[at + 2] = white ? (byte)255 : (byte)0;
				pixels[at + 3] = 255;
			}
		}

		var path = TempPath( ".tga" );

		using var file = File.Create( path );
		file.Write( header );
		file.Write( pixels );

		return path;
	}

	[TestMethod]
	public void CompilesAnImageIntoATexture()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var image = WriteImage( 4 );
		var compiled = TempPath( ".tex_c" );

		try
		{
			Assert.IsTrue( CoreTextureFile.Compile( image, compiled, srgb: true, generateMips: true, compress: false ),
				"the image should compile" );

			var texture = CoreTextureFile.Load( compiled );
			Assert.IsNotNull( texture, "and load back" );

			Assert.AreEqual( 4, texture.Width );
			Assert.AreEqual( 4, texture.Height );
			Assert.IsTrue( texture.IsSrgb, "it was compiled as colour" );

			//4x4, 2x2, 1x1 - all the way down, because the last levels cost almost nothing and leaving them out is what makes things flicker.
			Assert.AreEqual( 3, texture.MipCount );

			var info = texture.MipInfo( 0 );
			Assert.AreEqual( 4, info.Width );
			Assert.AreEqual( 4 * 4 * 4, info.ByteCount, "four channels a pixel" );

			info = texture.MipInfo( 2 );
			Assert.AreEqual( 1, info.Width, "the last level is one pixel" );
			Assert.AreEqual( 1, info.Height );

			//The top level is what went in: black on the left, white on the right.
			var top = texture.Mip( 0 );

			Assert.AreEqual( 4 * 4 * 4, top.Length );
			Assert.AreEqual( 0, top[0], "the first pixel is black" );
			Assert.AreEqual( 255, top[( 3 * 4 ) + 0], "the last of the row is white" );
			Assert.AreEqual( 255, top[3], "and opaque" );

			//Half black and half white, averaged, is grey - which proves the mip was filtered rather than sampled from one corner.
			var last = texture.Mip( 2 );

			Assert.AreEqual( 128, last[0], 2, "the one-pixel level is the average of the whole image" );
		}
		finally
		{
			File.Delete( image );
			if ( File.Exists( compiled ) ) File.Delete( compiled );
		}
	}

	//Without mips there is one level, and it is the image.
	[TestMethod]
	public void MipsCanBeTurnedOff()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var image = WriteImage( 8 );
		var compiled = TempPath( ".tex_c" );

		try
		{
			Assert.IsTrue( CoreTextureFile.Compile( image, compiled, srgb: false, generateMips: false, compress: false ) );

			var texture = CoreTextureFile.Load( compiled );

			Assert.IsNotNull( texture );
			Assert.AreEqual( 1, texture.MipCount );
			Assert.IsFalse( texture.IsSrgb, "compiled as data, not colour" );
		}
		finally
		{
			File.Delete( image );
			if ( File.Exists( compiled ) ) File.Delete( compiled );
		}
	}

	[TestMethod]
	public void RubbishIsRefused()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		Assert.IsFalse( CoreTextureFile.Compile( "no/such/image.png", TempPath( ".tex_c" ), true, true, false ),
			"a missing image compiles to nothing" );

		Assert.IsNull( CoreTextureFile.Load( "no/such/texture.tex_c" ) );

		var path = TempPath( ".tex_c" );

		try
		{
			File.WriteAllBytes( path, new byte[256] );
			Assert.IsNull( CoreTextureFile.Load( path ), "zeroes are not a texture" );
		}
		finally
		{
			File.Delete( path );
		}
	}
}
