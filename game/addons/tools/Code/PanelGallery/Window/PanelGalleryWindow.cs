namespace Editor.Gallery;

/// <summary>Panel Gallery workspace: every panel control on its own page, for viewing and testing</summary>
[EditorApp( "Panel Gallery", "grid_view", "Every panel control and piece of editor chrome on a page of its own, for looking at and for testing" )]
public sealed partial class PanelGalleryWindow : DockWindow
{
	//Which saved dock layout this window's arrangement belongs to. Bumped whenever the default layout changes shape.
	internal const string LayoutCookie = "PanelGallery.1";

	/// <summary>The gallery that is open, for the console commands. Null while there is none.</summary>
	public static PanelGalleryWindow Current { get; private set; }

	public GallerySession Session { get; }

	public PanelGalleryWindow()
	{
		DeleteOnClose = true;
		Size = new Vector2( 1400, 900 );

		WindowTitle = "Panel Gallery";
		SetWindowIcon( "grid_view" );

		Session = new GallerySession( GalleryCatalog.CreateDefault );
		Session.Reload();

		BuildUI();

		//After the docks and not before: setting this is what restores the saved layout, and
		//the default it falls back to opens docks that have to have been registered already.
		StateCookie = LayoutCookie;

		Current = this;
	}

	//Whether this is a tab in the main window rather than a window of its own - the workspace
	//takes the docks out of the frame and leaves the window itself an empty husk that never shows.
	internal bool IsEmbedded => DockManager.IsValid() && DockManager.Parent != this;

	public override void Show()
	{
		if ( !IsEmbedded )
			base.Show();

		StateCookie = LayoutCookie;

		ApplyLayout();
	}

	public override void OnDestroyed()
	{
		if ( Current == this )
			Current = null;

		base.OnDestroyed();
	}

	/// <summary>Opens the page an example is on and picks it. False where nothing goes by that name.</summary>
	public bool ShowExample( string idOrTitle ) => Session.Show( idOrTitle );

	/// <summary>The page of cards in the middle, for driving it from the console. Null while there is none.</summary>
	public GalleryPagePanel Page => pageHost?.Content as GalleryPagePanel;
}
