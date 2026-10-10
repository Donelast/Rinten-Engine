using Rinten.Engine;
using Rinten.UI;

namespace UITests;

//The seam between panels and the layout engine: what a panel pushes, when it pushes, and
//what comes back. Real panels under a real root, laid out the way a frame lays them out.
[TestClass]
[DoNotParallelize] //Modifies UI System Global
public class PanelLayoutTests
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	private static RootPanel Root( float width = 1000, float height = 1000 )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, width, height );
		return root;
	}

	[TestMethod]
	public void LayoutMinimal()
	{
		var root = new PanelLayout( null );
		root.FlexDirection = FlexDirection.Column;
		root.AlignItems = Align.Stretch;
		root.Width = 100.0f;
		root.Height = 100.0f;

		var child = new PanelLayout( null );
		child.FlexGrow = 1;
		root.AddChild( child );

		root.CalculateLayout();

		Assert.AreEqual( 100.0f, child.LayoutWidth );
		Assert.AreEqual( 100.0f, child.LayoutHeight );
		Assert.AreEqual( 0f, child.LayoutX );
		Assert.AreEqual( 0f, child.LayoutY );
	}

	[TestMethod]
	public void LayoutSanity()
	{
		var root = new PanelLayout( null );
		root.Width = 1920;
		root.Height = 1080;

		var child = new PanelLayout( null );
		child.PositionType = PositionMode.Absolute;
		child.Top = 64;
		child.Left = 32;
		child.Width = 100;
		child.Height = 100;
		root.AddChild( child );

		var child2 = new PanelLayout( null );
		child2.PositionType = PositionMode.Absolute;
		child2.Right = 64;
		child2.Bottom = 32;
		child2.Width = 100;
		child2.Height = 100;
		root.AddChild( child2 );

		root.CalculateLayout();

		Assert.AreEqual( 1920f, root.LayoutWidth );
		Assert.AreEqual( 1080f, root.LayoutHeight );
		Assert.AreEqual( 32f, child.LayoutX );
		Assert.AreEqual( 64f, child.LayoutY );
		Assert.AreEqual( 1756f, child2.LayoutX );
		Assert.AreEqual( 948f, child2.LayoutY );
	}

	[TestMethod]
	public void StyleMutationRecalculatesLayout()
	{
		var root = Root();
		var p = new Panel { Parent = root };
		p.Style.Set( "width: 100px; height: 50px;" );
		root.Layout();
		Assert.AreEqual( 100f, p.Box.Rect.Width );

		p.Style.Set( "width: 250px; height: 50px;" );
		root.Layout();
		Assert.AreEqual( 250f, p.Box.Rect.Width );
	}

	//A width in em depends on the font size, which the panel inherits. When the parent's
	//font grows the child's own rules have not changed, and it must still relayout.
	[TestMethod]
	public void RelativeUnitsRelayoutWhenTheirReferenceChanges()
	{
		var root = Root();
		var parent = new Panel { Parent = root };
		parent.Style.Set( "font-size: 10px; width: 500px; height: 100px;" );
		var child = new Panel { Parent = parent };
		child.Style.Set( "width: 10em; height: 2em;" );
		root.Layout();
		Assert.AreEqual( 100f, child.Box.Rect.Width );
		Assert.AreEqual( 20f, child.Box.Rect.Height );

		parent.Style.Set( "font-size: 20px; width: 500px; height: 100px;" );
		root.Layout();
		Assert.AreEqual( 200f, child.Box.Rect.Width, "the child's em width follows the parent's font size" );
		Assert.AreEqual( 40f, child.Box.Rect.Height );
	}

	//calc() percentages resolve against the previous layout's parent size; dependency tracking catches up
	[TestMethod]
	public void CalcPercentagesFollowTheParentSize()
	{
		var root = Root();
		var parent = new Panel { Parent = root };
		parent.Style.Set( "width: 400px; height: 100px;" );
		var child = new Panel { Parent = parent };
		child.Style.Set( "width: calc( 50% - 10px ); height: 20px;" );
		root.Layout();
		root.Layout();
		Assert.AreEqual( 190f, child.Box.Rect.Width );

		parent.Style.Set( "width: 800px; height: 100px;" );
		root.Layout();
		root.Layout();
		Assert.AreEqual( 390f, child.Box.Rect.Width, "the calc() width follows the parent's width" );
	}

	//A cascade marks a panel changed for any property. A colour change on the parent reaches the child as
	//a cascade, but none of its layout inputs moved, so its style is not pushed into the layout tree again.
	[TestMethod]
	public void UnrelatedChangesDoNotRepushDescendants()
	{
		var root = Root();
		var parent = new Panel { Parent = root };
		parent.Style.Set( "width: 400px; height: 100px; background-color: red;" );
		var child = new Panel { Parent = parent };
		child.Style.Set( "width: 50px; height: 20px;" );
		root.Layout();

		var pushes = child.LayoutTree.PushCount;
		Assert.IsTrue( pushes >= 1 );

		parent.Style.Set( "width: 400px; height: 100px; background-color: blue;" );
		root.Layout();
		Assert.AreEqual( pushes, child.LayoutTree.PushCount, "a colour change on the parent must not push the child's layout style" );
		Assert.AreEqual( 50f, child.Box.Rect.Width );

		parent.Style.Set( "width: 400px; height: 100px; background-color: blue; padding-left: 30px;" );
		root.Layout();
		Assert.AreEqual( 30f, child.Box.Rect.Left, "a padding change on the parent still moves the child" );
	}

	[TestMethod]
	public void ChildReorderUpdatesLayoutOrder()
	{
		var root = Root();
		var row = new Panel { Parent = root };
		row.Style.Set( "flex-direction: row; width: 300px; height: 40px;" );
		var a = new Panel { Parent = row };
		a.Style.Set( "width: 50px; height: 40px;" );
		var b = new Panel { Parent = row };
		b.Style.Set( "width: 50px; height: 40px;" );
		root.Layout();
		Assert.AreEqual( 0f, a.Box.Rect.Left );
		Assert.AreEqual( 50f, b.Box.Rect.Left );

		row.SetChildIndex( b, 0 );
		root.Layout();
		Assert.AreEqual( 0f, b.Box.Rect.Left, "the moved child lays out first" );
		Assert.AreEqual( 50f, a.Box.Rect.Left );
	}

	[TestMethod]
	public void MeasuredControlsRelayoutWhenTheirContentChanges()
	{
		var root = Root();
		var row = new Panel { Parent = root };
		row.Style.Set( "flex-direction: row; width: 600px; height: 40px;" );
		var label = new Label( "short" ) { Parent = row };
		label.Style.Set( "font-size: 16px;" );
		var after = new Panel { Parent = row };
		after.Style.Set( "width: 10px; height: 10px;" );
		root.Layout();
		var narrow = after.Box.Rect.Left;

		label.Text = "a much longer piece of text than before";
		root.Layout();
		Assert.IsTrue( after.Box.Rect.Left > narrow, "the panel after a label moves when the label's text grows" );
	}

	[TestMethod]
	public void FlexBasisFollowsTheParentDirection()
	{
		var root = Root();
		var column = new Panel { Parent = root };
		column.Style.Set( "flex-direction: column; width: 200px; height: 400px;" );
		var child = new Panel { Parent = column };
		child.Style.Set( "flex-basis: 25%; width: 20px;" );
		root.Layout();
		Assert.AreEqual( 100f, child.Box.Rect.Height, "in a column the basis is a height" );
	}
}
