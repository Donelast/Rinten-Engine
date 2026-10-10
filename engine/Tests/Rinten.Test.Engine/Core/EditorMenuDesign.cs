using Editor;
using Rinten;
using Rinten.UI;
using System.Linq;

namespace EngineTests;

//What a menu is made of: rows in the order they were added, a line where a separator was asked for, and a strip of names that only opens on a press.
[TestClass]
[DoNotParallelize]
public class EditorMenuDesignTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;
	}

	//A separator's row - 1px of line with 5px above and below it.
	private const float SeparatorHeight = 11.0f;

	private static float[] RowHeights( Menu menu )
	{
		return menu.Rows
			.OrderBy( x => x.Top )
			.Select( x => x.RowHeight )
			.ToArray();
	}

	[TestMethod]
	public void RowsComeOutInTheOrderTheyWereAddedIn()
	{
		var menu = new Menu();

		menu.AddOption( "New Scene", null, () => { } );
		menu.AddMenu( "Open Recent" );
		menu.AddSeparator();
		menu.AddOption( "Save", null, () => { } );

		menu.Build();

		//Submenus used to be built after every option, whatever order they were written in - which put Open Recent
		//at the bottom of the File menu and the separator in the middle of the options rather than after it.
		var heights = RowHeights( menu );

		Assert.AreEqual( 4, heights.Length );

		Assert.AreEqual( Theme.RowHeight, heights[0], "New Scene" );
		Assert.AreEqual( Theme.RowHeight, heights[1], "Open Recent, where it was written" );
		Assert.AreEqual( SeparatorHeight, heights[2], "the line, after the submenu and before Save" );
		Assert.AreEqual( Theme.RowHeight, heights[3], "Save" );

		menu.Destroy();
	}

	[TestMethod]
	public void ASeparatorIsALineRatherThanARowWithNothingOnIt()
	{
		var menu = new Menu();

		menu.AddOption( "Cut", null, () => { } );
		menu.AddSeparator();

		menu.Build();

		var heights = RowHeights( menu );

		Assert.AreEqual( 2, heights.Length, "a separator is a row" );
		Assert.AreEqual( SeparatorHeight, heights[1], "and it is a line's worth of it, not an option's" );

		menu.Destroy();
	}

	[TestMethod]
	public void TheMenuIsAsWideAsItsWidestRowAndNoNarrowerThanAHundredAndTwenty()
	{
		var narrow = new Menu();
		narrow.AddOption( "Cut", null, () => { } );
		narrow.Build();

		Assert.IsTrue( narrow.Size.x >= 120.0f, $"a short menu got {narrow.Size.x}" );

		var wide = new Menu();
		wide.AddOption( "Paste Special And Then Some More Words", null, () => { } );
		wide.Build();

		Assert.IsTrue( wide.Size.x > narrow.Size.x, "a long name makes the menu wider" );

		//Menu padding: 5px 0 - the room above the first row and below the last.
		Assert.AreEqual( Theme.RowHeight + 10.0f, narrow.Size.y, 0.5f );

		narrow.Destroy();
		wide.Destroy();
	}

	[TestMethod]
	public void SlidingAlongTheStripDoesNotOpenTheNextMenu()
	{
		var window = new Window();
		window.Size = new Vector2( 900, 600 );

		var bar = window.MenuBar;

		var file = bar.AddMenu( "File" );
		file.AddOption( "New Scene", null, () => { } );

		var edit = bar.AddMenu( "Edit" );
		edit.AddOption( "Undo", null, () => { } );

		window.ArrangeIfNeeded();
		bar.ArrangeIfNeeded();

		var rects = bar.TitleRects;

		Assert.AreEqual( 2, rects.Count );

		//Which name is down, rather than whether a panel appeared: a menu needs a window's overlay layer to be shown on, and there is no window here.
		Press( bar, rects[0].Center );

		Assert.AreEqual( 0, bar.OpenIndex, "a press puts the menu under it down" );

		Move( bar, rects[1].Center );

		Assert.AreEqual( 0, bar.OpenIndex, "hovering the next name does not open it" );

		Press( bar, rects[1].Center );

		Assert.AreEqual( 1, bar.OpenIndex, "a press on it does" );

		Menu.CloseAllPanels();
		window.Destroy();
	}

	[TestMethod]
	public void PressingOutsideAnOpenMenuOnAPanelClosesIt()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 800, 600 );

		var owner = new Widget( null );
		owner.Size = new Vector2( 400, 300 );
		root.AddChild( owner.Surface );

		EditorOverlay.For( root );

		var menu = new Menu( owner );
		menu.AddOption( "Test", null, () => { } );
		menu.OpenAt( new Vector2( 100, 100 ) );

		Assert.IsTrue( menu.IsOpen );

		Menu.PressedOnPanel( menu.Surface );

		Assert.IsTrue( menu.IsOpen, "a press on the menu itself leaves it open" );

		Menu.PressedOnPanel( owner.Surface );

		Assert.IsFalse( menu.IsOpen, "a press outside the menu closes it" );

		menu.Destroy();
		owner.Destroy();
		root.Delete();
	}

	private static void Press( Widget widget, Vector2 local )
	{
		widget.PanelMousePress( new MouseEvent( local, local, local,
			MouseButtons.Left, MouseButtons.Left, KeyboardModifiers.None ) );
	}

	private static void Move( Widget widget, Vector2 local )
	{
		widget.PanelMouseMove( new MouseEvent( local, local, local,
			MouseButtons.None, MouseButtons.None, KeyboardModifiers.None ) );
	}
}
