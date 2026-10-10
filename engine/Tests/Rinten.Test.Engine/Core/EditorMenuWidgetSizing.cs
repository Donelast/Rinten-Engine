using Editor;
using Rinten;
using System.Linq;

namespace EngineTests;

//Menus must size embedded widgets from what their layout needs.
[TestClass]
[DoNotParallelize]
public class EditorMenuWidgetSizingTests
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
		widget.ArrangeIfNeeded();

		foreach ( var child in widget.childWidgets ) Arrange( child );
	}

	//One row with a minimum height of one row.
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

	[TestMethod]
	public void AWidgetWithAColumnGetsHeightFromItsLayout()
	{
		var menu = new Menu();

		var widget = new Widget( menu );
		widget.MinimumWidth = 320;

		var column = Layout.Column();
		column.Spacing = 2;

		column.Add( new RowLine( widget ) );
		column.Add( new RowLine( widget ) );
		column.Add( new RowLine( widget ) );

		widget.Layout = column;

		menu.AddWidget( widget );
		menu.Build();

		var expected = Theme.RowHeight * 3 + column.Spacing * 2;

		Assert.IsTrue( widget.Size.y >= expected - 0.5f,
			$"widget height {widget.Size.y} should cover three rows ({expected})" );

		Arrange( menu );

		var rows = widget.childWidgets.OfType<RowLine>().OrderBy( x => x.Position.y ).ToArray();

		Assert.AreEqual( 3, rows.Length );
		Assert.IsTrue( rows[2].Position.y > rows[1].Position.y, "rows must not overlap" );

		menu.Destroy();
	}

	[TestMethod]
	public void AWidgetAddedBeforeTheMenuHasASurfaceStillArranges()
	{
		var menu = new Menu();

		var widget = new Widget( menu );
		widget.MinimumWidth = 320;

		var column = Layout.Column();
		column.Spacing = 2;

		column.Add( new RowLine( widget ) );
		column.Add( new RowLine( widget ) );

		widget.Layout = column;

		menu.AddWidget( widget );
		menu.Build();

		Assert.AreSame( menu.Surface, widget.Surface.Parent,
			"embedded widget panel must hang off the menu" );

		var rows = widget.childWidgets.OfType<RowLine>().OrderBy( x => x.Position.y ).ToArray();

		Assert.AreEqual( 2, rows.Length );
		Assert.IsTrue( rows[1].Position.y > rows[0].Position.y, "rows must be laid out" );

		menu.Destroy();
	}

	[TestMethod]
	public void AWidgetWithAGridGetsHeightFromItsRows()
	{
		var menu = new Menu();

		var widget = new Widget( menu );
		widget.MinimumWidth = 320;

		var grid = new GridLayout();
		grid.VerticalSpacing = 2;

		var label = new Widget( widget );
		label.MinimumHeight = Theme.RowHeight;

		var control = new RowLine( widget );
		control.HorizontalSizeMode = SizeMode.Flexible;

		grid.AddCell( 2, 5, label, alignment: TextFlag.LeftTop );
		grid.AddCell( 3, 5, control, alignment: TextFlag.LeftTop );

		grid.SetColumnStretch( 0, 0, 0, 1 );
		grid.SetMinimumColumnWidth( 2, 140 );

		widget.Layout = grid;

		menu.AddWidget( widget );
		menu.Build();

		Assert.IsTrue( widget.Size.y >= Theme.RowHeight - 0.5f,
			$"grid row should be at least one row tall ({widget.Size.y})" );

		Arrange( menu );

		Assert.IsTrue( control.Size.y >= Theme.RowHeight - 0.5f,
			"control cell should not be squashed" );

		menu.Destroy();
	}
}
