using System;
using System.Collections.Generic;
using Rinten.Layout;

namespace LayoutTests.Core;

//The cache's reuse rules for containers: an answer given under one constraint stands under another it fits, unless something
//below can make the answer depend on the room offered. Visits past the cache are counted through the engine's trace hook.
[TestClass]
public class ReuseTests
{
	private static readonly LayoutConfig Config = new() { UseWebDefaults = true, PointScaleFactor = 0 };

	private static LayoutNode Node()
	{
		var node = new LayoutNode( Config );
		node.Style.PositionType = PositionType.Static;
		return node;
	}

	//Eight units a character, sixteen a line; wraps by whole characters when held to a width.
	private static LayoutSize MeasureText( LayoutNode node, float width, MeasureMode widthMode, float height, MeasureMode heightMode )
	{
		var text = (string)node.Context;
		var natural = 8f * text.Length;

		if ( widthMode == MeasureMode.Undefined || natural <= width )
			return new LayoutSize( widthMode == MeasureMode.Exactly ? width : natural, 16f );

		var perLine = Math.Max( 1, (int)( width / 8f ) );
		var lines = ( text.Length + perLine - 1 ) / perLine;
		return new LayoutSize( widthMode == MeasureMode.Exactly ? width : 8f * perLine, 16f * lines );
	}

	private static LayoutNode Label( string text )
	{
		var node = Node();
		node.Context = text;
		node.SetMeasureFunc( MeasureText );
		return node;
	}

	//A fixed-size root holding one auto-sized group of two labels.
	private static (LayoutNode Root, LayoutNode Group, LayoutNode First, LayoutNode Second) Tree()
	{
		var root = Node();
		root.Style.SetDimension( Dimension.Width, SizeLength.Points( 800 ) );
		root.Style.SetDimension( Dimension.Height, SizeLength.Points( 100 ) );
		root.Style.SetPadding( Edge.All, StyleLength.Points( 3 ) );

		var group = Node();
		group.Style.FlexDirection = FlexDirection.Column;
		group.Style.SetPadding( Edge.All, StyleLength.Points( 2 ) );

		var first = Label( "first label" );
		var second = Label( "second" );
		group.InsertChild( first, 0 );
		group.InsertChild( second, 1 );
		root.InsertChild( group, 0 );

		return (root, group, first, second);
	}

	private static Dictionary<LayoutNode, int> CountVisits( Action layout )
	{
		var visits = new Dictionary<LayoutNode, int>();
		FlexLayout.Trace = ( node, w, wm, h, hm, performLayout, depth ) => visits[node] = visits.GetValueOrDefault( node ) + 1;

		try
		{
			layout();
		}
		finally
		{
			FlexLayout.Trace = null;
		}

		return visits;
	}

	private static int Visits( Dictionary<LayoutNode, int> visits, LayoutNode node ) => visits.GetValueOrDefault( node );

	[TestMethod]
	public void AnAnswerServesEveryLimitItFits()
	{
		var (root, group, first, second) = Tree();
		FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR );

		var width = group.Layout.GetDimension( Dimension.Width );
		Assert.AreEqual( 8f * "first label".Length + 4, width, 0.001f );

