using Rinten.Engine;
using System;
using System.IO;
using System.Linq;

namespace Editor;

//Finds project assets by walking files in managed code, on open and whenever a file changes
internal static class CoreAssetScan
{
	private static readonly Logger log = new( "Assets" );

	//Assets are numbered as they are found, which is all the number is for.
	private static ulong nextId = 1;

	//What has already been found, by where it is on disk.
	private static readonly Dictionary<string, CoreAsset> known = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Walks the mounted content and registers everything the editor knows a type for.</summary>
	public static void ScanAll()
	{
		var content = FileSystem.Content;

		if ( content is null ) return;

		var found = 0;

		foreach ( var file in content.FindFile( "/", "*", true ) )
		{
			if ( Register( content, file ) ) found++;
		}

		log.Info( $"{found} assets" );

		//Said once, by name, rather than as a load, a reference scan and a compile each failing on every one of them - see Asset.IsCompiledLeftover.
		var leftovers = known.Values.Where( x => x.IsCompiledLeftover ).Select( x => x.FoundAs ).ToList();

		if ( leftovers.Count > 0 )
			log.Warning( $"{leftovers.Count} compiled files have no source beside them and are left alone - delete them or bring their sources back: {string.Join( ", ", leftovers )}" );

		Logging.Flush();

		AssetSystem.AssetScanComplete();
	}

	/// <summary>One file, if it is an asset at all. Answers whether it was added now.</summary>
	public static bool Register( BaseFileSystem content, string file )
	{
		if ( string.IsNullOrWhiteSpace( file ) ) return false;

		var extension = System.IO.Path.GetExtension( file );

		if ( string.IsNullOrEmpty( extension ) ) return false;

		//Only what the editor has a type for. Everything else in a project - the code, the readme, the .gitignore - is not an asset.
		if ( AssetType.FromExtension( extension ) is null ) return false;

		var absolute = content.GetFullPath( file );

		if ( string.IsNullOrWhiteSpace( absolute ) ) return false;

		return RegisterFile( absolute, file );
	}

	/// <summary>A file by its full path, wherever it was found. Answers whether it was added rather than already known.</summary>
	public static bool RegisterFile( string absolute, string relative )
	{
		if ( Path.IsPathRooted( absolute ) )
		{
			try { absolute = Path.GetFullPath( absolute ); }
			catch ( IOException ) { }
			catch ( ArgumentException ) { }
		}

		absolute = absolute.Replace( '\\', '/' );

		if ( known.ContainsKey( absolute ) ) return false;

		//A compiled _c file isn't its own asset when its source sits beside it, or the browser gets twins
		if ( absolute.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) &&
			File.Exists( absolute[..^2] ) )
			return false;

		var asset = new CoreAsset( nextId++, absolute, relative );

		known[absolute] = asset;

		AssetSystem.AddManaged( asset );

		return true;
	}

	/// <summary>The asset for a file on disk, if it is one of ours.</summary>
	public static CoreAsset Find( string absolute )
	{
		if ( absolute is null ) return null;

		return known.TryGetValue( absolute.Replace( '\\', '/' ), out var asset ) ? asset : null;
	}

	/// <summary>A file changed, appeared or went away: the editor watches the content folder and says so, and this keeps the list honest.</summary>
	public static void FileChanged( string absolute )
	{
		if ( string.IsNullOrWhiteSpace( absolute ) ) return;

		var path = absolute.Replace( '\\', '/' );

		//And the models built from it, which are out of date with it now.
		foreach ( var model in DependentsOf( path ) )
			AssetSystem.ChangedManaged( model );

		if ( Find( path ) is { } existing )
		{
			AssetSystem.ChangedManaged( existing );
			return;
		}

		//A folder that arrived whole - copied in, moved in, renamed. The watcher names the folder and nothing inside it (one inotify move is one event),
		//so what it holds is walked here: a model in a folder dragged into the project was not an asset until the next start's full scan found it.
		if ( Directory.Exists( path ) )
		{
			try
			{
				foreach ( var file in Directory.EnumerateFiles( path, "*", SearchOption.AllDirectories ) )
					FileChanged( file );
			}
			catch ( IOException ) { }
			catch ( UnauthorizedAccessException ) { }

			return;
		}

		//A folder that vanished: recheck everything under it so its assets get marked deleted
		if ( !File.Exists( path ) )
		{
			//The root itself is never the thing that went.
			if ( path.Trim( '/' ).Length == 0 ) return;

			var prefix = path.TrimEnd( '/' ) + "/";
			var relativePrefix = path.Trim( '/' ) + "/";

			foreach ( var (key, asset) in known )
			{
				if ( key.StartsWith( prefix, StringComparison.OrdinalIgnoreCase )
					|| ( asset.RelativePath?.StartsWith( relativePrefix, StringComparison.OrdinalIgnoreCase ) ?? false ) )
					AssetSystem.ChangedManaged( asset );
			}

			return;
		}

		var content = FileSystem.Content;

		if ( content is null ) return;

		//Named by where it is inside the content, which is how everything else refers to it - "models/thing.mdl" rather than a path off this disk.
		var relative = content.GetRelativePath( path ) ?? System.IO.Path.GetFileName( path );

		if ( AssetType.FromExtension( System.IO.Path.GetExtension( path ) ) is null ) return;

		RegisterFile( path, relative );
	}

	/// <summary>Models built from this file (see ModelInputs), so editing the art makes them stale too</summary>
	public static IEnumerable<CoreAsset> DependentsOf( string absolute )
	{
		if ( string.IsNullOrWhiteSpace( absolute ) ) yield break;

		var path = absolute.Replace( '\\', '/' );

		//A model is not built from a model, and its own compiled half changing is the build arriving rather than something to build for.
		var extension = System.IO.Path.GetExtension( path );
		if ( extension.Equals( ".mdl", StringComparison.OrdinalIgnoreCase )
			|| extension.Equals( ".mdl_c", StringComparison.OrdinalIgnoreCase ) )
			yield break;

		foreach ( var asset in known.Values.ToArray() )
		{
			if ( !asset.IsModel || asset.IsDeleted || asset.AbsolutePath is null ) continue;

			foreach ( var input in Rinten.ModelInputs.Paths( asset.AbsolutePath ) )
			{
				if ( !string.Equals( input, path, StringComparison.OrdinalIgnoreCase ) ) continue;

				yield return asset;
				break;
			}
		}
	}

	/// <summary>Everything forgotten, for a project being closed.</summary>
	public static void Clear()
	{
		known.Clear();
		nextId = 1;

		Rinten.ModelInputs.Clear();
	}
}
