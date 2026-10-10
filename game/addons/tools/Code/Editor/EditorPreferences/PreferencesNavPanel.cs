namespace Editor.Preferences;

//It knows what it was handed and nothing about where it came from, so the pages can be the whole type library or a list a test wrote by hand.
/// <summary>The list of pages down the left of the preferences window.</summary>
[StyleSheet( "PreferencesNavPanel.cs.scss" )]
public sealed class PreferencesNavPanel : Panel
{
	/// <summary>Told whenever a different page is picked.</summary>
	public Action<PreferencesPageDescriptor> SelectionChanged { get; set; }

	/// <summary>The page the sidebar is pointing at.</summary>
	public PreferencesPageDescriptor Current { get; private set; }

	readonly Panel list;
	readonly List<PreferencesNavItemPanel> items = new();

	IReadOnlyList<PreferencesPageSection> sections = [];

	public PreferencesNavPanel()
	{
		AddClass( "preferences-nav" );

		list = AddChild<Panel>( "list" );
	}

	/// <summary>The pages to list, under their headings, in the order they are shown.</summary>
	public void SetSections( IReadOnlyList<PreferencesPageSection> value )
	{
		sections = value ?? [];

		Rebuild();
	}

	/// <summary>Points at the first page there is. False where there are none.</summary>
	public bool SelectFirst()
	{
		var first = items.FirstOrDefault();
		if ( first is null ) return false;

		Select( first.Page );
		return true;
	}

	/// <summary>Points at a page by its type - what the rest of the editor links to.</summary>
	public bool Select( Type pageType )
	{
		var found = items.FirstOrDefault( x => x.Page.Is( pageType ) );
		if ( found is null ) return false;

		Select( found.Page );
		return true;
	}

	public void Select( PreferencesPageDescriptor page )
	{
		if ( page is null || Current == page ) return;

		Current = page;
		SyncSelection();

		SelectionChanged?.Invoke( page );
	}

	void Rebuild()
	{
		list.DeleteChildren();
		items.Clear();

		foreach ( var section in sections )
		{
			var header = list.AddChild<Rinten.UI.Label>( "section" );
			header.AddClass( "micro" );
			header.Text = section.Title;

			foreach ( var page in section.Pages )
			{
				var item = new PreferencesNavItemPanel( page, Select );

				list.AddChild( item );
				items.Add( item );
			}
		}

		SyncSelection();
	}

	void SyncSelection()
	{
		foreach ( var item in items )
			item.SetSelected( item.Page == Current );
	}
}