		//The root is resized twice; the group's content still fits, so nothing below the root is visited again.
		root.Style.SetDimension( Dimension.Width, SizeLength.Points( 700 ) );
		var visits = CountVisits( () => FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR ) );
		Assert.AreEqual( 1, Visits( visits, root ), "the root changed and is laid out again" );
		Assert.AreEqual( 0, Visits( visits, group ), "the group's answer under 800 fits 700" );
		Assert.AreEqual( 0, Visits( visits, first ) + Visits( visits, second ) );

		root.Style.SetDimension( Dimension.Width, SizeLength.Points( 600 ) );
		visits = CountVisits( () => FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR ) );
		Assert.AreEqual( 0, Visits( visits, group ) + Visits( visits, first ) + Visits( visits, second ) );

		Assert.AreEqual( width, group.Layout.GetDimension( Dimension.Width ), 0.001f );
		Assert.AreEqual( 3f, group.Layout.Position( PhysicalEdge.Left ), 0.001f );
	}

	[TestMethod]
	public void AnAnswerThatNoLongerFitsIsRecomputed()
	{
		var (root, group, first, _) = Tree();
		FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR );

		//Narrower than the longer label: the label wraps and the group must be measured again.
		root.Style.SetDimension( Dimension.Width, SizeLength.Points( 60 ) );
		var visits = CountVisits( () => FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR ) );
		Assert.IsTrue( Visits( visits, group ) >= 1 );
		Assert.IsTrue( first.Layout.GetDimension( Dimension.Height ) > 16f, "the label wrapped" );
		Assert.IsTrue( group.Layout.GetDimension( Dimension.Width ) <= 60f - 6f + 0.001f );
	}

	[TestMethod]
	public void APercentageBelowKeepsTheExactKey()
	{
		var (root, group, first, _) = Tree();
		first.Style.SetMargin( Edge.Left, StyleLength.Percent( 10 ) );
		Assert.IsTrue( root.SubtreeUsesPercent && root.SubtreeBlocksReuse );

		FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR );
		var margin = first.Layout.Margin( PhysicalEdge.Left );
		Assert.IsTrue( margin > 0 );

		root.Style.SetDimension( Dimension.Width, SizeLength.Points( 700 ) );
		var visits = CountVisits( () => FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR ) );
		Assert.IsTrue( Visits( visits, group ) >= 1, "a percentage below resolves against the new room" );
		Assert.IsTrue( first.Layout.Margin( PhysicalEdge.Left ) < margin );
	}

	[TestMethod]
	public void WrappingBelowKeepsTheExactKey()
	{
		var (root, group, _, _) = Tree();
		group.Style.FlexWrap = Wrap.Wrap;
		Assert.IsTrue( root.SubtreeBlocksReuse && !root.SubtreeUsesPercent );

		FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR );

		root.Style.SetDimension( Dimension.Width, SizeLength.Points( 700 ) );
		var visits = CountVisits( () => FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR ) );
		Assert.IsTrue( Visits( visits, group ) >= 1, "a wrapping container is asked again under new room" );
	}

	[TestMethod]
	public void TraitsFollowTheTree()
	{
		var root = Node();
		var child = Node();
		var grandchild = Node();
		root.InsertChild( child, 0 );
		child.InsertChild( grandchild, 0 );

		Assert.IsFalse( root.SubtreeUsesPercent || root.SubtreeBlocksReuse );

		grandchild.Style.SetDimension( Dimension.Width, SizeLength.Percent( 50 ) );
		Assert.IsTrue( root.SubtreeUsesPercent && child.SubtreeUsesPercent && grandchild.SubtreeUsesPercent );

		root.RemoveChild( child );
		Assert.IsFalse( root.SubtreeUsesPercent );
		Assert.IsTrue( child.SubtreeUsesPercent );

		root.InsertChild( child, 0 );
		Assert.IsTrue( root.SubtreeUsesPercent );

		grandchild.Style.SetDimension( Dimension.Width, SizeLength.Points( 50 ) );
		Assert.IsFalse( root.SubtreeUsesPercent || root.SubtreeBlocksReuse );

		grandchild.Style.AspectRatio = 2;
		Assert.IsTrue( root.SubtreeBlocksReuse && !root.SubtreeUsesPercent );

		child.RemoveAllChildren();
		Assert.IsFalse( root.SubtreeBlocksReuse );

		grandchild.Reset();
		Assert.IsFalse( grandchild.SubtreeBlocksReuse );
	}

	[TestMethod]
	public void AnOwnerChangeRemeasuresAPercentageLeaf()
	{
		var root = Node();
		root.Style.SetDimension( Dimension.Width, SizeLength.Points( 400 ) );
		root.Style.SetDimension( Dimension.Height, SizeLength.Points( 100 ) );

		var label = Label( "text" );
		label.Style.SetPadding( Edge.Left, StyleLength.Percent( 10 ) );
		root.InsertChild( label, 0 );

		FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR );
		Assert.AreEqual( 40f, label.Layout.Padding( PhysicalEdge.Left ), 0.001f );

		root.Style.SetDimension( Dimension.Width, SizeLength.Points( 200 ) );
		FlexLayout.Calculate( root, float.NaN, float.NaN, Direction.LTR );
		Assert.AreEqual( 20f, label.Layout.Padding( PhysicalEdge.Left ), 0.001f );
		Assert.AreEqual( 8f * 4 + 20f, label.Layout.GetDimension( Dimension.Width ), 0.001f );
	}
}
