namespace Editor.Preferences;

/// <summary>Pages listed under one heading in the sidebar.</summary>
public sealed class PreferencesPageSection
{
	public PreferencesPageSection( string title, IReadOnlyList<PreferencesPageDescriptor> pages )
	{
		Title = title;
		Pages = pages;
	}

	public string Title { get; }
	public IReadOnlyList<PreferencesPageDescriptor> Pages { get; }
}
