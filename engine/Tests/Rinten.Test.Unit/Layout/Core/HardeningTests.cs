using System;
using System.Collections.Generic;
using Rinten.Layout;

namespace LayoutTests.Core;

//Invariants of the node API and the cache, written against the production engine directly. The ones the current port
//does not satisfy are ignored with the phase that enables them, so the list doubles as the checklist for those phases.
[TestClass]
public class HardeningTests
{
	//What the panels build their nodes with: web defaults, no pixel snapping.
	private static readonly LayoutConfig UiConfig = new() { UseWebDefaults = true, PointScaleFactor = 0 };

	private static LayoutNode Node() => new( UiConfig );

	[TestMethod]
	[DataRow( -1f, 10.4f, 0f, 10.4f )]
	[DataRow( float.NaN, 10.4f, 0f, 10.4f )]
	[DataRow( 20.4f, -1f, 20.4f, 0f )]
	[DataRow( 20.4f, float.NaN, 20.4f, 0f )]
	[DataRow( -1f, float.NaN, 0f, 0f )]
	[DataRow( float.NaN, -1f, 0f, 0f )]
	public void InvalidMeasuredDimensionsAreSanitized( float width, float height, float expectedWidth, float expectedHeight )
	{
		var node = Node();
		node.SetMeasureFunc( ( _, _, _, _, _ ) => new LayoutSize( width, height ) );
		var size = node.Measure( float.NaN, MeasureMode.Undefined, float.NaN, MeasureMode.Undefined );
		Assert.AreEqual( expectedWidth, size.Width );
		Assert.AreEqual( expectedHeight, size.Height );

		FlexLayout.Calculate( node, float.NaN, float.NaN, Direction.LTR );
		Assert.AreEqual( expectedWidth, node.Layout.GetDimension( Dimension.Width ), 0.001f );
		Assert.AreEqual( expectedHeight, node.Layout.GetDimension( Dimension.Height ), 0.001f );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FractionalSizesAreNotRounded( bool measured )
	{
		var node = Node();
		if ( measured )
		{
			node.SetMeasureFunc( ( _, _, _, _, _ ) => new LayoutSize( 20.4f, 10.4f ) );
		}
		else
		{
			node.Style.SetDimension( Dimension.Width, SizeLength.Points( 20.4f ) );
			node.Style.SetDimension( Dimension.Height, SizeLength.Points( 10.4f ) );
		}

		for ( var pass = 0; pass < 2; pass++ )
		{
			FlexLayout.Calculate( node, float.NaN, float.NaN, Direction.LTR );
			Assert.AreEqual( 20.4f, node.Layout.GetDimension( Dimension.Width ), 0.001f );
			Assert.AreEqual( 10.4f, node.Layout.GetDimension( Dimension.Height ), 0.001f );
		}
	}

	[TestMethod]
	public void RemovingAChildDetachesAndResetsIt()
	{
		var root = Node();
		var child = Node();
		child.Style.SetDimension( Dimension.Width, SizeLength.Points( 10 ) );
		child.Style.SetDimension( Dimension.Height, SizeLength.Points( 10 ) );
		root.InsertChild( child, 0 );
		FlexLayout.Calculate( root, 100, 100, Direction.LTR );
		Assert.AreEqual( 10f, child.Layout.GetDimension( Dimension.Width ) );

		Assert.IsTrue( root.RemoveChild( child ) );
		Assert.IsNull( child.Owner );
		Assert.AreEqual( 0, root.ChildCount );
		Assert.IsTrue( root.IsDirty );
		Assert.IsTrue( float.IsNaN( child.Layout.GetDimension( Dimension.Width ) ) );
		Assert.IsFalse( root.RemoveChild( child ) );
	}

	[TestMethod]
	public void StyleChangeAfterLayoutDirtiesThePathToTheRoot()
	{
		var root = Node();
		var child = Node();
		var grandchild = Node();
		root.InsertChild( child, 0 );
		child.InsertChild( grandchild, 0 );
		FlexLayout.Calculate( root, 100, 100, Direction.LTR );
		Assert.IsFalse( root.IsDirty || child.IsDirty || grandchild.IsDirty );

		grandchild.Style.SetDimension( Dimension.Width, SizeLength.Points( 5 ) );
		grandchild.MarkDirtyAndPropagate();
		Assert.IsTrue( grandchild.IsDirty && child.IsDirty && root.IsDirty );
	}

	[TestMethod]
	public void LayoutCacheRestoresOverflowResult()
	{
		var root = Node();
		root.Style.SetDimension( Dimension.Width, SizeLength.Points( 100 ) );
		root.Style.SetDimension( Dimension.Height, SizeLength.Points( 20 ) );
		var child = Node();
		child.Style.SetDimension( Dimension.Width, SizeLength.Points( 200 ) );
		child.Style.FlexShrink = 0;
		root.InsertChild( child, 0 );

		FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR );
		Assert.IsTrue( root.Layout.HadOverflow );

		root.Layout.HadOverflow = false;
		FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR );
		Assert.IsTrue( root.Layout.HadOverflow );
	}

	[TestMethod]
	public void LayoutCacheIncludesPercentageReferenceSize()
	{
		var node = Node();
		node.Style.SetDimension( Dimension.Width, SizeLength.Points( 100 ) );
		node.Style.SetDimension( Dimension.Height, SizeLength.Points( 20 ) );
		node.Style.SetPadding( Edge.Left, StyleLength.Percent( 10 ) );

		FlexLayout.Calculate( node, 200, 100, Direction.LTR );
		Assert.AreEqual( 20f, node.Layout.Padding( PhysicalEdge.Left ) );

		FlexLayout.Calculate( node, 400, 100, Direction.LTR );
		Assert.AreEqual( 40f, node.Layout.Padding( PhysicalEdge.Left ) );
	}

	[TestMethod]
	public void TreeRejectsSelfAndAncestorCycles()
	{
		var root = Node();
		var child = Node();
		var grandchild = Node();
		root.InsertChild( child, 0 );
		child.InsertChild( grandchild, 0 );

		Assert.ThrowsException<InvalidOperationException>( () => root.InsertChild( root, 0 ) );
		Assert.ThrowsException<InvalidOperationException>( () => grandchild.InsertChild( root, 0 ) );
		Assert.AreSame( root, child.Owner );
		Assert.AreSame( child, grandchild.Owner );
	}

	[TestMethod]
	public void ChildrenCannotMutateTheTree()
	{
		var root = Node();
		var child = Node();
		root.InsertChild( child, 0 );

		var children = (ICollection<LayoutNode>)root.Children;
		Assert.IsTrue( children.IsReadOnly );
		Assert.ThrowsException<NotSupportedException>( () => children.Add( Node() ) );
	}

	[TestMethod]
	public void BaselineCallbackDrivesBaselineAlignment()
	{
		var root = Node();
		root.Style.FlexDirection = FlexDirection.Row;
		root.Style.AlignItems = Align.Baseline;
		root.Style.SetDimension( Dimension.Width, SizeLength.Points( 200 ) );
		root.Style.SetDimension( Dimension.Height, SizeLength.Points( 100 ) );

		var a = Node();
		a.SetMeasureFunc( ( _, _, _, _, _ ) => new LayoutSize( 50, 40 ) );
		a.SetBaselineFunc( ( _, _, _ ) => 10 );

		var b = Node();
		b.SetMeasureFunc( ( _, _, _, _, _ ) => new LayoutSize( 50, 20 ) );
		b.SetBaselineFunc( ( _, _, _ ) => 15 );

		root.InsertChild( a, 0 );
		root.InsertChild( b, 1 );
		FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR );

		//The baselines meet 15 down from the line: a sits 5 lower than b.
		Assert.AreEqual( 5, a.Layout.Position( PhysicalEdge.Top ) );
		Assert.AreEqual( 0, b.Layout.Position( PhysicalEdge.Top ) );

		a.SetBaselineFunc( null );
		Assert.IsFalse( a.HasBaselineFunc );
		FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR );

		//Without one a leaf's baseline is its bottom: 40 against b's 15.
		Assert.AreEqual( 0, a.Layout.Position( PhysicalEdge.Top ) );
		Assert.AreEqual( 25, b.Layout.Position( PhysicalEdge.Top ) );
	}

	[TestMethod]
	public void FixedNodeIsPlacedAgainstTheRoot()
	{
		var root = Node();
		root.Style.SetDimension( Dimension.Width, SizeLength.Points( 300 ) );
		root.Style.SetDimension( Dimension.Height, SizeLength.Points( 200 ) );

		//A positioned box, padded, with another positioned box inside: neither contains a fixed node.
		var mid = Node();
		mid.Style.PositionType = PositionType.Relative;
		mid.Style.SetMargin( Edge.Left, StyleLength.Points( 20 ) );
		mid.Style.SetMargin( Edge.Top, StyleLength.Points( 30 ) );
		mid.Style.SetPadding( Edge.All, StyleLength.Points( 10 ) );
		mid.Style.SetDimension( Dimension.Width, SizeLength.Points( 200 ) );
		mid.Style.SetDimension( Dimension.Height, SizeLength.Points( 100 ) );

		var inner = Node();
		inner.Style.PositionType = PositionType.Relative;
		inner.Style.SetDimension( Dimension.Width, SizeLength.Points( 100 ) );
		inner.Style.SetDimension( Dimension.Height, SizeLength.Points( 50 ) );

		var pinned = Node();
		pinned.Style.PositionType = PositionType.Fixed;
		pinned.Style.SetPosition( Edge.Left, StyleLength.Points( 10 ) );
		pinned.Style.SetPosition( Edge.Top, StyleLength.Points( 20 ) );
		pinned.Style.SetDimension( Dimension.Width, SizeLength.Points( 50 ) );
		pinned.Style.SetDimension( Dimension.Height, SizeLength.Points( 30 ) );

		var wide = Node();
		wide.Style.PositionType = PositionType.Fixed;
		wide.Style.SetPosition( Edge.Left, StyleLength.Points( 0 ) );
		wide.Style.SetPosition( Edge.Right, StyleLength.Points( 0 ) );
		wide.Style.SetDimension( Dimension.Height, SizeLength.Points( 10 ) );

		root.InsertChild( mid, 0 );
		mid.InsertChild( inner, 0 );
		inner.InsertChild( pinned, 0 );
		inner.InsertChild( wide, 1 );

		Assert.IsTrue( root.SubtreeHasFixed );
		Assert.IsTrue( inner.SubtreeHasFixed );

		FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR );

		//inner sits at (30, 40) in the root; the fixed boxes report positions relative to inner.
		Assert.AreEqual( 10 - 30, pinned.Layout.Position( PhysicalEdge.Left ) );
		Assert.AreEqual( 20 - 40, pinned.Layout.Position( PhysicalEdge.Top ) );
		Assert.AreEqual( 50, pinned.Layout.GetDimension( Dimension.Width ) );
		Assert.AreEqual( -30, wide.Layout.Position( PhysicalEdge.Left ) );
		Assert.AreEqual( 300, wide.Layout.GetDimension( Dimension.Width ) );

		//Back in the flow, the count goes with it.
		pinned.Style.PositionType = PositionType.Static;
		wide.Style.PositionType = PositionType.Static;
		Assert.IsFalse( root.SubtreeHasFixed );
	}

	[TestMethod]
	public void FixedNodeInsideAbsoluteIsPlacedAgainstTheRoot()
	{
		var root = Node();
		root.Style.SetDimension( Dimension.Width, SizeLength.Points( 300 ) );
		root.Style.SetDimension( Dimension.Height, SizeLength.Points( 200 ) );

		var floating = Node();
		floating.Style.PositionType = PositionType.Absolute;
		floating.Style.SetPosition( Edge.Left, StyleLength.Points( 100 ) );
		floating.Style.SetPosition( Edge.Top, StyleLength.Points( 50 ) );
		floating.Style.SetDimension( Dimension.Width, SizeLength.Points( 100 ) );
		floating.Style.SetDimension( Dimension.Height, SizeLength.Points( 100 ) );

		var pinned = Node();
		pinned.Style.PositionType = PositionType.Fixed;
		pinned.Style.SetPosition( Edge.Left, StyleLength.Points( 0 ) );
		pinned.Style.SetPosition( Edge.Top, StyleLength.Points( 0 ) );
		pinned.Style.SetDimension( Dimension.Width, SizeLength.Points( 50 ) );
		pinned.Style.SetDimension( Dimension.Height, SizeLength.Points( 30 ) );

		root.InsertChild( floating, 0 );
		floating.InsertChild( pinned, 0 );
		FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR );

		Assert.AreEqual( -100, pinned.Layout.Position( PhysicalEdge.Left ) );
		Assert.AreEqual( -50, pinned.Layout.Position( PhysicalEdge.Top ) );
	}

	[TestMethod]
	public void InsertingAnOwnedNodeMovesIt()
	{
		var a = Node();
		var b = Node();
		var child = Node();
		a.InsertChild( child, 0 );
		b.InsertChild( child, 0 );

		Assert.AreEqual( 0, a.ChildCount );
		Assert.AreEqual( 1, b.ChildCount );
		Assert.AreSame( b, child.Owner );
	}

	[TestMethod]
	public void ContentsChildrenAreReplacedByTheirOwnChildren()
	{
		var root = Node();
		root.Style.FlexDirection = FlexDirection.Row;
		root.Style.SetDimension( Dimension.Width, SizeLength.Points( 100 ) );
		root.Style.SetDimension( Dimension.Height, SizeLength.Points( 20 ) );
		var contents = Node();
		contents.Style.Display = Display.Contents;
		var inner = Node();
		inner.Style.SetDimension( Dimension.Width, SizeLength.Points( 30 ) );
		root.InsertChild( contents, 0 );
		contents.InsertChild( inner, 0 );

		FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR );
		Assert.AreEqual( 30f, inner.Layout.GetDimension( Dimension.Width ) );
		Assert.AreEqual( 20f, inner.Layout.GetDimension( Dimension.Height ) );
		Assert.AreEqual( 0f, contents.Layout.GetDimension( Dimension.Width ) );
	}
}
