using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Editor.TreeBrowser;

//Files and folders the user pinned to the top of the asset tree. Paths are canonical in here. The same file reaches us written both ways -
//a folder listing hands back backslashes, an asset's own path forward ones - and two spellings of one file would pin twice and unpin never.
static class AssetPins
{
	const string CookieKey = "AssetLocations.Pins";

	static List<string> paths;

	/// <summary>Raised the moment the set changes, so every open tree can rebuild.</summary>
	public static event Action Changed;

	public static IReadOnlyList<string> All => Paths;

	static List<string> Paths => paths ??= Load();

	static List<string> Load()
	{
		var loaded = new List<string>();

		foreach ( var path in ProjectCookie.Get<List<string>>( CookieKey, [] ) )
		{
			var canonical = Canonical( path );
			if ( string.IsNullOrEmpty( canonical ) ) continue;
			if ( loaded.Any( x => x.Equals( canonical, StringComparison.OrdinalIgnoreCase ) ) ) continue;

			loaded.Add( canonical );
		}

		return loaded;
	}

	public static bool Contains( string path )
	{
		var canonical = Canonical( path );
		if ( string.IsNullOrEmpty( canonical ) ) return false;

		return Paths.Any( x => x.Equals( canonical, StringComparison.OrdinalIgnoreCase ) );
	}

	public static void Add( string path )
	{
		var canonical = Canonical( path );
		if ( string.IsNullOrEmpty( canonical ) || Contains( canonical ) )
			return;

		Paths.Add( canonical );
		Save();
	}

	public static void Remove( string path )
	{
		var canonical = Canonical( path );
		if ( string.IsNullOrEmpty( canonical ) )
			return;

		if ( Paths.RemoveAll( x => x.Equals( canonical, StringComparison.OrdinalIgnoreCase ) ) == 0 )
			return;

		Save();
	}

	//One spelling per file: full, with the platform's separators, no trailing one.
	static string Canonical( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		try
		{
			var full = Path.GetFullPath( path );
			return full.Length > 3 ? full.TrimEnd( '\\', '/' ) : full;
		}
		catch
		{
			return path.Trim();
		}
	}

	static void Save()
	{
		ProjectCookie.Set( CookieKey, Paths );
		Changed?.Invoke();
	}
}
