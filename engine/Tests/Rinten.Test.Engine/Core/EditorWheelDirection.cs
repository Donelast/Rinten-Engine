using Editor;
using Rinten;

namespace EngineTests;

//Which way the wheel turns things.
[TestClass]
[DoNotParallelize]
public class EditorWheelDirectionTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;
	}

	//A widget that keeps whatever the wheel told it.
	private sealed class Listener : Widget
	{
		public float Delta;

		public Listener() : base( null ) { }

		protected override void OnMouseWheel( WheelEvent e )
		{
			Delta = e.Delta;
			e.Accept();
		}
	}

	//A panel says positive is down, and a widget hears answer: up is a hundred and twenty.
	[TestMethod]
	public void AWidgetHearsUpAsAPositiveNotch()
	{
		var widget = new Listener();
		widget.Size = new Vector2( 100, 100 );

		//What the panel system hands over for one notch upwards: it flips SDL's own sign as the event arrives (PanelInput.AddMouseWheel).
		widget.Surface.OnMouseWheel( new Vector2( 0, -1 ) );

		Assert.AreEqual( 120.0f, widget.Delta, 0.01f, "one notch up is +120 to a widget" );

		widget.Surface.OnMouseWheel( new Vector2( 0, 1 ) );

		Assert.AreEqual( -120.0f, widget.Delta, 0.01f, "one notch down is -120" );
	}

	//And a scrolling area moves towards the top of what it holds, which is the end everybody expects the wheel to take them to.
	[TestMethod]
	public void ScrollingUpMovesTowardsTheTop()
	{
		var scroll = new ScrollArea( null );
		scroll.Size = new Vector2( 100, 100 );

		var canvas = new Widget( null );
		canvas.Size = new Vector2( 100, 1000 );

		scroll.Canvas = canvas;
		scroll.ArrangeIfNeeded();

		//Somewhere in the middle, so there is room to go either way.
		scroll.ScrollOffset = new Vector2( 0, 400 );

		var from = scroll.ScrollOffset.y;

		scroll.Surface.OnMouseWheel( new Vector2( 0, -1 ) );

		System.Console.WriteLine( $"up: {from} -> {scroll.ScrollOffset.y}" );

		Assert.IsTrue( scroll.ScrollOffset.y < from,
			$"the wheel went up and the list went from {from} to {scroll.ScrollOffset.y}" );

		from = scroll.ScrollOffset.y;

		scroll.Surface.OnMouseWheel( new Vector2( 0, 1 ) );

		System.Console.WriteLine( $"down: {from} -> {scroll.ScrollOffset.y}" );

		Assert.IsTrue( scroll.ScrollOffset.y > from, "and down goes the other way" );
	}
}
