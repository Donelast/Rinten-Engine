using Rinten.Engine;
using Rinten.UI;
using UITests.Panels;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class PopupPositionTest
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	//Nested panels should report a screen style rect at the accumulated position.
	[TestMethod]
	public void GetScreenStyleRectMatchesNestedLayoutPosition()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var outer = new Panel { Parent = root };
		outer.Style.Set( "position: absolute; left: 100px; top: 200px; width: 400px; height: 300px;" );

		var inner = new Panel { Parent = outer };
		inner.Style.Set( "position: absolute; left: 50px; top: 30px; width: 120px; height: 28px;" );

		root.Layout();

		var rect = inner.GetScreenStyleRect();
		var scale = inner.ScaleFromScreen;

		Assert.AreEqual( inner.Box.Rect.Left * scale, rect.Left, 0.01f );
		Assert.AreEqual( inner.Box.Rect.Bottom * scale, rect.Bottom, 0.01f );
		Assert.AreEqual( inner.Box.Rect.Width * scale, rect.Width, 0.01f );
	}

	//Scrolling a parent should move the child's screen style rect with it.
	[TestMethod]
	public void GetScreenStyleRectTracksScrolledContent()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var scroll = new Panel { Parent = root };
		scroll.Style.Set( "position: absolute; left: 10px; top: 20px; width: 200px; height: 80px; overflow: scroll;" );

		var content = new Panel { Parent = scroll };
		content.Style.Set( "position: absolute; left: 0px; top: 0px; width: 180px; height: 200px;" );

		var source = new Panel { Parent = content };
		source.Style.Set( "position: absolute; left: 0px; top: 120px; width: 100px; height: 24px;" );

		root.Layout();

		scroll.ScrollOffset = new Vector2( 0, 80 );
		root.Layout();

		var rect = source.GetScreenStyleRect();
		var scale = source.ScaleFromScreen;

		Assert.AreEqual( source.Box.Rect.Bottom * scale, rect.Bottom, 0.01f );
	}

	//Popups on the editor overlay use window pixels, not scaled style lengths.
	[TestMethod]
	public void OverlayPopupBelowStretchUsesWindowPixels()
	{
		var root = new ScalingRootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.SetUiScale( 2.0f );
		root.Layout();

		var overlay = new Panel { Parent = root };
		overlay.AddClass( "editor-overlay" );
		overlay.Style.Set( "position: absolute; left: 0px; top: 0px; width: 100%; height: 100%;" );

		var source = new Panel { Parent = root };
		source.Style.Set( "position: absolute; left: 64px; top: 96px; width: 160px; height: 28px;" );

		root.Layout();

		var sourceBox = source.Box.Rect;
		var overlayBox = overlay.Box.Rect;
		var anchorLeft = sourceBox.Left - overlayBox.Left;
		var anchorBottom = sourceBox.Bottom - overlayBox.Top;

		var popup = new Panel { Parent = overlay };
		popup.Style.Position = PositionMode.Absolute;
		popup.Style.Left = anchorLeft;
		popup.Style.Top = anchorBottom;
		popup.Style.Width = sourceBox.Width;
		root.Layout();

		Assert.AreEqual( Length.Pixels( anchorBottom ), popup.Style.Top );
		Assert.AreEqual( Length.Pixels( anchorLeft ), popup.Style.Left );
		Assert.AreEqual( Length.Pixels( sourceBox.Width ), popup.Style.Width );
	}

	//Popup clamp should use the root window, not the game viewport size.
	[TestMethod]
	public void PopupClampUsesRootPanelBounds()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 2048, 1114 );

		var layoutRect = new Rect( 1455, 765, 583, 78 );
		ClampPopupRect( root, ref layoutRect );

		Assert.AreEqual( 1455, layoutRect.Left, 0.01f );
		Assert.AreEqual( 765, layoutRect.Top, 0.01f );
		Assert.AreEqual( 583, layoutRect.Width, 0.01f );
	}

	//Using the game viewport width would wrongly shift a wide-window popup left.
	[TestMethod]
	public void GameViewportWidthWouldClampAPopupThatFitsTheEditorWindow()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 2048, 1114 );

		var layoutRect = new Rect( 1455, 765, 583, 78 );
		var wrongMaxX = Screen.Width - 10;

		Assert.IsTrue( layoutRect.Right > wrongMaxX,
			"a popup under the inspector fits the editor window but not the game viewport width" );

		ClampPopupRect( root, ref layoutRect );
		Assert.AreEqual( 1455, layoutRect.Left, 0.01f );
	}

	static void ClampPopupRect( RootPanel root, ref Rect layoutRect )
	{
		var bounds = root.PanelBounds;
		var padding = 10;
		var maxX = bounds.Right - padding;
		var maxY = bounds.Bottom - padding;

		if ( layoutRect.Bottom > maxY )
			layoutRect.Top -= layoutRect.Bottom - maxY;

		if ( layoutRect.Right > maxX )
			layoutRect.Left -= layoutRect.Right - maxX;

		if ( layoutRect.Left < bounds.Left )
			layoutRect.Left = bounds.Left;
	}
}
