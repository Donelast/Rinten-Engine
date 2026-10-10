namespace Editor.Gallery;

/// <summary>The window's furniture: the docks around the page.</summary>
public sealed partial class PanelGalleryWindow
{
	PanelDockHost pageHost;
	PanelDockHost catalogHost;
	PanelDockHost inspectorHost;
	PanelDockHost selfTestHost;
	DockWidget centralDock;

	void BuildUI()
	{
		pageHost = new PanelDockHost();
		catalogHost = new PanelDockHost();
		inspectorHost = new PanelDockHost();
		selfTestHost = new PanelDockHost();

		BuildWorkspace();

		centralDock = DockManager.SetCentralWidget( pageHost );

		//Each with the edge it belongs on: a registered dock is made at once and put on its area closed.
		DockManager.RegisterDock( new() { Title = "Catalog", Icon = "list", Area = DockArea.Left, CreateAction = () => catalogHost } );
		DockManager.RegisterDock( new() { Title = "Inspector", Icon = "tune", Area = DockArea.Right, CreateAction = () => inspectorHost } );
		DockManager.RegisterDock( new() { Title = "Self-test", Icon = "checklist", Area = DockArea.Bottom, CreateAction = () => selfTestHost } );
	}

	//The panels in the docks, made together off the one session. Made again on hotload, which is why it
	//is one method: the hosts stay where the dock manager put them and only what is inside them is new.
	void BuildWorkspace()
	{
		pageHost.SetContent( new GalleryPagePanel( Session ) );
		catalogHost.SetContent( new GalleryNavPanel( Session ) );
		inspectorHost.SetContent( new GalleryInspectorPanel( Session ) );
		selfTestHost.SetContent( new GallerySelfTestPanel( Session ) );
	}

	[EditorEvent.Hotload]
	public void OnHotload()
	{
		Session.Reload();

		BuildWorkspace();
		ApplyLayout();
	}

	//Where the docks sit when nobody has moved them.
	protected override void BuildDefaultLayout()
	{
		DockManager.OpenDock( "Catalog", DockArea.Left );
		DockManager.OpenDock( "Inspector", DockArea.Right );
		DockManager.OpenDock( "Self-test", DockArea.Bottom );

		ApplyLayout();
	}

	//Narrow catalog, narrow inspector, a strip for the self-test, and the page gets the rest.
	internal void ApplyLayout()
	{
		if ( centralDock.IsValid() )
			centralDock.ToggleView( true );

		Proportions( "Catalog", 0.18f );
		Proportions( "Inspector", 0.78f );
		Proportions( "Self-test", 0.78f );

		DockManager.RaiseDock( "Catalog" );
		DockManager.RaiseDock( "Inspector" );
	}

	void Proportions( string title, float first )
	{
		if ( DockManager.FindDockWidget( title ) is { } dock )
			DockManager.SetSplitterProportions( dock, first, 1.0f - first );
	}
}
