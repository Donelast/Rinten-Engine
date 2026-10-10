using Editor;
using Rinten;
using Rinten.UI;

namespace EngineTests;

//Which window's layer a popup opens on, so it isn't measured for one window and placed on another
[TestClass]
[DoNotParallelize]
public class EditorPopupHostTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;
	}

	//A window: a root with a widget filling it.
	private static Widget Window( RootPanel root, float width = 800, float height = 600 )
	{
		var window = new Widget( null );

		window.Size = new Vector2( width, height );

		root.PanelBounds = new Rect( 0, 0, width, height );
		root.AddChild( window.Surface );

		return window;
	}

	[TestMethod]
	public void APopupOpensOnTheWindowOfWhateverOpenedIt()
	{
		var root = new RootPanel();
		var owner = Window( root );

		var popup = new PopupWidget( owner );
		popup.OpenAt( new Vector2( 40, 40 ), animate: false );

		var layer = EditorOverlay.Find( root );

		//The layer is made where the window has not got one yet. It used to be only looked for, and a window that had never opened
		//a menu had none - so the popup fell through to guessing at the window list instead of using the owner it was given.
		Assert.IsNotNull( layer, "the owner's window should have a layer by now" );
		Assert.AreSame( layer, popup.Surface.Parent, "and the popup should be on it" );

		popup.Destroy();
	}

	[TestMethod]
	public void APopupIgnoresANewerWindowAndStaysWithItsOwner()
	{
		var first = new RootPanel();
		var owner = Window( first );

		//A second window opened afterwards - an asset editor, a panel dragged out to float. This is the one the guess used to land on.
		var second = new RootPanel();
		Window( second );
		EditorOverlay.For( second );

		var popup = new PopupWidget( owner );
		popup.OpenAt( new Vector2( 40, 40 ), animate: false );

		Assert.AreSame( EditorOverlay.Find( first ), popup.Surface.Parent,
			"it belongs to the window that opened it, not the newest one" );

		popup.Destroy();
	}
}
