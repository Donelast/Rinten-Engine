using Editor;
using Rinten;
using Rinten.UI;
using System.Linq;

namespace EngineTests;

//Inspector grids and nested columns must not stretch rows to fill spare height.
[TestClass]
[DoNotParallelize]
public class EditorInspectorGridHeightTests
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

			if ( size.x <= 0 ) size.x = 320;
			if ( size.y <= 0 ) size.y = 600;

			root = new RootPanel();
			root.PanelBounds = new Rect( 0, 0, size.x, size.y );
			root.AddChild( widget.Surface );
		}

		widget.SettleFlexSubtree();
	}

	private sealed class FixedRow : Widget
	{
		public FixedRow( Widget parent ) : base( parent )
		{
			MinimumHeight = Theme.RowHeight;
			VerticalSizeMode = SizeMode.CanShrink;
		}

		protected override Vector2 MinimumSizeHint() => Theme.RowHeight;
	}

	[TestMethod]
	public void GridRowsStayContentHeightInTallArea()
	{
		var holder = new Widget( null );
		holder.Size = new Vector2( 280, 400 );

		var grid = new GridLayout();
		holder.Layout = grid;

		var top = new FixedRow( holder );
		var bottom = new FixedRow( holder );

		grid.AddCell( 0, 0, top );
		grid.AddCell( 0, 1, bottom );

		Arrange( holder );

		var maxRow = Theme.RowHeight + 4.0f;

		Assert.IsTrue( top.Size.y <= maxRow,
			$"top row height {top.Size.y} should stay one row tall" );
		Assert.IsTrue( bottom.Size.y <= maxRow,
			$"bottom row height {bottom.Size.y} should stay one row tall" );
		Assert.IsTrue( bottom.Position.y < top.Position.y + Theme.RowHeight + 4.0f,
			"rows should stay stacked without huge gaps" );
	}

	[TestMethod]
	public void NestedGridInFlexColumnDoesNotStretch()
	{
		var holder = new Widget( null );
		holder.Size = new Vector2( 280, 400 );

		var column = holder.Layout = Layout.Column();
		column.Spacing = 2;

		var grid = new GridLayout();
		column.Add( grid );

		for ( int i = 0; i < 3; i++ )
			grid.AddCell( 0, i, new FixedRow( holder ) );

		column.AddStretchCell( 1 );

		Arrange( holder );

		var first = grid.Cells[0].Widget!;
		var last = grid.Cells[2].Widget!;

		var maxRow = Theme.RowHeight + 4.0f;

		Assert.IsTrue( first.Size.y <= maxRow,
			$"first row height {first.Size.y} should stay one row tall" );
		Assert.IsTrue( last.Size.y <= maxRow,
			$"last row height {last.Size.y} should stay one row tall" );
		Assert.IsTrue( last.Position.y < first.Position.y + Theme.RowHeight * 3 + 8.0f,
			"grid rows should not spread across the column height" );
	}

	[TestMethod]
	public void TransformLikeColumnKeepsThreeRowsCompact()
	{
		var holder = new Widget( null );
		holder.Size = new Vector2( 280, 300 );

		var control = new Widget( holder );
		control.VerticalSizeMode = SizeMode.CanShrink;
		control.Layout = Layout.Column();
		control.Layout.Spacing = 2;

		for ( int i = 0; i < 3; i++ )
			control.Layout.Add( new FixedRow( control ) );

		holder.Layout = Layout.Column();
		holder.Layout.Add( control );

		Arrange( holder );

		var rows = control.Layout.Cells.Select( c => c.Widget ).ToList();
		Assert.AreEqual( 3, rows.Count );

		var maxRow = Theme.RowHeight + 4.0f;

		foreach ( var row in rows )
			Assert.IsTrue( row!.Size.y <= maxRow,
				$"row height {row.Size.y} should stay one row tall" );

		var span = rows[2]!.Position.y - rows[0]!.Position.y;
		Assert.IsTrue( span < Theme.RowHeight * 3 + 8.0f,
			$"three rows should span ~{Theme.RowHeight * 3}px, not {span}" );
	}
}
