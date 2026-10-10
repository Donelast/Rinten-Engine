using Rinten.UI;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

/// <summary>The pages down the left of the gallery, and a search over every example on them.</summary>
[StyleSheet( "GalleryNavPanel.cs.scss" )]
public sealed class GalleryNavPanel : Panel
{
	readonly GallerySession session;
	readonly TextEntry search;
	readonly Panel list;
	readonly List<GalleryNavItemPanel> items = new();

	public GalleryNavPanel( GallerySession session )
	{
		this.session = session;

		AddClass( "gallery-nav" );

		search = AddChild<TextEntry>( "search" );
		search.Placeholder = "Search examples";
		search.OnTextEdited = _ => Rebuild();

		list = AddChild<Panel>( "list" );

		session.CatalogChanged += Rebuild;
		session.SectionChanged += OnSectionChanged;
		session.SelectionChanged += OnSelectionChanged;

		Rebuild();
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		session.CatalogChanged -= Rebuild;
		session.SectionChanged -= OnSectionChanged;
		session.SelectionChanged -= OnSelectionChanged;
	}

	void OnSectionChanged( GallerySection _ ) => SyncSelection();
	void OnSelectionChanged( GalleryEntry _ ) => SyncSelection();

	//Pages while the search is empty; matching examples under their page headings while it is not.
	void Rebuild()
	{
		list.DeleteChildren( true );
		items.Clear();

		var query = search.Text?.Trim();
		var searching = !string.IsNullOrEmpty( query );

		foreach ( var section in session.Sections )
		{
			if ( !searching )
			{
				var item = new GalleryNavItemPanel( section.Icon, section.Title, section.Entries.Count.ToString(), () => session.ShowSection( section ) )
				{
					Section = section,
				};

				list.AddChild( item );
				items.Add( item );
				continue;
			}

			var matches = section.Entries.Where( x => Matches( x, query ) ).ToList();
			if ( matches.Count == 0 ) continue;

			var header = list.AddChild<Label>( "section micro" );
			header.Text = section.Title;

			foreach ( var entry in matches )
			{
				var item = new GalleryNavItemPanel( entry.Icon, entry.Title, null, () => session.Show( entry.Id ) )
				{
					Entry = entry,
				};

				list.AddChild( item );
				items.Add( item );
			}
		}

		SyncSelection();
	}

	static bool Matches( GalleryEntry entry, string query )
		=> entry.Title.Contains( query, StringComparison.OrdinalIgnoreCase )
		|| entry.Id.Contains( query, StringComparison.OrdinalIgnoreCase )
		|| entry.Description.Contains( query, StringComparison.OrdinalIgnoreCase );

	void SyncSelection()
	{
		foreach ( var item in items )
		{
			var selected = item.Entry is not null
				? item.Entry == session.SelectedEntry
				: item.Section == session.CurrentSection;

			item.SetSelected( selected );
		}
	}
}

//One line of the catalog: an icon, a name, and a count where it stands for a page.
sealed class GalleryNavItemPanel : Panel
{
	public GallerySection Section { get; init; }
	public GalleryEntry Entry { get; init; }

	public GalleryNavItemPanel( string icon, string title, string badge, Action pressed )
	{
		AddClass( "nav-item" );
		AddClass( "pressable" );

		var iconLabel = AddChild<Label>( "icon" );
		iconLabel.AddClass( "material-icon" );
		iconLabel.Text = icon;

		var titleLabel = AddChild<Label>( "title" );
		titleLabel.Text = title;

		if ( !string.IsNullOrEmpty( badge ) )
		{
			var count = AddChild<Label>( "count micro" );
			count.Text = badge;
		}

		AddEventListener( "onclick", _ => pressed?.Invoke() );
	}

	public void SetSelected( bool selected ) => SetClass( "is-selected", selected );
}
