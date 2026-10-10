using Editor;
using Rinten;
using Rinten.UI;

namespace EngineTests;

//Inspector controls that stack two or more rows in a column.
[TestClass]
[DoNotParallelize]
public class EditorInspectorColumnTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;

		if ( Theme.RowHeight <= 0.0f )
			Theme.RowHeight = 28.0f;
	}

	private static void Arrange( Widget widget )
	{
		var root = widget.Surface?.FindRootPanel();

		if ( root is null )
		{
			var size = widget.Size;

			if ( size.x <= 0 ) size.x = 800;
			if ( size.y <= 0 ) size.y = 600;

			root = new RootPanel();
			root.PanelBounds = new Rect( 0, 0, size.x, size.y );
			root.AddChild( widget.Surface );
		}

		widget.SettleFlexSubtree();
	}

	private static float TwoRowsWanted() => Theme.RowHeight * 2 + 2;

	//One row: wide size hint, minimum height one row.
	private sealed class RowLine : Widget
	{
		public RowLine( Widget parent ) : base( parent )
		{
			MinimumWidth = 60;
			HorizontalSizeMode = SizeMode.CanGrow;
		}

		protected override Vector2 SizeHint() => new( 1000, 0 );

		protected override Vector2 MinimumSizeHint() => Theme.RowHeight;
	}

	//Column of two row lines with no vertical size hint.
	private sealed class TwoRowColumn : Widget
	{
		public readonly RowLine Top;
		public readonly RowLine Bottom;

		public TwoRowColumn( Widget parent ) : base( parent )
		{
			Layout = Layout.Column();
			Layout.Spacing = 2;

			Top = Layout.Add( new RowLine( this ) );
			Bottom = Layout.Add( new RowLine( this ) );
		}

		protected override Vector2 SizeHint() => new( 1000, 0 );

		protected override Vector2 MinimumSizeHint() => Theme.RowHeight;
	}

	//Column of two rows with a one-row vertical size hint.
	private sealed class HintedOneRowColumn : Widget
	{
		public HintedOneRowColumn( Widget parent ) : base( parent )
		{
			Layout = Layout.Column();
			Layout.Spacing = 2;

			Layout.Add( new RowLine( this ) );
			Layout.Add( new RowLine( this ) );
		}

		protected override Vector2 SizeHint() => new( 1000, Theme.RowHeight );
	}

	[TestMethod]
	public void AfterArrangeAFlexColumnFillsItsHolder()
	{
		var holder = new Widget( null );
		holder.Size = new Vector2( 200, 80 );

		var column = new TwoRowColumn( holder );
		holder.Layout = Layout.Column();
		holder.Layout.Add( column, 1 );

		Arrange( holder );

		Assert.AreEqual( holder.Size.y, column.Size.y, 0.5f,
			"flex gives the column the height its holder had spare" );
	}

	[TestMethod]
	public void AHintedColumnAsksForItsLayoutNotJustOneRow()
	{
		var column = new HintedOneRowColumn( null );

		var cell = new Layout.Cell { Widget = column };
		var want = Layout.WantsOf( cell, vertical: true );

		Assert.IsTrue( want > Theme.RowHeight,
			"size hint on y is one row; layout must ask for more" );
		Assert.IsTrue( want >= TwoRowsWanted() - 0.5f,
			$"column should ask for at least two rows ({want} vs {TwoRowsWanted()})" );
	}

	[TestMethod]
	public void ATwoRowColumnGetsEnoughHeightInAnInspectorRow()
	{
		var row = new Widget( null );
		row.Size = new Vector2( 320, 120 );

		var grid = new GridLayout();
		row.Layout = grid;

		var label = new Widget( row );
		label.MinimumWidth = 100;
		label.MinimumHeight = Theme.RowHeight;

		var column = new TwoRowColumn( row );
		column.HorizontalSizeMode = SizeMode.Flexible;

		grid.AddCell( 2, 5, label, alignment: TextFlag.LeftTop );
		grid.AddCell( 3, 5, column, alignment: TextFlag.LeftTop );

		grid.SetColumnStretch( 0, 0, 0, 1 );
		grid.SetMinimumColumnWidth( 0, 0 );
		grid.SetMinimumColumnWidth( 1, 0 );
		grid.SetMinimumColumnWidth( 2, 140 );

		Arrange( row );

		Assert.IsTrue( column.Size.y >= TwoRowsWanted() - 0.5f,
			$"column height {column.Size.y} should cover two rows" );

		Assert.IsTrue( column.Bottom.Position.y > column.Top.Position.y,
			"mins and maxs rows must not overlap" );
	}
}
