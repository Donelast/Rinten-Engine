namespace Editor.TreeBrowser;

/// <summary>Lets more than one Asset Browser exist at a time. The dock manager keys panels by title, so each extra one registers under its own numbered title and gets its own entry in the View menu.</summary>
public partial class TreeAssetBrowser
{
	const string DockTitle = "Asset Browser";
	const string DockIcon = "folder_open";

	//Window a new browser opens in: the workspace in front, or the main window
	static DockWindow Host => DockWindow.InFront ?? EditorWindow;

	/// <summary>Opens another browser as a floating window.</summary>
	public static TreeAssetBrowser CreateFloating()
	{
		var (browser, dock) = CreateDock();
		Host.DockManager.AddDockFloating( dock );
		return browser;
	}

	/// <summary>Opens another browser docked against an existing widget.</summary>
	public static TreeAssetBrowser Create( Widget relativeTo, DockArea area )
	{
		var (browser, dock) = CreateDock( area );
		var manager = Host.DockManager;
		var relativeDock = manager.FindDockWidget( relativeTo );

		manager.AddDock( dock, area, relativeDock );
		return browser;
	}

	static (TreeAssetBrowser Browser, DockWidget Dock) CreateDock( DockArea area = DockArea.Bottom )
	{
		var manager = Host.DockManager;
		var name = $"{DockTitle} 2";

		bool IsTaken( string candidate )
		{
			if ( manager.FindDockWidget( candidate ) is not null )
				return true;

			return manager.DockTypes.Any( x => x.Title == candidate );
		}

		for ( var index = 3; IsTaken( name ); index++ )
			name = $"{DockTitle} {index}";

		var browser = new TreeAssetBrowser( EditorWindow );

		manager.RegisterDockType( new DockManager.DockInfo
		{
			Title = name,
			Icon = DockIcon,
			Area = area,
			CreateAction = () => new TreeAssetBrowser( EditorWindow )
		} );

		var dock = manager.CreateDockWidget( name, DockIcon, browser );
		dock.DeleteOnClose = true;

		return (browser, dock);
	}

	[Event( "tools.editorwindow.postcreateview" )]
	static void AddViewMenuButtons( Menu menu )
	{
		menu.AddSeparator();
		menu.AddOption( "New Asset Browser", "create_new_folder", () => CreateFloating() );
	}
}
