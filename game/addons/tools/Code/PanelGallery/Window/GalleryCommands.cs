namespace Editor.Gallery;

/// <summary>The gallery from the console - which is the gallery over MCP.</summary>
public static class GalleryCommands
{
	const string AppTitle = "Panel Gallery";

	/// <summary>Opens the gallery, or brings it forward.</summary>
	[ConCmd( "panelgallery" )]
	public static void Open() => EditorAppAttribute.OpenApp( AppTitle );

	/// <summary>Opens the gallery on one example, by id (<c>page/title</c>) or by title.</summary>
	[ConCmd( "panelgallery_show" )]
	public static void Show( string idOrTitle )
	{
		Open();

		if ( PanelGalleryWindow.Current is not { } window || !window.IsValid() )
		{
			Log.Warning( "panelgallery_show: the gallery did not open" );
			return;
		}

		if ( !window.ShowExample( idOrTitle ) )
			Log.Warning( $"panelgallery_show: no example called '{idOrTitle}' - panelgallery_list prints them all" );
	}

	//For finding the one example on a page that takes the editor down: halve the page by id until it is alone. Set from the console, kept until the editor closes.
	/// <summary>Only cards whose id contains this are built on a page. Empty is every card.</summary>
	public static string CardFilter { get; private set; } = "";

	/// <summary>Builds only the cards whose id contains the text given, on this and every page after; no text is every card.</summary>
	[ConCmd( "panelgallery_filter" )]
	public static void Filter( string text )
	{
		CardFilter = text?.Trim() ?? "";

		Log.Info( CardFilter.Length == 0 ? "panelgallery_filter: every card" : $"panelgallery_filter: cards with '{CardFilter}' in their id" );

		if ( PanelGalleryWindow.Current is { } window && window.IsValid() )
			window.Session.RequestPageRebuild();
	}

	/// <summary>Scrolls the open page to a height from the top, and says where it is and how far it goes.</summary>
	[ConCmd( "panelgallery_scroll" )]
	public static void Scroll( float y )
	{
		if ( PanelGalleryWindow.Current is not { } window || !window.IsValid() || window.Page is not { } page )
		{
			Log.Warning( "panelgallery_scroll: the gallery is not open" );
			return;
		}

		page.ScrollTo( y );

		var (offset, size) = page.Scroll;
		Log.Info( $"panelgallery_scroll: at {offset:F0} of {size:F0}" );
	}

	/// <summary>Prints every example id, page by page.</summary>
	[ConCmd( "panelgallery_list" )]
	public static void List()
	{
		foreach ( var section in GalleryCatalog.CreateDefault().Sections() )
		{
			Log.Info( $"{section.Title} ({section.Entries.Count})" );

			foreach ( var entry in section.Entries )
				Log.Info( $"  {entry.Id}" );
		}
	}

	/// <summary>Builds every example once and prints what failed.</summary>
	[ConCmd( "panelgallery_selftest" )]
	public static void SelfTest()
	{
		var results = GallerySelfTest.Run( GalleryCatalog.CreateDefault().Entries() );

		Log.Info( GallerySelfTest.Report( results ) );
	}
}
