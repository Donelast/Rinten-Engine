using Editor;
using Rinten;
using Rinten.UI;
using System.Linq;

namespace EngineTests;

//The wheel over a list that scrolls itself.
[TestClass]
[DoNotParallelize]
public class EditorListScrollTests
{
	//A pointer somebody else is holding: the same input a window reads from the window system, without a window system.
	private sealed class FakePointer : PanelInput
	{
		public InputData Data;

		internal override InputData GetInputData() => Data;

		public override void SetCursor( string name ) { }
	}

	//A list that says which of its rows are laid out.
	private sealed class Rows : ListView
	{
		public Rows() : base( null ) { }

		//The topmost row with a widget of its own, or -1 for none.
		public int First => ItemLayouts.Count == 0 ? -1 : ItemLayouts.Min( x => x.Row );
	}

	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;

		Rinten.Engine.GlobalContext.Current.UISystem ??= new UISystem();
	}

	//How far the clock has been wound, in seconds.
	private static double clock;

	//One tick of a window: the clocks, the panels' own frame, the layout, the pointer, and the
	//editor's frame event - the order CoreUiWindow.FrameEach and ManagedTools.Frame run them in.
	private static void Frame( RootPanel root, FakePointer pointer, BaseItemWidget list, int width = 400, int height = 300 )
	{
		//Wound by hand: smooth scrolling settles over real time, and a test that runs a hundred frames in a millisecond would never see it move.
		clock += 0.05;
		RealTime.Update( clock );

		root.TickInternal();

		root.PreLayout( new Rect( 0, 0, width, height ) );
		root.CalculateLayout();
		root.PostLayout();

		root.TickInputInternal();
		pointer.Tick( new[] { root }, mouseIsActive: true );

		//The wheel is how far it turned since it was last read, not where it is.
		pointer.Data.MouseWheel = 0;

		//What the editor's own frame event does to a list: lays its rows out again if anything asked it to.
		//Called by hand because the event system hands events out by assembly, and a test registers none.
		list.UpdateIfDirty();
	}

	//Turning the wheel over a list moves the rows it is showing.
	[TestMethod]
	public void TheWheelMovesTheRowsAList()
	{
		var root = new UiWindowRoot();
		var pointer = new FakePointer();

		var list = new Rows();

		//Rows the width of the list, as a list of assets is.
		list.ItemSize = new Vector2( 0, 24 );
		list.ItemSpacing = 0;

		list.Position = new Vector2( 0, 0 );
		list.Size = new Vector2( 300, 200 );

		//Far more than fits, so there is somewhere to go.
		list.SetItems( Enumerable.Range( 0, 200 ).Select( x => (object)$"item {x}" ) );

		root.AddChild( list.Surface );

		for ( var i = 0; i < 4; i++ ) Frame( root, pointer, list );

		var wasFirst = list.First;
		var wasValue = list.VerticalScrollbar.Value;

		Assert.AreEqual( 0, wasFirst, "the list starts at the top" );
		Assert.IsTrue( list.VerticalScrollbar.Maximum > 0,
			$"a list of 200 rows in 200 pixels has {list.VerticalScrollbar.Maximum} to scroll" );

		//The pointer arrives, and the wheel turns three notches downwards. A panel says positive is down.
		pointer.Data.MousePos = new Vector2( 150, 100 );
		Frame( root, pointer, list );

		Assert.AreEqual( list.Surface, pointer.Hovered, "the pointer is over the list" );

		pointer.Data.MouseWheel = new Vector2( 0, 3 );

		//A few frames for the scrolling to settle.
		for ( var i = 0; i < 20; i++ ) Frame( root, pointer, list );

		System.Console.WriteLine(
			$"first row {wasFirst} -> {list.First}, scroll {wasValue} -> {list.VerticalScrollbar.Value}" );

		Assert.IsTrue( list.VerticalScrollbar.Value > wasValue,
			$"the wheel should have scrolled, and the value went {wasValue} -> {list.VerticalScrollbar.Value}" );

		Assert.IsTrue( list.First > wasFirst,
			$"the rows should have moved, and the top row went {wasFirst} -> {list.First}" );
	}

	//Two clicks close together activate the row they landed on - which is how an asset is opened, and how a folder in the asset browser's tree is.
	[TestMethod]
	public void ADoubleClickActivatesTheRowUnderThePointer()
	{
		var root = new UiWindowRoot();
		var pointer = new FakePointer();

		object activated = null;

		var list = new Rows();

		list.ItemSize = new Vector2( 0, 24 );
		list.ItemSpacing = 0;
		list.ItemActivated = x => activated = x;

		//Away from the corner on purpose: nothing here should depend on where the desktop's own cursor happens to be sitting.
		list.Position = new Vector2( 60, 40 );
		list.Size = new Vector2( 300, 200 );

		list.SetItems( Enumerable.Range( 0, 30 ).Select( x => (object)$"item {x}" ) );

		root.AddChild( list.Surface );

		for ( var i = 0; i < 4; i++ ) Frame( root, pointer, list );

		//The third row: eight pixels of margin above the first, and each row is twenty four tall.
		var at = list.Position + new Vector2( 150, 8 + 24 * 2 + 12 );

		pointer.Data.MousePos = at;
		Frame( root, pointer, list );

		Assert.AreEqual( list.Surface, pointer.Hovered, "the pointer is over the list" );

		//What a window makes of two presses close together in time and place - see
		//UiWindowInput.DoubleClicked, which is the only thing that counts them for an editor window.
		pointer.Hovered.CreateEvent( new MousePanelEvent( "ondoubleclick", pointer.Hovered, "mouseleft" ) );

		//A panel runs its events on the tick after the one that made them.
		Frame( root, pointer, list );
		Frame( root, pointer, list );

		Assert.AreEqual( "item 2", activated, "the row under the pointer is the one activated" );
	}
}
