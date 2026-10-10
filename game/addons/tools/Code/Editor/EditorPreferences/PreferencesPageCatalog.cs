namespace Editor.Preferences;

/// <summary>Every <see cref="PreferencesPage"/> the type library knows, grouped by its [Group].</summary>
public sealed class PreferencesPageCatalog : IPreferencesPageSource
{
	public static PreferencesPageCatalog Default { get; } = new();

	public IReadOnlyList<PreferencesPageSection> Sections()
	{
		var pages = EditorTypeLibrary.GetTypes<PreferencesPage>()
			.Where( x => !x.IsAbstract )
			.Select( x => new PreferencesPageDescriptor( x ) )
			.ToList();

		return pages
			.GroupBy( x => x.Section )
			.OrderBy( x => x.Min( p => p.Order ) )
			.ThenBy( x => x.Key )
			.Select( x => new PreferencesPageSection( x.Key, Sorted( x ) ) )
			.ToList();
	}

	static IReadOnlyList<PreferencesPageDescriptor> Sorted( IEnumerable<PreferencesPageDescriptor> pages )
		=> pages.OrderBy( x => x.Order ).ThenBy( x => x.Title ).ToList();
}
