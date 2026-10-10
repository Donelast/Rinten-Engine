using Editor;
using Rinten;

namespace EngineTests;

//Inspector canvas takes the panel's width, not its wanted 1000px, and grows down to its rows
[TestClass]
[DoNotParallelize]
public class EditorInspectorScrollTests
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

	//A control that asks for a thousand pixels, as they all do.
	private sealed class Control : Widget
	{
		public Control( Widget parent ) : base( parent )
		{
			MinimumHeight = Theme.RowHeight;
			HorizontalSizeMode = SizeMode.Flexible;
		}

		protected override Vector2 SizeHint() => new( 1000, Theme.RowHeight );
	}

	[TestMethod]
	public void TheSheetIsTheWidthOfThePanelAndAsTallAsItNeeds()
	{
		var scroll = new ScrollArea( null );
		scroll.Size = new Vector2( 320, 200 );

		var canvas = new Widget( scroll );

		//What the inspector says about its own canvas.
		canvas.HorizontalSizeMode = SizeMode.Flexible;
		canvas.Layout = Layout.Column();

		var rows = new Control[6];

		for ( var i = 0; i < rows.Length; i++ )
			rows[i] = canvas.Layout.Add( new Control( canvas ) );

		scroll.Canvas = canvas;

		Arrange( scroll );

		System.Console.WriteLine( $"canvas {canvas.Position} {canvas.Size}" );
		System.Console.WriteLine( $"row 0  {rows[0].Position} {rows[0].Size}" );

		//Across: the panel's width, so a row fits in it.
		Assert.AreEqual( 320.0f, canvas.Size.x, 0.5f,
			$"the canvas is {canvas.Size.x} wide in a panel of 320" );

		Assert.IsTrue( rows[0].Size.x <= 320.5f, $"a row is {rows[0].Size.x} wide" );

		//Down: as tall as the rows need, which is what there is to scroll.
		Assert.IsTrue( canvas.Size.y >= 200.0f, $"the canvas is {canvas.Size.y} tall" );
	}

	[TestMethod]
	public void ACanvasToldNotToScrollAcrossFitsThePanel()
	{
		var scroll = new ScrollArea( null );
		scroll.Size = new Vector2( 320, 200 );
		scroll.HorizontalScrollbarMode = ScrollbarMode.Off;

		var canvas = new Widget( scroll );
		canvas.Layout = Layout.Column();

		canvas.Layout.Add( new Control( canvas ) );

		scroll.Canvas = canvas;

		Arrange( scroll );

		Assert.AreEqual( 320.0f, canvas.Size.x, 0.5f,
			$"the canvas is {canvas.Size.x} wide in a panel of 320" );
	}
}
