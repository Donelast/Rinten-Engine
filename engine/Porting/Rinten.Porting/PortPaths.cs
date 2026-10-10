using System.IO;

namespace Rinten.Porting;

/// <summary>Reading somebody's project the same way wherever the port is run.</summary>
public static class PortPaths
{
	/// <summary>Whether this is one of the old engine's compiled files rather than one of the files somebody wrote.</summary>
	public static bool IsCompiled( string path ) =>
		path is not null && path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase );

	/// <summary>Whether this is the binary half a resource keeps beside it rather than the resource itself.</summary>
	public static bool IsBlobSidecar( string path ) =>
		path is not null && path.EndsWith( "_d", StringComparison.OrdinalIgnoreCase );

	/// <summary>A path to write to, folded onto the directories that are already there.</summary>
	public static string OntoExisting( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return path;

		var name = Path.GetFileName( path );
		var directory = Path.GetDirectoryName( path );

		if ( string.IsNullOrEmpty( directory ) ) return path;

		var at = Path.GetPathRoot( directory ) ?? "";
		var rest = directory[at.Length..].Split( Path.DirectorySeparatorChar,
			StringSplitOptions.RemoveEmptyEntries );

		var walked = at.Length > 0 ? at : ".";

		foreach ( var step in rest )
		{
			var found = Existing( walked, step );

			walked = Path.Combine( walked, found );
		}

		return Path.Combine( walked, name );
	}

	//The directory in inside that is named, however it is spelled there, or the name as asked for where there is none.
	private static string Existing( string inside, string named )
	{
		if ( Directory.Exists( Path.Combine( inside, named ) ) ) return named;

		string[] there;

		try { there = Directory.GetDirectories( inside ); }
		catch ( Exception ) { return named; }

		foreach ( var directory in there )
		{
			var already = Path.GetFileName( directory );

			if ( string.Equals( already, named, StringComparison.OrdinalIgnoreCase ) )
				return already;
		}

		return named;
	}

	/// <summary>Every file under a root, in the same order on every platform.</summary>
	public static string[] Walk( string root )
	{
		var files = Directory.GetFiles( root, "*", SearchOption.AllDirectories );

		Array.Sort( files, StringComparer.Ordinal );

		return files;
	}

	/// <summary>Every directory directly under a root, in the same order on every platform.</summary>
	public static string[] Directories( string root )
	{
		var directories = Directory.GetDirectories( root );

		Array.Sort( directories, StringComparer.Ordinal );

		return directories;
	}

	/// <summary>A path out of somebody's content as a resource path: forward slashes, and no leading one.</summary>
	public static string Resource( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return path;

		return path.Replace( '\\', '/' ).TrimStart( '/' );
	}

	/// <summary>The file that path names under a root, on this filesystem.</summary>
	public static string Local( string root, string path )
		=> Path.Combine( root, Resource( path ).Replace( '/', Path.DirectorySeparatorChar ) );

	/// <summary>Whether a path is absolute on any platform (C:\... too), unlike Path.IsPathRooted</summary>
	public static bool IsRootedAnywhere( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return false;

		if ( Path.IsPathRooted( path ) ) return true;

		//A drive letter, or a share.
		if ( path.Length >= 3 && char.IsLetter( path[0] ) && path[1] == ':' && ( path[2] == '\\' || path[2] == '/' ) )
			return true;

		return path.StartsWith( "\\\\", StringComparison.Ordinal );
	}

	/// <summary>First file named name.*.extension (cached downloads); matched here for consistent case handling</summary>
	public static string FindCached( string directory, string name, string extension )
	{
		if ( !Directory.Exists( directory ) ) return null;

		var files = Directory.GetFiles( directory );

		Array.Sort( files, StringComparer.Ordinal );

		foreach ( var file in files )
		{
			var found = Path.GetFileName( file );

			if ( !found.StartsWith( name + ".", StringComparison.OrdinalIgnoreCase ) ) continue;
			if ( !found.EndsWith( extension, StringComparison.OrdinalIgnoreCase ) ) continue;

			return file;
		}

		return null;
	}

	/// <summary>File at a path matched case-insensitively per segment, or null; only after the exact path fails</summary>
	public static string FindFile( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;
		if ( File.Exists( path ) ) return path;

		var directory = FindFolderPath( Path.GetDirectoryName( path ) );
		if ( directory is null ) return null;

		var wanted = Path.GetFileName( path );

		foreach ( var found in Listed( directory ) )
		{
			if ( string.Equals( Path.GetFileName( found ), wanted, StringComparison.OrdinalIgnoreCase ) )
				return found;
		}

		return null;
	}

	//A directory path with each of its segments in the case it is on disk, or null where no run of cases names a directory that exists.
	private static string FindFolderPath( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return null;
		if ( Directory.Exists( path ) ) return path;

		var parent = FindFolderPath( Path.GetDirectoryName( path ) );
		if ( parent is null ) return null;

		var wanted = Path.GetFileName( path );

		foreach ( var found in Directory.GetDirectories( parent ) )
		{
			if ( string.Equals( Path.GetFileName( found ), wanted, StringComparison.OrdinalIgnoreCase ) )
				return found;
		}

		return null;
	}

	//What a directory holds, read once however many names miss in it.
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> listings = new();

	private static string[] Listed( string directory )
		=> listings.GetOrAdd( directory,
			at => Directory.Exists( at ) ? Directory.GetFiles( at ) : System.Array.Empty<string>() );

	/// <summary>Named directory under root in its on-disk case, or the would-be path if missing</summary>
	public static string FindDirectory( string root, string name )
	{
		var wanted = Path.Combine( root, name );

		if ( Directory.Exists( wanted ) || !Directory.Exists( root ) ) return wanted;

		foreach ( var found in Directory.GetDirectories( root ) )
		{
			if ( string.Equals( Path.GetFileName( found ), name, StringComparison.OrdinalIgnoreCase ) )
				return found;
		}

		return wanted;
	}
}
