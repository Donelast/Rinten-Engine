using Editor;
using Rinten;

namespace EngineTests;

//View menu panels open in the workspace layout in front, not always the main window's
[TestClass]
[DoNotParallelize]
public class EditorViewMenuTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;
	}

	private static DockManager Manager()
	{
		var docks = new DockManager( null );

		docks.Size = new Vector2( 800, 600 );
		docks.SetCentralWidget( new Widget( null ) );

		return docks;
	}

	private static void Register( DockManager docks, string title )
	{
		docks.RegisterDockType( new DockManager.DockInfo
		{
			Title = title,
			Icon = "science",
			Area = DockArea.Bottom,
			CreateAction = () => new Widget( null ) { Name = title },
		} );
	}

	[TestMethod]
	public void AWorkspaceLearnsTheWindowsPanels()
	{
		var window = Manager();
		var workspace = Manager();

		Register( window, "Profiler" );

		Assert.IsFalse( workspace.DockTypes.Any( x => x.Title == "Profiler" ) );

		workspace.AdoptDockTypes( window );

		Assert.IsTrue( workspace.DockTypes.Any( x => x.Title == "Profiler" ),
			"a panel of the window's can be opened in the layout on screen" );
	}

	[TestMethod]
	public void APanelOpensInTheLayoutThatWasAsked()
	{
		var window = Manager();
		var workspace = Manager();

		Register( window, "Profiler" );
		workspace.AdoptDockTypes( window );

		workspace.SetDockState( "Profiler", true );

		Assert.IsTrue( workspace.IsDockOpen( "Profiler" ), "it opened here" );
		Assert.IsFalse( window.IsDockOpen( "Profiler" ), "and not in the layout nobody is looking at" );
	}

	[TestMethod]
	public void AWorkspacesOwnPanelIsTheOneItKeeps()
	{
		var window = Manager();
		var workspace = Manager();

		//Both have an inspector, and they are different inspectors: the mapping workspace builds its own rather than stealing the window's.
		Register( window, "Inspector" );

		var mine = new Widget( null ) { Name = "MappingInspector" };
		workspace.AddDock( "Inspector", "manage_search", mine, DockArea.Left );

		workspace.AdoptDockTypes( window );

		Assert.AreSame( mine, workspace.FindDockWidget( "Inspector" )?.Widget );
		Assert.AreSame( mine, workspace.DockTypes.First( x => x.Title == "Inspector" ).CreateAction() );
	}

	[TestMethod]
	public void ALayoutThatIsNotAWindowSaysSo()
	{
		//What the View menu asks before offering to reset a layout: a manager somebody built for a workspace has no window and no default to go back to.
		Assert.IsNull( Manager().Window );
	}

	[TestMethod]
	public void AnOrphanedDockOpensWhereItBelongs()
	{
		var docks = Manager();
		docks.Size = new Vector2( 800, 600 );

		var middle = docks.SetCentralWidget( new Widget( null ) );

		docks.RegisterDock( new DockManager.DockInfo
		{
			Title = "Profiler",
			Icon = "speed",
			Area = DockArea.Bottom,
			CreateAction = () => new Widget( null ) { Name = "Profiler" },
		} );

		docks.RegisterDock( new DockManager.DockInfo
		{
			Title = "Asset Browser",
			Icon = "folder",
			Area = DockArea.Bottom,
			CreateAction = () => new Widget( null ) { Name = "Asset Browser" },
		} );

		docks.OpenDock( "Asset Browser", DockArea.Bottom, middle );

		var state = docks.SaveStateManaged();
		Assert.IsFalse( state.Contains( "Profiler" ), "a closed dock is not written into the layout" );

		Assert.IsTrue( docks.RestoreState( state ) );

		var profiler = docks.FindDockWidget( "Profiler" );
		Assert.IsNotNull( profiler );

		docks.SetDockState( "Profiler", true );
		docks.SettleFlexSubtree();

		Assert.IsTrue( profiler.Position.y > docks.Size.y * 0.5f, "opening it from the View menu lands in the bottom half" );
		Assert.IsTrue( profiler.Size.y > 40, "with room to show something" );
	}
}
