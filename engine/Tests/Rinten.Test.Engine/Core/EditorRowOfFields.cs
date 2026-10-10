using Editor;
using Rinten;

namespace EngineTests;

//A row of controls that all want more room than there is.
[TestClass]
[DoNotParallelize]
public class EditorRowOfFieldsTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;
	}

	//A control that asks for a thousand pixels, as they all do.
	private sealed class Field : Widget
	{
		public Field() : base( null )
		{
			MinimumWidth = 24;

			//What a vector's fields say for themselves: they take what is going, and they are not fussy about being made smaller.
			HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
			VerticalSizeMode = SizeMode.CanGrow;
		}

		protected override Vector2 SizeHint() => new( 1000, Theme.RowHeight );
	}

	[TestMethod]
	public void ThreeFieldsShareTheRowRatherThanQueueingUpOutsideIt()
	{
		var row = new Widget( null );

		row.Size = new Vector2( 300, Theme.RowHeight );
		row.Layout = Layout.Row();
		row.Layout.Spacing = 2;

		var x = row.Layout.Add( new Field() );
		var y = row.Layout.Add( new Field() );
		var z = row.Layout.Add( new Field() );

		row.ArrangeIfNeeded();

		System.Console.WriteLine( $"x {x.Position} {x.Size}" );
		System.Console.WriteLine( $"y {y.Position} {y.Size}" );
		System.Console.WriteLine( $"z {z.Position} {z.Size}" );

		//Side by side, in order, and none of them off the end of the row.
		Assert.IsTrue( x.Position.x < y.Position.x, "y sits after x" );
		Assert.IsTrue( y.Position.x < z.Position.x, "z sits after y" );

		Assert.IsTrue( z.Position.x + z.Size.x <= row.Size.x + 0.5f,
			$"the last field ends at {z.Position.x + z.Size.x}, past the row's {row.Size.x}" );

		//A third each, near enough - the spacing between them is the difference.
		Assert.AreEqual( 300.0f / 3.0f, x.Size.x, 4.0f );
		Assert.AreEqual( x.Size.x, z.Size.x, 1.0f );

		//And none of them squeezed out of existence: a field still has to be wide enough to be a field.
		Assert.IsTrue( x.Size.x >= x.MinimumWidth, "a field is at least its minimum" );
	}

	//The same row, with no room at all to share. What has a minimum keeps it.
	[TestMethod]
	public void AFieldIsNeverSqueezedBelowItsMinimum()
	{
		var row = new Widget( null );

		row.Size = new Vector2( 40, Theme.RowHeight );
		row.Layout = Layout.Row();

		var x = row.Layout.Add( new Field() );
		var y = row.Layout.Add( new Field() );

		row.ArrangeIfNeeded();

		System.Console.WriteLine( $"tight x {x.Position} {x.Size}" );
		System.Console.WriteLine( $"tight y {y.Position} {y.Size}" );

		Assert.IsTrue( x.Size.x >= 24.0f, $"x got {x.Size.x}" );
		Assert.IsTrue( y.Size.x >= 24.0f, $"y got {y.Size.x}" );
	}

	//A thousand-pixel field beside a button already at its minimum stays inside the row.
	[TestMethod]
	public void AGreedyFieldShrinksBeforeAButtonThatIsAlreadyAtItsMinimum()
	{
		var row = new Widget( null );

		row.Size = new Vector2( 280, Theme.RowHeight );
		row.Layout = Layout.Row();
		row.Layout.Spacing = 4;

		var field = row.Layout.Add( new Field(), 1 );
		field.MinimumWidth = 60;
		field.HorizontalSizeMode = SizeMode.Flexible;

		var button = row.Layout.Add( new ButtonAtMinimum() );

		row.ArrangeIfNeeded();

		System.Console.WriteLine( $"field  {field.Position} {field.Size}" );
		System.Console.WriteLine( $"button {button.Position} {button.Size}" );

		Assert.AreEqual( 64.0f, button.Size.x, 0.5f, $"the button is {button.Size.x} wide" );
		Assert.IsTrue( button.Position.x + button.Size.x <= row.Size.x + 0.5f,
			$"the button ends at {button.Position.x + button.Size.x}, past the row's {row.Size.x}" );
		Assert.IsTrue( field.Size.x >= field.MinimumWidth - 0.5f, $"the field got {field.Size.x}" );
		Assert.IsTrue( field.Size.x < 200.0f, $"the field is still {field.Size.x} wide" );
	}

	//A button that is already as wide as its minimum.
	private sealed class ButtonAtMinimum : Widget
	{
		public ButtonAtMinimum() : base( null )
		{
			MinimumWidth = 64;
			MaximumWidth = 64;
			HorizontalSizeMode = SizeMode.CanShrink;
		}

		protected override Vector2 SizeHint() => new( 64, Theme.RowHeight );
	}
}
