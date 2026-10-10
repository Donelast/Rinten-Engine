using System;

namespace Editor;

/// <summary>A workspace laid out around the scene: General's scene area moves into its middle while it is the tab in front, and its own docks stand around it. Mapping is one; an editor that works on the open scene - the movie maker - is another. Opened through <c>EditorMainWindow.OpenSceneWorkspace</c>.</summary>
public abstract class SceneWorkspace : Widget
{
	readonly DockWidget _sceneDock;
	readonly Widget _host;

	//The panels this workspace made, which are this workspace's to destroy.
	readonly List<DockWidget> _panels = new();

	//The docks around the scene.
	protected DockManager Docks { get; }

	//Built by the editor's apps menu, which hands no parent: the tab parents it.
	protected SceneWorkspace( string name ) : this( WorkspaceArea.Current, name ) { }

	protected SceneWorkspace( Widget parent, string name ) : base( parent )
	{
		Name = name;
		Layout = Layout.Column();

		//No bar of its own over the scene: the one button it held, the viewport layout, is on the scene view's
		//toolbar beside the view settings - see ViewportToolsPanel.Layout - where it is in every workspace.
		Docks = Layout.Add( new DockManager( this ), 1 );

		//A stand-in so the docks can be built without stealing General's scene area. Taking the scene hands that over; a throw here then leaves it alone.
		_host = new Widget( this ) { Name = name + "SceneHost" };
		_sceneDock = Docks.SetCentralWidget( _host );
	}

	//One of the editor's panels, built again for this workspace.
	protected DockWidget OpenPanel( string title, string icon, string type, DockArea area, DockWidget relativeTo = null )
	{
		Widget widget;

		try
		{
			widget = EditorTypeLibrary.Create<Widget>( type, new object[] { this } );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"{Name}: {type} would not open" );
			return null;
		}

		return OpenPanel( title, icon, widget, area, relativeTo );
	}

	//A panel this workspace built itself, docked the same way.
	protected DockWidget OpenPanel( string title, string icon, Widget widget, DockArea area, DockWidget relativeTo = null )
	{
		if ( !widget.IsValid() )
			return null;

		var dock = Docks.AddDock( title, icon, widget, area, relativeTo );

		_panels.Add( dock );

		return dock;
	}

	//Whether the central dock currently owns this scene area.
	internal bool ContainsScene( Widget scene ) => scene.IsValid() && _sceneDock.Widget == scene;

	//Take the scene area over, for as long as this workspace is current.
	internal void TakeScene( Widget scene )
	{
		if ( !scene.IsValid() ) return;

		_sceneDock.Widget = scene;
	}

	//Give it back.
	internal void ReleaseScene()
	{
		_sceneDock.Widget = _host;
	}

	public override void OnDestroyed()
	{
		ReleaseScene();

		foreach ( var panel in _panels )
		{
			panel.Widget?.Destroy();
			panel.Destroy();
		}

		_panels.Clear();

		_sceneDock?.Destroy();
		Docks?.Destroy();

		base.OnDestroyed();
	}

	protected override void OnPaint()
	{
		//Nothing: the ground belongs to the window, and on a glass one that ground is the blurred desktop. See EditorWorkspaces.
	}
}
