using Editor;
using Rinten;
using Rinten.UI;

namespace EngineTests;

//A click's full path from pointer position through panel hit testing into the widget
[TestClass]
[DoNotParallelize]
public class EditorClickPathTests
{
	//A pointer somebody else is holding: the same input a window reads from the window system, without a window system.
	private sealed class FakePointer : PanelInput
	{
		public InputData Data;

		internal override InputData GetInputData() => Data;

		public override void SetCursor( string name ) { }

		//Where the pointer is, and whether the left button is down.
		public void Point( Vector2 at, bool down )
		{
			Data.MousePos = at;
			Data.Mouse0 = down;
		}
	}

	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		Editor.EditorBackend.Panels = true;

		Rinten.Engine.GlobalContext.Current.UISystem ??= new UISystem();
	}

	//One tick of a window: the panels' own frame, the layout, then the pointer - exactly the order CoreUiWindow.Frame runs them in.
	private static void Frame( RootPanel root, FakePointer pointer, int width = 400, int height = 300 )
	{
		root.TickInternal();

		root.PreLayout( new Rect( 0, 0, width, height ) );
		root.CalculateLayout();
		root.PostLayout();

		root.TickInputInternal();
		pointer.Tick( new[] { root }, mouseIsActive: true );
	}

	//Press and release over a widget clicks it, with a few frames either side
	[TestMethod]
	public void PressingOverAWidgetClicksIt()
	{
		var root = new UiWindowRoot();
		var pointer = new FakePointer();

		var clicks = 0;

		var button = new Editor.Button( "Compile" ) { Clicked = () => clicks++ };

		button.Position = new Vector2( 40, 20 );
		button.Size = new Vector2( 120, 30 );

		root.AddChild( button.Surface );

		//Settled: a window has been running frames long before anybody clicks.
		for ( var i = 0; i < 4; i++ ) Frame( root, pointer );

		var middle = new Vector2( 40 + 60, 20 + 15 );

		//The pointer arrives, presses, and lets go - a frame each, as a window would see it.
		pointer.Point( middle, down: false );
		Frame( root, pointer );

		Assert.AreEqual( button.Surface, pointer.Hovered,
			"the panel under the pointer should be the button's" );

		pointer.Point( middle, down: true );
		Frame( root, pointer );

		pointer.Point( middle, down: false );
		Frame( root, pointer );

		//And a couple more, because panel events are queued and run on the tick after the one that made them.
		Frame( root, pointer );
		Frame( root, pointer );

		Assert.AreEqual( 1, clicks, "the button should have been clicked once" );
	}

	//A click lands on the widget under the pointer, not on the one behind it.
	[TestMethod]
	public void TheWidgetUnderThePointerIsTheOneClicked()
	{
		var root = new UiWindowRoot();
		var pointer = new FakePointer();

		var behind = 0;
		var front = 0;

		//A box holding a button, which is every editor window in miniature.
		var panel = new Editor.Widget( null ) { Position = 0, Size = new Vector2( 400, 300 ) };
		panel.MouseClick = () => behind++;

		var button = new Editor.Button( "Compile" ) { Clicked = () => front++ };

		button.Parent = panel;
		button.Position = new Vector2( 40, 20 );
		button.Size = new Vector2( 120, 30 );

		root.AddChild( panel.Surface );

		for ( var i = 0; i < 4; i++ ) Frame( root, pointer );

		var onButton = new Vector2( 40 + 60, 20 + 15 );

		pointer.Point( onButton, down: false );
		Frame( root, pointer );

		pointer.Point( onButton, down: true );
		Frame( root, pointer );

		pointer.Point( onButton, down: false );
		Frame( root, pointer );

		Frame( root, pointer );
		Frame( root, pointer );

		Assert.AreEqual( 1, front, "the button under the pointer takes the click" );
	}
}
