using System;
using System.IO;
using System.IO.Compression;

namespace Rinten.Android;

//Unpacks the game from apk assets into the app folder so engine paths work like on desktop
internal static class Payload
{
	//What the export wrote, as the packaging step put it in.
	private const string Archive = "payload.zip";

	//Written last, so a run that was killed halfway unpacks again.
	private const string Stamp = "payload.stamp";

	//Unpacks the build if this version of it is not there yet, and says where it is.
	public static string Open( global::Android.Content.Res.AssetManager assets, string into, string version, string data )
	{
		var root = Path.Combine( into, "game" );
		var stamp = Path.Combine( into, Stamp );

		KeepData( root, data );

		if ( File.Exists( stamp ) && File.ReadAllText( stamp ) == version ) return root;

		if ( Directory.Exists( root ) ) Directory.Delete( root, true );

		Directory.CreateDirectory( root );

		using ( var stream = assets.Open( Archive, global::Android.Content.Res.Access.Streaming ) )
		using ( var zip = new ZipArchive( Buffered( stream ), ZipArchiveMode.Read ) )
		{
			foreach ( var entry in zip.Entries )
			{
				//A name is a path from whoever made the zip, so it is checked rather than trusted: one with .. in it writes outside the app.
				var where = Path.GetFullPath( Path.Combine( root, entry.FullName ) );

				if ( !where.StartsWith( root, StringComparison.Ordinal ) ) continue;

				if ( entry.Length == 0 && entry.FullName.EndsWith( "/", StringComparison.Ordinal ) )
				{
					Directory.CreateDirectory( where );
					continue;
				}

				Directory.CreateDirectory( Path.GetDirectoryName( where ) );
				entry.ExtractToFile( where, true );
			}
		}

		File.WriteAllText( stamp, version );

		return root;
	}

	//Moves the saves a build from before kept inside itself, at game/data , out to where they are kept now - once, and only where nothing is there
	//yet. The build is deleted whole when a new version is installed, and every save was deleted with it: an update was a game started from nothing.
	private static void KeepData( string root, string data )
	{
		if ( string.IsNullOrEmpty( data ) ) return;

		var old = Path.Combine( root, "data" );

		try
		{
			if ( !Directory.Exists( old ) || Directory.Exists( data ) ) return;

			Directory.Move( old, data );
		}
		catch ( Exception e )
		{
			global::Android.Util.Log.Warn( "rinten", $"could not move the old saves out of the build: {e.Message}" );
		}
	}

	//The archive in memory, because a zip is read by seeking and an asset stream cannot seek.
	private static Stream Buffered( Stream stream )
	{
		var memory = new MemoryStream();

		stream.CopyTo( memory );
		memory.Position = 0;

		return memory;
	}

	//Whether this package carries a game at all.
	public static bool In( global::Android.Content.Res.AssetManager assets )
	{
		try
		{
			using var stream = assets.Open( Archive, global::Android.Content.Res.Access.Random );
			return true;
		}
		catch ( Java.IO.FileNotFoundException )
		{
			return false;
		}
	}
}
