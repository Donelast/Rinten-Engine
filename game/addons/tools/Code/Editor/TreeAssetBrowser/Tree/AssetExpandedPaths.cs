using System.IO;

namespace Editor.TreeBrowser;

//Expanded-folder state shared across every asset tree panel.
static class AssetExpandedPaths
{
	const string CookieKey = "TreeAssetBrowser.ExpandedPaths";

	static readonly HashSet<string> Paths = new( StringComparer.OrdinalIgnoreCase );
	static int sharedVersion;
	static int savedVersion;
	static bool loaded;

	public static int SharedVersion => sharedVersion;

	public static void EnsureLoaded()
	{
		if ( loaded ) return;

		loaded = true;

		foreach ( var path in ProjectCookie.Get<List<string>>( CookieKey, [] ) )
			Paths.Add( path );
	}

	public static IReadOnlyCollection<string> Snapshot() => Paths;

	public static void SaveIfDirty()
	{
		if ( savedVersion == sharedVersion ) return;

		savedVersion = sharedVersion;
		ProjectCookie.Set( CookieKey, Paths.ToList() );
	}

	public static bool SetExpanded( string key, bool expanded )
	{
		if ( string.IsNullOrEmpty( key ) ) return false;

		bool changed;

		if ( expanded )
		{
			changed = Paths.Add( key );
		}
		else
		{
			changed = Paths.Remove( key );

			var prefix1 = key + Path.DirectorySeparatorChar;
			var prefix2 = key + "/";
			changed |= Paths.RemoveWhere( p =>
				p.StartsWith( prefix1, StringComparison.OrdinalIgnoreCase ) ||
				p.StartsWith( prefix2, StringComparison.OrdinalIgnoreCase ) ) > 0;
		}

		if ( changed )
			sharedVersion++;

		return changed;
	}

	public static bool Contains( string key ) => Paths.Contains( key );

	public static void ReplaceAll( IEnumerable<string> paths )
	{
		Paths.Clear();

		foreach ( var path in paths )
			Paths.Add( path );
	}

	public static void AddDefault( string key )
	{
		if ( Paths.Add( key ) )
			sharedVersion++;
	}
}
