namespace Editor.Preferences;

/// <summary>The editor's shortcuts, grouped and filtered the way the keybinds page lists them.</summary>
public sealed class ShortcutCatalog
{
	/// <summary>Groups matching the search, in the order shown. A group nothing matched is left out.</summary>
	public IReadOnlyList<ShortcutGroup> Groups( string query = null )
	{
		var search = query?.Trim();

		return EditorShortcuts.Entries
			.GroupBy( x => x.Identifier )
			.Select( x => new ShortcutBinding( x.First() ) )
			.Where( x => Matches( x, search ) )
			.GroupBy( x => x.Group )
			.OrderBy( x => x.Key )
			.Select( x => new ShortcutGroup( x.Key, x.OrderBy( b => b.Name ).ToList() ) )
			.Where( x => x.Bindings.Count > 0 )
			.ToList();
	}

	//What it does or what it is bound to - either is enough to find it by.
	static bool Matches( ShortcutBinding binding, string query )
	{
		if ( string.IsNullOrEmpty( query ) ) return true;

		return binding.Name.Contains( query, StringComparison.OrdinalIgnoreCase )
			|| binding.Keys.Contains( query, StringComparison.OrdinalIgnoreCase );
	}
}
