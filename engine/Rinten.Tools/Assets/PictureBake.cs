using System;
using System.IO;

namespace Editor;

//Feeds a picture's .meta settings into its lazy block compile; a settings change discards old blocks
internal static class PictureBake
{
	//Tells the engine's texture compiler where a picture's settings are.
	internal static void Install() => Rinten.CoreTextureFile.SettingsFor = Read;

	//The settings beside a picture, or null where there are none that change anything.
	private static Rinten.CoreTextureFile.PictureSettings? Read( string source )
	{
		if ( string.IsNullOrWhiteSpace( source ) ) return null;

		var path = MetaPath( source );
		if ( !File.Exists( path ) ) return null;

		var meta = new MetaData( path );

		var settings = new Rinten.CoreTextureFile.PictureSettings(
			meta.Get( "nomip", false ),
			meta.Get( "nocompress", false ),
			meta.Get( "brightness", 1.0f ) );

		return settings.NoMips || settings.NoCompress || MathF.Abs( settings.Brightness - 1.0f ) > 0.001f
			? settings
			: null;
	}

	//Where a picture's .meta is - see Asset.MetaData.
	private static string MetaPath( string source ) => source.Replace( '\\', '/' ) + ".meta";

	//Every name the picture's blocks can be kept under: colour or data, and blocks or pixels - which of them exist depends on what drew it.
	private static string[] Blocks( string source )
	{
		var colour = Rinten.CompiledPaths.TextureBlocks( source, srgb: true );
		var data = Rinten.CompiledPaths.TextureBlocks( source, srgb: false );

		return [colour, Rinten.CompiledPaths.Compressed( colour ), data, Rinten.CompiledPaths.Compressed( data )];
	}

	//Whether any blocks were made before the settings last changed.
	internal static bool IsStale( string source )
	{
		try
		{
			var meta = MetaPath( source );
			if ( !File.Exists( meta ) ) return false;

			var changed = File.GetLastWriteTimeUtc( meta );

			foreach ( var blocks in Blocks( source ) )
			{
				if ( File.Exists( blocks ) && File.GetLastWriteTimeUtc( blocks ) < changed ) return true;
			}

			return false;
		}
		catch ( IOException )
		{
			return true;
		}
	}

	//Throws away the blocks made before the settings changed, and what is on the card from them; the next draw compiles them again, with the settings.
	internal static bool Build( Asset asset, string source )
	{
		var removed = false;

		foreach ( var blocks in Blocks( source ) )
		{
			try
			{
				if ( !File.Exists( blocks ) ) continue;

				File.Delete( blocks );
				removed = true;
			}
			catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
			{
				Log.Warning( e, $"{asset.Path}: could not remove {blocks} ({e.Message})" );
				return false;
			}
		}

		if ( removed ) Rinten.ResourceReload.Changed( asset.Path );

		return true;
	}
}
