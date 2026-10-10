using Editor;
using Rinten;
using System.Linq;

namespace EngineTests;

//Dock drag targets: squares over the hovered group, highlight shows the real room, arrows split it
[TestClass]
[DoNotParallelize]
public class EditorDockingTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;
	}

	private static void Arrange( Widget widget )
	{
		widget.ArrangeIfNeeded();

		foreach ( var child in widget.childWidgets ) Arrange( child );
	}

	private static DockManager Manager()
	{
		var docks = new DockManager( null );

		docks.Size = new Vector2( 800, 600 );

		docks.SetCentralWidget( new Widget( null ) );

		Arrange( docks );

		return docks;
	}

	//Where the pointer would put a dock down, as the compass says.
	private static DockArea Aim( DockManager docks, Vector2 at ) => docks.AimAt( at, out _ );

	//Where a dock would land: five squares over the group under the pointer.

	[TestMethod]
	public void TheCompassIsFiveSquaresOverTheGroup()
	{
		var docks = Manager();

		//Nothing but the middle, so the one group is the whole window.
		Assert.AreEqual( DockArea.Center, Aim( docks, new Vector2( 400, 300 ) ) );

		Assert.AreEqual( DockArea.Left, Aim( docks, docks.TargetOf( DockArea.Left ).Center ) );
		Assert.AreEqual( DockArea.Right, Aim( docks, docks.TargetOf( DockArea.Right ).Center ) );
		Assert.AreEqual( DockArea.Top, Aim( docks, docks.TargetOf( DockArea.Top ).Center ) );
		Assert.AreEqual( DockArea.Bottom, Aim( docks, docks.TargetOf( DockArea.Bottom ).Center ) );
	}

	[TestMethod]
	public void TheSquaresSitWhereTheirNamesSay()
	{
		var docks = Manager();

		Aim( docks, new Vector2( 400, 300 ) );

		var centre = docks.TargetOf( DockArea.Center );

		Assert.IsTrue( docks.TargetOf( DockArea.Left ).Right <= centre.Left, "left is to the left of the middle" );
		Assert.IsTrue( docks.TargetOf( DockArea.Right ).Left >= centre.Right, "right is to the right of it" );
		Assert.IsTrue( docks.TargetOf( DockArea.Top ).Bottom <= centre.Top, "top is above it" );
		Assert.IsTrue( docks.TargetOf( DockArea.Bottom ).Top >= centre.Bottom, "bottom is below it" );
	}

	[TestMethod]
	public void TheCompassFollowsThePointerOntoAGroup()
	{
		var docks = Manager();

		var console = docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );

		Arrange( docks );

		var group = docks.SideOf( console );

		//Over the console, the middle square is over the console - not in the middle of the window, which is where it used to be whatever it was over.
		Aim( docks, group.Rect.Center );

		Assert.AreEqual( group.Rect.Center, docks.TargetOf( DockArea.Center ).Center,
			"the compass sits on the group the pointer is over" );
	}

	[TestMethod]
	public void AwayFromTheSquaresIsThePartOfTheGroup()
	{
		var docks = Manager();

		//Pointing at a panel is already saying which panel: the squares say what that would do rather than being the only way to say it.
		Assert.AreEqual( DockArea.Left, Aim( docks, new Vector2( 40, 300 ) ) );
		Assert.AreEqual( DockArea.Right, Aim( docks, new Vector2( 760, 300 ) ) );
		Assert.AreEqual( DockArea.Top, Aim( docks, new Vector2( 400, 40 ) ) );
		Assert.AreEqual( DockArea.Bottom, Aim( docks, new Vector2( 400, 560 ) ) );
	}

	[TestMethod]
	public void TheMiddleOfAGroupIsATabInIt()
	{
		var docks = Manager();

		//Clear of every square, and still the middle of the group.
		Assert.AreEqual( DockArea.Center, Aim( docks, new Vector2( 520, 300 ) ) );
	}

	[TestMethod]
	public void TheStripOfNamesIsATabInIt()
	{
		var docks = Manager();

		var console = docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );

		Arrange( docks );

		var group = docks.SideOf( console ).Rect;

		//The top of a group is its tab strip, and letting go on it is asking for another tab - not for the group to be split along its top edge.
		Assert.AreEqual( DockArea.Center, Aim( docks, new Vector2( group.Left + 20.0f, group.Top + 4.0f ) ) );
	}

	[TestMethod]
	public void TheWindowKeepsFourSquaresOfItsOwn()
	{
		var docks = Manager();

		//Every point is inside some group now, so without these there would be no way left to say "down the side of everything".
		var area = docks.AimAt( new Vector2( 800 - 12 - 23, 300 ), out var outer );

		Assert.AreEqual( DockArea.Right, area );
		Assert.IsTrue( outer, "against the window rather than against the group" );
	}

	//What the highlight covers: the room the dock would take.

	[TestMethod]
	public void TheHighlightIsHalfOfTheGroup()
	{
		var docks = Manager();

		var console = docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );

		Arrange( docks );

		var group = docks.SideOf( console ).Rect;

		Aim( docks, group.Center );

		var zone = docks.ZoneRect( DockArea.Right, false );

		Assert.AreEqual( group.Width * 0.5f, zone.Width, 0.5f, "half of the group across" );
		Assert.AreEqual( group.Height, zone.Height, 0.5f, "and all of it down" );
		Assert.AreEqual( group.Left + ( group.Width * 0.5f ), zone.Left, 0.5f, "the right half of it" );
	}

	[TestMethod]
	public void TheHighlightForTheMiddleIsTheWholeGroup()
	{
		var docks = Manager();

		var console = docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );

		Arrange( docks );

		var group = docks.SideOf( console ).Rect;

		Aim( docks, group.Center );

		Assert.AreEqual( group, docks.ZoneRect( DockArea.Center, false ) );
	}

	//The order of the tabs in a group.

	//Two docks sharing the bottom edge, and the strip they are named on.
	private static (DockManager Docks, DockWidget First, DockWidget Second, Rect Strip) Tabs()
	{
		var docks = Manager();

		var console = docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );
		var output = docks.AddDock( "Output", null, new Widget( null ), DockArea.Bottom );

		Arrange( docks );

		var group = docks.SideOf( console ).Rect;

		return (docks, console, output, new Rect( group.Left, group.Top, group.Width, 24.0f ));
	}

	[TestMethod]
	public void TheStripSaysWhichGapATabWouldGoInto()
	{
		var (docks, _, _, strip) = Tabs();

		docks.AimAt( new Vector2( strip.Left + 2.0f, strip.Top + 4.0f ), out _ );

		Assert.AreEqual( 0, docks.LandingSlot, "in front of the first name" );

		docks.AimAt( new Vector2( strip.Right - 2.0f, strip.Top + 4.0f ), out _ );

		Assert.AreEqual( 2, docks.LandingSlot, "past both of them is the end of the strip" );
	}

	[TestMethod]
	public void ATabDroppedOnTheStripGoesWhereTheMarkWas()
	{
		var (docks, console, output, _) = Tabs();

		var side = docks.SideOf( console );

		Assert.AreEqual( console, side.Open.First(), "the first one opened is the first tab" );

		docks.PlaceManaged( output, side, 0 );

		Assert.AreEqual( output, docks.SideOf( output ).Open.First(),
			"dropped in front of the first name, it is the first name" );
	}

	[TestMethod]
	public void ATabCarriedAlongItsOwnStripChangesPlaces()
	{
		var (docks, console, output, _) = Tabs();

		var side = docks.SideOf( console );

		//Carried past the end of the strip it is already on: the order changes, and the group it left is the group it is in.
		docks.PlaceManaged( console, side, 2 );

		Arrange( docks );

		Assert.AreEqual( side, docks.SideOf( console ), "still on the edge it started on" );
		Assert.AreEqual( output, side.Open.First(), "the other one moved up" );
		Assert.AreEqual( console, side.Open.Last(), "and this one is at the end" );
	}

	[TestMethod]
	public void TheOnlyTabDroppedOnItsOwnStripStaysWhereItIs()
	{
		var docks = Manager();

		var console = docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );

		Arrange( docks );

		var side = docks.SideOf( console );

		docks.PlaceManaged( console, side, 1 );

		Arrange( docks );

		Assert.AreEqual( DockArea.Bottom, docks.SideOf( console ).Area, "the edge it was on is still there" );
		Assert.IsTrue( console.Visible, "and it is still shown" );
	}

	[TestMethod]
	public void ATabFromAnotherEdgeGoesIntoTheGapItWasDroppedOn()
	{
		var (docks, console, output, _) = Tabs();

		var side = docks.SideOf( console );

		var browser = docks.AddDock( "Browser", null, new Widget( null ), DockArea.Left );

		Arrange( docks );

		docks.PlaceManaged( browser, side, 1 );

		Arrange( docks );

		var open = side.Open.ToList();

		Assert.AreEqual( console, open[0] );
		Assert.AreEqual( browser, open[1], "between the two it was dropped between" );
		Assert.AreEqual( output, open[2] );
	}

	//Splitting a group in two, which is the only way "next to that one" can be said at all.

	[TestMethod]
	public void AnArrowSplitsTheGroupItIsOver()
	{
		var docks = Manager();

		var console = docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );
		var output = docks.AddDock( "Output", null, new Widget( null ), DockArea.Bottom );

		Arrange( docks );

		//Both are tabs on the bottom edge to start with, so only one shows.
		Assert.IsFalse( console.Visible && output.Visible, "tabs share the room" );

		var group = docks.SideOf( console );

		docks.SplitManaged( output, group, DockArea.Right );

		Arrange( docks );

		Assert.IsTrue( console.Visible, "the one that was there is still shown" );
		Assert.IsTrue( output.Visible, "and so is the one dropped beside it" );

		Assert.IsTrue( output.Position.x > console.Position.x, "the new one took the right half" );
		Assert.AreEqual( console.Size.y, output.Size.y, 1.0f, "and the same height" );
	}

	[TestMethod]
	public void ASplitStaysInsideTheGroupItSplit()
	{
		var docks = Manager();

		var console = docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );
		var output = docks.AddDock( "Output", null, new Widget( null ), DockArea.Bottom );

		Arrange( docks );

		var before = docks.SideOf( console ).Rect;

		docks.SplitManaged( output, docks.SideOf( console ), DockArea.Right );

		Arrange( docks );

		//The bottom edge is still the bottom edge: splitting one group does not move it, it divides it.
		Assert.AreEqual( before.Left, console.Position.x, 1.0f, "the first half starts where the group did" );
		Assert.AreEqual( before.Right, output.Position.x + output.Size.x, 1.0f, "and the second ends where it ended" );
		Assert.AreEqual( before.Width, console.Size.x + output.Size.x, 5.0f, "the two of them are the group, less the gap" );
	}

	[TestMethod]
	public void EmptyingHalfOfASplitPutsItBackTogether()
	{
		var docks = Manager();

		var console = docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );
		var output = docks.AddDock( "Output", null, new Widget( null ), DockArea.Bottom );

		docks.SplitManaged( output, docks.SideOf( console ), DockArea.Right );

		Assert.IsTrue( docks.SideOf( console ) != docks.SideOf( output ), "two groups after the split" );

		//Carried off somewhere else, the half it left has nothing in it.
		docks.PlaceManaged( output, DockArea.Right );

		Arrange( docks );

		Assert.AreEqual( DockArea.Right, docks.SideOf( output ).Area, "it went where it was put" );
		Assert.IsTrue( docks.SideOf( console ).Rect.Height > 0.0f, "and what it left is one group again" );
	}

	//A dock in a window of its own.

	[TestMethod]
	public void ADockCarriedOutOfTheWindowBecomesOne()
	{
		var docks = Manager();

		var side = docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );

		Arrange( docks );

		Assert.IsTrue( side.Position.y > 0.0f, "it starts docked at the bottom" );

		docks.FloatManaged( side );

		Assert.IsTrue( docks.IsFloatingManaged( side ), "and is out on its own" );

		Arrange( docks );

		//The edge it came from is empty now, so the middle has the window.
		Assert.IsFalse( docks.SideOf( side ).Open.Contains( side ),
			"the edge it came from no longer shows it" );
	}

	[TestMethod]
	public void ClosingTheWindowPutsTheDockBack()
	{
		var docks = Manager();

		var side = docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );

		docks.FloatManaged( side );
		docks.ReturnManaged( side );

		Assert.IsFalse( docks.IsFloatingManaged( side ) );

		Arrange( docks );

		//Back on the edge it was taken from, not dumped in the middle: which edge it came from is the one thing nobody wants to be asked for twice.
		Assert.AreEqual( DockArea.Bottom, docks.SideOf( side ).Area );
		Assert.IsTrue( docks.SideOf( side ).Open.Contains( side ) );
	}

	[TestMethod]
	public void AFloatingDockIsNotCountedOnItsEdge()
	{
		var docks = Manager();

		var first = docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );
		var second = docks.AddDock( "Output", null, new Widget( null ), DockArea.Bottom );

		docks.FloatManaged( second );

		Arrange( docks );

		var side = docks.SideOf( first );

		Assert.AreEqual( 1, side.Open.Count(), "the edge has the one that is still on it" );
		Assert.IsTrue( side.Docks.Contains( second ), "and still remembers the one that left" );
	}

	//Writing the layout down.

	[TestMethod]
	public void ASavedLayoutComesBackWithItsSplits()
	{
		var docks = Manager();

		var console = docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );
		var output = docks.AddDock( "Output", null, new Widget( null ), DockArea.Bottom );

		docks.SplitManaged( output, docks.SideOf( console ), DockArea.Right );

		Arrange( docks );

		var saved = docks.State;

		Assert.IsTrue( docks.RestoreState( saved ), "it reads back" );

		Arrange( docks );

		Assert.IsTrue( console.Visible && output.Visible, "both halves are still shown" );
		Assert.IsTrue( output.Position.x > console.Position.x, "on the sides they were on" );
	}

	[TestMethod]
	public void ALayoutFromBeforeTheTreeStillLoads()
	{
		var docks = Manager();

		docks.AddDock( "Console", null, new Widget( null ), DockArea.Bottom );

		//What the manager wrote before the layout was a tree.
		Assert.IsTrue( docks.RestoreState( "rinten1;Bottom:0.25:Console" ),
			"an old layout is not thrown away" );

		Arrange( docks );

		Assert.AreEqual( DockArea.Bottom, docks.SideOf( docks.FindDockWidget( "Console" ) ).Area );
	}
}
