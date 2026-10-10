using System;
using System.IO;

namespace Rinten.Porting.SBox;

//Splits a packed map into separate roughness, metalness and AO pictures (R=AO, G=rough, B=metal)
internal static class SBoxMaterialMaps
{
	//What one of a material's maps is called, given the material and what the map is.
	internal static string NameFor( string material, string what )
	{
		var directory = Path.GetDirectoryName( material )?.Replace( '\\', '/' ) ?? "";
		var name = Path.GetFileNameWithoutExtension( material );

		return string.IsNullOrEmpty( directory )
			? $"{name}_{what}.png"
			: $"{directory}/{name}_{what}.png";
	}

	//A packed picture split into a grey one per map, written beside the material under assets.
	//The resource paths they were written to, or null where the picture could not be read.
	internal static (string Rough, string Metal, string Occlusion)? Split( string packedFile, string assets, string material )
	{
		var map = Read( packedFile );
		if ( map is null ) return null;

		var rough = NameFor( material, "rough" );
		var metal = NameFor( material, "metal" );
		var occlusion = NameFor( material, "ao" );

		Grey( map, 1, Under( assets, rough ) );
		Grey( map, 2, Under( assets, metal ) );
		Grey( map, 0, Under( assets, occlusion ) );

		return (rough, metal, occlusion);
	}

	private static string Under( string assets, string resource )
		=> Path.Combine( assets, resource.Replace( '/', Path.DirectorySeparatorChar ) );

	//One channel of a picture, written out as a grey one.
	private static void Grey( Map map, int channel, string into )
	{
		var pixels = new byte[map.Width * map.Height * 4];

		for ( var i = 0; i < map.Pixels.Length; i++ )
		{
			var texel = map.Pixels[i];
			var value = channel switch { 0 => texel.r, 1 => texel.g, _ => texel.b };

			pixels[( i * 4 ) + 0] = value;
			pixels[( i * 4 ) + 1] = value;
			pixels[( i * 4 ) + 2] = value;
			pixels[( i * 4 ) + 3] = 255;
		}

		using var bitmap = new Bitmap( map.Width, map.Height );

		bitmap.SetPixelsRgba( pixels );

		Directory.CreateDirectory( Path.GetDirectoryName( into ) );
		File.WriteAllBytes( into, bitmap.ToPng() );
	}

	//A picture, as pixels, or null where there is no file.
	private static Map Read( string file )
	{
		if ( string.IsNullOrWhiteSpace( file ) || !File.Exists( file ) ) return null;

		try
		{
			using var bitmap = Bitmap.CreateFromBytes( File.ReadAllBytes( file ) );

			if ( bitmap is null || !bitmap.IsValid || bitmap.Width <= 0 || bitmap.Height <= 0 ) return null;

			return new Map( bitmap.GetPixels32(), bitmap.Width, bitmap.Height );
		}
		catch ( Exception )
		{
			//A picture that will not decode is one material's three maps. Its own numbers are a better answer than refusing the port.
			return null;
		}
	}

	private sealed record Map( Color32[] Pixels, int Width, int Height );
}
