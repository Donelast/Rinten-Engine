using System.Collections.Generic;
using System.IO;
using Rinten;

namespace EngineTests;

//Reading the textures a ported project brings compiled and nothing else - everything their
//material compiler generated, and everything whose source image was never checked in.
[TestClass]
public class ImportedTextureImportTest
{
	//Builds one of their containers around a texture: the header, a block list with
	//one DATA block in it, the texture's own fields, and the pixels after the block.
	private static byte[] Container( int width, int height, byte format, byte mips, byte[] payload )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );

		const int headerSize = 16;
		const int blockEntrySize = 12;
		const int dataFields = 40;

		//Header: size, header version, resource version, where the block list starts (from this field), how many blocks.
		writer.Write( 0 );              //filled in at the end
		writer.Write( (ushort)12 );
		writer.Write( (ushort)1 );
		writer.Write( 8 );              //the list follows the header
		writer.Write( 1 );

		//One block: name, where it is (from this field), how long it is.
		writer.Write( System.Text.Encoding.ASCII.GetBytes( "DATA" ) );
		writer.Write( 8 );              //straight after this entry
		writer.Write( dataFields );

		//The texture itself.
		writer.Write( (ushort)1 );      //version
		writer.Write( (ushort)0 );      //flags
		for ( var i = 0; i < 4; i++ ) writer.Write( 1.0f ); //reflectivity
		writer.Write( (ushort)width );
		writer.Write( (ushort)height );
		writer.Write( (ushort)1 );      //depth
		writer.Write( format );
		writer.Write( mips );
		writer.Write( 0 );              //picmip
		writer.Write( 8 );              //where the extra data starts
		writer.Write( 0 );              //and how much of it there is

		writer.Write( payload );

		writer.Flush();

		var bytes = stream.ToArray();

		//The size the header claims.
		System.BitConverter.GetBytes( bytes.Length ).CopyTo( bytes, 0 );

		System.Diagnostics.Debug.Assert( headerSize + blockEntrySize > 0 );

		return bytes;
	}

	private static string Write( byte[] bytes )
	{
		var path = Path.Combine( Path.GetTempPath(), $"rinten-vtex-{System.Guid.NewGuid():N}.vtex_c" );

		File.WriteAllBytes( path, bytes );

		return path;
	}

	//Four pixels stored as they are, so the container and the field offsets are what is being tested rather than any decoding.
	[TestMethod]
	public void RawPixelsThroughTheContainer()
	{
		var pixels = new byte[]
		{
			255, 0, 0, 255,
			0, 255, 0, 255,
			0, 0, 255, 255,
			255, 255, 255, 128,
		};

		var path = Write( Container( 2, 2, format: 4, mips: 1, pixels ) );

		try
		{
			using var bitmap = CoreImportedTexture.Load( path );

			Assert.IsNotNull( bitmap, "a two by two RGBA texture should read" );
			Assert.AreEqual( 2, bitmap.Width );
			Assert.AreEqual( 2, bitmap.Height );

			var read = bitmap.GetPixels32();

			Assert.AreEqual( 255, read[0].r );
			Assert.AreEqual( 255, read[1].g );
			Assert.AreEqual( 255, read[2].b );
			Assert.AreEqual( 128, read[3].a );
		}
		finally
		{
			File.Delete( path );
		}
	}

	//Their blue and green channels are the other way round, and a texture read without swapping them looks obviously wrong rather than subtly.
	[TestMethod]
	public void BlueAndRedAreSwappedBack()
	{
		//One pixel, written the way their format does: blue, green, red, alpha.
		var path = Write( Container( 1, 1, format: 28, mips: 1, new byte[] { 200, 100, 50, 255 } ) );

		try
		{
			using var bitmap = CoreImportedTexture.Load( path );
			var read = bitmap.GetPixels32();

			Assert.AreEqual( 50, read[0].r );
			Assert.AreEqual( 100, read[0].g );
			Assert.AreEqual( 200, read[0].b );
		}
		finally
		{
			File.Delete( path );
		}
	}

	//A DXT1 block holding one flat colour: both endpoints the same, so every pixel in it comes out as that colour whatever the indices say.
	[TestMethod]
	public void Dxt1FlatBlock()
	{
		//565 for pure red is 0xF800.
		var block = new byte[] { 0x00, 0xF8, 0x00, 0xF8, 0, 0, 0, 0 };

		var path = Write( Container( 4, 4, format: 1, mips: 1, block ) );

		try
		{
			using var bitmap = CoreImportedTexture.Load( path );

			Assert.IsNotNull( bitmap, "a single DXT1 block should read" );

			foreach ( var pixel in bitmap.GetPixels32() )
			{
				Assert.AreEqual( 255, pixel.r );
				Assert.AreEqual( 0, pixel.g );
				Assert.AreEqual( 0, pixel.b );
				Assert.AreEqual( 255, pixel.a );
			}
		}
		finally
		{
			File.Delete( path );
		}
	}

	//A DXT5 block's alpha half on its own: two endpoints, and indices picking the first for every pixel.
	[TestMethod]
	public void Dxt5AlphaIsRead()
	{
		var block = new byte[16];

		block[0] = 32;      //first alpha endpoint
		block[1] = 255;     //second
		//indices are all zero, so every pixel is the first endpoint

		//The colour half: white, both endpoints.
		block[8] = 0xFF; block[9] = 0xFF; block[10] = 0xFF; block[11] = 0xFF;

		var path = Write( Container( 4, 4, format: 2, mips: 1, block ) );

		try
		{
			using var bitmap = CoreImportedTexture.Load( path );

			Assert.IsNotNull( bitmap );

			foreach ( var pixel in bitmap.GetPixels32() )
			{
				Assert.AreEqual( 32, pixel.a, "the alpha endpoint the indices point at" );
			}
		}
		finally
		{
			File.Delete( path );
		}
	}

	//A BC7 block in mode six - one subset, which is what their compiler writes for anything
	//flat. Both endpoints the same colour, so the interpolation cannot hide a misread.
	[TestMethod]
	public void Bc7ModeSixFlatBlock()
	{
		var block = new byte[16];
		var at = 0;

		void Bits( int value, int count )
		{
			for ( var i = 0; i < count; i++ )
			{
				if ( ( ( value >> i ) & 1 ) != 0 )
					block[at >> 3] |= (byte)( 1 << ( at & 7 ) );

				at++;
			}
		}

		Bits( 0b1000000, 7 );   //mode six: six zeros then a one

		//Seven bits an endpoint, red at both ends and nothing else.
		Bits( 127, 7 ); Bits( 127, 7 );   //red
		Bits( 0, 7 ); Bits( 0, 7 );       //green
		Bits( 0, 7 ); Bits( 0, 7 );       //blue
		Bits( 127, 7 ); Bits( 127, 7 );   //alpha

		Bits( 1, 1 ); Bits( 1, 1 );       //the low bit of each endpoint

		Bits( 0, 3 );                      //the first index, three bits
		for ( var i = 1; i < 16; i++ ) Bits( 0, 4 );

		var path = Write( Container( 4, 4, format: 20, mips: 1, block ) );

		try
		{
			using var bitmap = CoreImportedTexture.Load( path );

			Assert.IsNotNull( bitmap, "mode six is the one their compiler writes" );

			foreach ( var pixel in bitmap.GetPixels32() )
			{
				Assert.AreEqual( 255, pixel.r );
				Assert.AreEqual( 255, pixel.a );

				//The channels written as zero come back as one: the low bit of every endpoint is the shared p-bit, and this block sets it.
				Assert.AreEqual( 1, pixel.g );
				Assert.AreEqual( 1, pixel.b );
			}
		}
		finally
		{
			File.Delete( path );
		}
	}

	//Anything that is not one of their containers is refused, rather than read as whatever the bytes happen to say.
	[TestMethod]
	public void NotOneOfTheirs()
	{
		Assert.IsNull( CoreImportedResource.Open( new byte[] { 1, 2, 3, 4 } ) );
		Assert.IsNull( CoreImportedResource.Open( new byte[64] ) );
	}

	//The block list is what a reader walks, so it has to come back whole.
	[TestMethod]
	public void BlocksAreListed()
	{
		var bytes = Container( 1, 1, format: 4, mips: 1, new byte[] { 1, 2, 3, 4 } );

		var resource = CoreImportedResource.Open( bytes );

		Assert.IsNotNull( resource );
		Assert.AreEqual( 1, resource.Blocks.Count );
		Assert.AreEqual( "DATA", resource.Blocks[0].Name );
		Assert.IsNotNull( resource.Find( "DATA" ) );
		Assert.IsNull( resource.Find( "VBIB" ) );
	}
}
