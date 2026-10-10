using System.IO;

namespace EngineTests;

//Picture settings from a real .meta are baked in via the editor's PictureBake hook, left installed
[TestClass]
public class PictureSettingsTest
{
	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-picture-{System.Guid.NewGuid():N}{extension}" );

	//An eight by eight picture, all one grey, half see-through - as a TGA.
	private static string WriteImage( string meta )
	{
		const int size = 8;

		var header = new byte[18];
		header[2] = 2;
		header[12] = size;
		header[14] = size;
		header[16] = 32;
		header[17] = 8 | 0x20;

		var pixels = new byte[size * size * 4];

		for ( var i = 0; i < pixels.Length; i += 4 )
		{
			pixels[i + 0] = 100;
			pixels[i + 1] = 100;
			pixels[i + 2] = 100;
			pixels[i + 3] = 128;
		}

		var path = TempPath( ".tga" );

		using ( var file = File.Create( path ) )
		{
			file.Write( header );
			file.Write( pixels );
		}

		if ( meta is not null ) File.WriteAllText( path + ".meta", meta );

		return path;
	}

	private static CoreTextureFile Compile( string meta, bool compress )
	{
		Editor.PictureBake.Install();

		var image = WriteImage( meta );
		var compiled = TempPath( ".tex_c" );

		try
		{
			Assert.IsTrue( CoreTextureFile.Compile( image, compiled, srgb: true, generateMips: true, compress ) );

			return CoreTextureFile.Load( compiled );
		}
		finally
		{
			File.Delete( image );
			File.Delete( image + ".meta" );
			File.Delete( compiled );
		}
	}

	private static bool Ready()
	{
		if ( CoreNative.Available ) return true;

		Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
		return false;
	}

	[TestMethod]
	public void NoSettings_CompilesAsItAlwaysDid()
	{
		if ( !Ready() ) return;

		var texture = Compile( null, compress: true );

		Assert.AreEqual( 4, texture.MipCount, "8, 4, 2, 1" );
		Assert.AreNotEqual( CoreTextureFile.TextureFormat.Rgba8, texture.Format );
	}

	[TestMethod]
	public void NoMip_KeepsOnlyTheFullSize()
	{
		if ( !Ready() ) return;

		var texture = Compile( """{ "nomip": true }""", compress: false );

		Assert.AreEqual( 1, texture.MipCount );
	}

	[TestMethod]
	public void NoCompress_KeepsThePixels()
	{
		if ( !Ready() ) return;

		var texture = Compile( """{ "nocompress": true }""", compress: true );

		Assert.AreEqual( CoreTextureFile.TextureFormat.Rgba8, texture.Format );
	}

	[TestMethod]
	public void Brightness_ScalesTheColourAndNotTheAlpha()
	{
		if ( !Ready() ) return;

		var texture = Compile( """{ "brightness": 2.0 }""", compress: false );
		var top = texture.Mip( 0 );

		Assert.AreEqual( 200, top[0] );
		Assert.AreEqual( 200, top[1] );
		Assert.AreEqual( 200, top[2] );
		Assert.AreEqual( 128, top[3] );
	}
}
