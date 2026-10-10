using Editor;
using Rinten;
using Rinten.UI;

namespace EngineTests;

//An inspector row with a vector control must keep its three fields inside the row's width
[TestClass]
[DoNotParallelize]
public class EditorInspectorRowTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;
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

	//One of a vector's fields: it asks for the moon and takes what it is given.
	private sealed class Field : Widget
	{
		public Field( Widget parent ) : base( parent )
		{
			MinimumWidth = 24;

			HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
			VerticalSizeMode = SizeMode.CanGrow;
		}

		protected override Vector2 SizeHint() => new( 1000, Theme.RowHeight );
	}

	//The vector control: a row of three of them.
	private sealed class Vector : Widget
	{
		public readonly Field X;
		public readonly Field Y;
		public readonly Field Z;

		public Vector( Widget parent ) : base( parent )
		{
			Layout = Layout.Row();
			Layout.Spacing = 2;

			X = Layout.Add( new Field( this ) );
			Y = Layout.Add( new Field( this ) );
			Z = Layout.Add( new Field( this ) );
		}

		//A control widget's own, and the reason any of this is interesting.
		protected override Vector2 SizeHint() => new( 1000, Theme.RowHeight );
	}

	[TestMethod]
	public void TheThreeFieldsAreSideBySideInsideTheRow()
	{
		var row = new Widget( null );

		row.Size = new Vector2( 320, 40 );

		var grid = new GridLayout();
		row.Layout = grid;

		var label = new Widget( row );
		label.MinimumWidth = 100;
		label.MinimumHeight = Theme.RowHeight;

		var vector = new Vector( row );
		vector.HorizontalSizeMode = SizeMode.Flexible;

		grid.AddCell( 2, 5, label, alignment: TextFlag.LeftTop );
		grid.AddCell( 3, 5, vector, alignment: TextFlag.LeftTop );

		grid.SetColumnStretch( 0, 0, 0, 1 );
		grid.SetMinimumColumnWidth( 0, 0 );
		grid.SetMinimumColumnWidth( 1, 0 );
		grid.SetMinimumColumnWidth( 2, 140 );

		Arrange( row );

		System.Console.WriteLine( $"row    {row.Position} {row.Size}" );
		System.Console.WriteLine( $"label  {label.Position} {label.Size}" );
		System.Console.WriteLine( $"vector {vector.Position} {vector.Size}" );
		System.Console.WriteLine( $"x      {vector.X.Position} {vector.X.Size}" );
		System.Console.WriteLine( $"y      {vector.Y.Position} {vector.Y.Size}" );
		System.Console.WriteLine( $"z      {vector.Z.Position} {vector.Z.Size}" );

		//The control fits the row it was put in.
		Assert.IsTrue( vector.Position.x + vector.Size.x <= row.Size.x + 0.5f,
			$"the control ends at {vector.Position.x + vector.Size.x}, past the row's {row.Size.x}" );

		//And the three fields are beside each other inside it.
		Assert.IsTrue( vector.Y.Position.x > vector.X.Position.x, "y is after x" );
		Assert.IsTrue( vector.Z.Position.x > vector.Y.Position.x, "z is after y" );

		Assert.IsTrue( vector.Z.Position.x + vector.Z.Size.x <= vector.Size.x + 0.5f,
			$"the last field ends at {vector.Z.Position.x + vector.Z.Size.x}, past the control's {vector.Size.x}" );

		//A third each, near enough.
		Assert.AreEqual( vector.X.Size.x, vector.Z.Size.x, 1.0f );
	}

	[TestMethod]
	public void TheLabelKeepsTheWidthOfItsWords()
	{
		var row = new Widget( null );
		row.Size = new Vector2( 320, 40 );

		var grid = new GridLayout();
		row.Layout = grid;

		var label = new Name( row );
		var control = new Widget( row );
		control.HorizontalSizeMode = SizeMode.Flexible;

		grid.AddCell( 2, 5, label, alignment: TextFlag.LeftTop );
		grid.AddCell( 3, 5, control, alignment: TextFlag.LeftTop );

		grid.SetColumnStretch( 0, 0, 0, 1 );
		grid.SetMinimumColumnWidth( 0, 0 );
		grid.SetMinimumColumnWidth( 1, 0 );

		Arrange( row );

		Assert.AreEqual( 48.0f, label.Size.x, 1.0f, "the label stays as wide as its words" );
		Assert.AreEqual( 48.0f, control.Position.x, 1.0f, "the field starts where the label ends" );
		Assert.AreEqual( 272.0f, control.Size.x, 1.0f, "and takes everything left" );
	}

	//A property name: as wide as its words, and no wider.
	private sealed class Name : Widget
	{
		public Name( Widget parent ) : base( parent )
		{
			MinimumHeight = Theme.RowHeight;
			HorizontalSizeMode = SizeMode.CanShrink;
		}

		protected override Vector2 SizeHint() => new( 48, Theme.RowHeight );
	}
}
