using Editor;
using Rinten.UI;

namespace EngineTests;

//Popups have to sit on the window overlay, not inside the widget that opened them.
[TestClass]
[DoNotParallelize]
public class EditorPopupOverlayTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();
		EditorBackend.Panels = true;
	}

	[TestMethod]
	public void APopupOpenedFromInsideAScrollAreaSitsOnTheOverlay()
	{
		var root = new RootPanel();
		var host = new Widget( null );
		host.Size = new Vector2( 800, 600 );
		root.AddChild( host.Surface );

		EditorOverlay.For( root );

		var clip = new ScrollArea( host );
		clip.Size = new Vector2( 200, 200 );
		clip.Position = new Vector2( 500, 0 );

		var inspector = new Widget( clip );
		inspector.Size = new Vector2( 200, 800 );

		var popup = new PopupWidget( inspector );
		popup.MinimumWidth = 180;
		popup.MinimumHeight = 240;
		popup.OpenAt( new Vector2( 520, 180 ), animate: false );

		try
		{
			Assert.IsTrue( popup.IsValid, "opening it should leave it alive" );
			Assert.IsTrue( popup.Visible, "and on screen" );
			Assert.IsTrue( popup.Surface.Parent is EditorOverlay,
				"it belongs on the overlay, not inside the inspector that would clip it" );
			Assert.AreEqual( new Vector2( 520, 180 ), popup.Position );
		}
		finally
		{
			popup.Destroy();
			host.Destroy();
			root.Delete();
		}
	}

	[TestMethod]
	public void OverlayAnchorUsesWidgetScreenSpaceInsideASurface()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 800, 600 );

		var host = new Widget( null );
		host.Size = new Vector2( 400, 600 );
		host.Position = new Vector2( 100, 0 );
		root.AddChild( host.Surface );

		EditorOverlay.For( root );

		var source = new Panel();
		source.Style.Position = PositionMode.Absolute;
		source.Style.Left = 50;
		source.Style.Top = 120;
		source.Style.Width = 200;
		source.Style.Height = 28;
		host.Surface.AddChild( source );

		root.PreLayout( new Rect( 0, 0, 800, 600 ) );
		root.CalculateLayout();
		root.PostLayout();
		host.ArrangeIfNeeded();

		var anchor = source.GetOverlayAnchorRect();
		var pixel = source.GetScreenPixelRect();
		var surfacePixel = host.Surface.GetScreenPixelRect();
		var expected = pixel.Position + (host.ScreenPosition - surfacePixel.Position);

		Assert.AreEqual( expected.x, anchor.Left, 1.0f );
		Assert.AreEqual( expected.y, anchor.Top, 1.0f );
	}

	[TestMethod]
	public void APopupInsideAScrollAreaMountsOnTheScrollParent()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 800, 600 );

		var host = new Widget( null );
		host.Size = new Vector2( 400, 600 );
		host.Position = new Vector2( 100, 0 );
		root.AddChild( host.Surface );

		EditorOverlay.For( root );

		var scroll = new Panel();
		scroll.Style.FlexGrow = 1;
		scroll.Style.Overflow = OverflowMode.Scroll;
		host.Surface.AddChild( scroll );

		var body = scroll.AddChild<Panel>( "body" );
		body.Style.FlexDirection = FlexDirection.Column;

		var source = body.AddChild<Panel>( "source" );
		source.Style.Height = 28;
		source.Style.Width = 200;

		scroll.ScrollOffset = new Vector2( 0, 82 );

		root.PreLayout( new Rect( 0, 0, 800, 600 ) );
		root.CalculateLayout();
		root.PostLayout();
		host.ArrangeIfNeeded();

		var mount = source.FindScrollPopupMount();

		Assert.IsNotNull( mount );
		Assert.AreSame( host.Surface, mount );

		root.Delete();
	}

	[TestMethod]
	public void ABelowStretchPopupInAScrollAreaAlignsUnderItsSource()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 2048, 1114 );

		var host = new Widget( null );
		host.Size = new Vector2( 400, 600 );
		host.Position = new Vector2( 100, 0 );
		root.AddChild( host.Surface );

		EditorOverlay.For( root );

		var scroll = host.Surface.AddChild<Panel>( "scroll" );
		scroll.Style.FlexGrow = 1;
		scroll.Style.Overflow = OverflowMode.Scroll;

		var body = scroll.AddChild<Panel>( "body" );
		body.Style.FlexDirection = FlexDirection.Column;
		body.Style.MinHeight = 800;

		var spacer = body.AddChild<Panel>( "spacer" );
		spacer.Style.Height = 400;
		spacer.Style.Width = 200;

		var source = body.AddChild<Panel>( "source" );
		source.Style.Height = 28;
		source.Style.Width = 200;

		scroll.ScrollOffset = new Vector2( 0, 200 );

		root.PreLayout( new Rect( 0, 0, 2048, 1114 ) );
		root.CalculateLayout();
		root.PostLayout();
		host.ArrangeIfNeeded();

		var mount = source.FindScrollPopupMount();
		Assert.AreSame( host.Surface, mount );

		var sourcePixel = source.GetScreenPixelRect();
		var parentPixel = mount.GetScreenPixelRect();
		var wanted = new Vector2( sourcePixel.Left, sourcePixel.Bottom );
		var placed = source.PlaceOverlayPopup( wanted, new Vector2( sourcePixel.Width, 40 ) );

		var popup = new BelowStretchPopupPanel { Parent = mount, PopupSource = source };
		popup.AddClass( "popup-panel" );
		popup.Style.Position = PositionMode.Absolute;
		popup.Style.Left = placed.x - parentPixel.Left;
		popup.Style.Top = placed.y - parentPixel.Top;
		popup.Style.Width = sourcePixel.Width;
		popup.Style.MinHeight = 40;

		root.PreLayout( new Rect( 0, 0, 2048, 1114 ) );
		root.CalculateLayout();
		root.PostLayout();

		var popupPixel = popup.GetScreenPixelRect();

		Assert.AreEqual( sourcePixel.Left, popupPixel.Left, 10.0f );
		Assert.AreEqual( sourcePixel.Bottom, popupPixel.Top, 10.0f );
		Assert.AreEqual( sourcePixel.Width, popupPixel.Width, 1.0f );

		root.Delete();
	}

	sealed class BelowStretchPopupPanel : Panel
	{
		public Panel PopupSource { get; set; }

		public override void OnLayout( ref Rect layoutRect )
		{
			if ( PopupSource?.IsValid() == true )
				layoutRect.Width = PopupSource.GetScreenPixelRect().Width;

			var root = FindRootPanel();
			var bounds = root?.PanelBounds ?? new Rect( 0, 0, Screen.Width, Screen.Height );
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

	[TestMethod]
	public void ClickingAwayFromAPopupClosesIt()
	{
		var host = new Widget( null );
		host.Size = new Vector2( 800, 600 );

		var popup = new PopupWidget( host );
		popup.OpenAt( new Vector2( 40, 40 ), animate: false );

		var inside = new Widget( popup );

		try
		{
			PopupWidget.PressedOn( inside );
			Assert.IsTrue( popup.IsValid, "a press on the popup itself should leave it open" );

			PopupWidget.PressedOn( host );
			Assert.IsFalse( popup.IsValid, "a press anywhere else should close it" );
		}
		finally
		{
			if ( popup.IsValid ) popup.Destroy();
			host.Destroy();
		}
	}

	[TestMethod]
	public void ADropdownSearchBarSitsAboveTheListAndMatchesItsWidth()
	{
		var host = new Widget( null );
		host.Size = new Vector2( 800, 600 );

		var dropdown = new AdvancedDropdownWidget( host );
		dropdown.Size = dropdown.WantedSize();

		try
		{
			dropdown.ArrangeIfNeeded();
			foreach ( var child in dropdown.childWidgets )
			{
				child.ArrangeIfNeeded();
			}

			Assert.IsTrue( dropdown.Size.y > dropdown.ContentSize.y,
				"the popup is taller than the list, because the search bar sits above it" );
			Assert.IsTrue( dropdown.Search.Position.y < 12,
				"the search bar is at the top" );
			Assert.AreEqual( dropdown.Size.x, dropdown.Search.Size.x, 1.0f,
				"and as wide as the list under it" );
		}
		finally
		{
			dropdown.Destroy();
			host.Destroy();
		}
	}
}
