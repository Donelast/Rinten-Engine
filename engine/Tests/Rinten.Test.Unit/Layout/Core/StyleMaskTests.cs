using System;
using Rinten.Layout;

namespace LayoutTests.Core;

//The style's bitmasks of set entries must always agree with the entries, because
//the sums and the edge resolution trust the mask instead of reading the table.
[TestClass]
public class StyleMaskTests
{
	[TestMethod]
	public void NothingSetSumsToZero()
	{
		var style = new LayoutStyle();
		Assert.IsFalse( style.HasMargin );
		Assert.IsFalse( style.HasPaddingOrBorder );
		Assert.IsFalse( style.HasMinOrMaxDimension( Dimension.Width ) );
		Assert.AreEqual( 0f, style.ComputeMarginForAxis( FlexDirection.Row, 100 ) );
		Assert.AreEqual( 0f, style.ComputePaddingAndBorderForDimension( Direction.LTR, Dimension.Height, 100 ) );
		Assert.IsFalse( style.FlexStartMarginIsAuto( FlexDirection.Row, Direction.LTR ) );
		Assert.IsTrue( style.ComputeMargin( PhysicalEdge.Left, Direction.LTR ).IsUndefined );
	}

	[TestMethod]
	public void EverySettableEdgeRaisesAndLowersItsBit()
	{
		var style = new LayoutStyle();
		foreach ( var edge in Enum.GetValues<Edge>() )
		{
			style.SetMargin( edge, StyleLength.Points( 3 ) );
			Assert.IsTrue( style.HasMargin, edge.ToString() );
			style.SetMargin( edge, StyleLength.Undefined );
			Assert.IsFalse( style.HasMargin, edge.ToString() );

			style.SetPadding( edge, StyleLength.Points( 3 ) );
			Assert.IsTrue( style.HasPaddingOrBorder, edge.ToString() );
			style.SetPadding( edge, StyleLength.Undefined );
			Assert.IsFalse( style.HasPaddingOrBorder, edge.ToString() );

			style.SetBorder( edge, StyleLength.Points( 3 ) );
			Assert.IsTrue( style.HasPaddingOrBorder, edge.ToString() );
			style.SetBorder( edge, StyleLength.Undefined );
			Assert.IsFalse( style.HasPaddingOrBorder, edge.ToString() );
		}
	}

	[TestMethod]
	public void ShorthandsResolveThroughTheMask()
	{
		var style = new LayoutStyle();
		style.SetMargin( Edge.All, StyleLength.Points( 4 ) );
		style.SetMargin( Edge.Horizontal, StyleLength.Points( 6 ) );
		style.SetMargin( Edge.Start, StyleLength.Auto );

		Assert.AreEqual( 4f, style.ComputeMargin( PhysicalEdge.Top, Direction.LTR ).Value );
		Assert.AreEqual( 6f, style.ComputeMargin( PhysicalEdge.Right, Direction.LTR ).Value );
		Assert.IsTrue( style.ComputeMargin( PhysicalEdge.Left, Direction.LTR ).IsAuto );
		Assert.IsTrue( style.ComputeMargin( PhysicalEdge.Right, Direction.RTL ).IsAuto );
		Assert.IsTrue( style.FlexStartMarginIsAuto( FlexDirection.Row, Direction.LTR ) );

		//Unsetting start uncovers horizontal again; unsetting that uncovers all.
		style.SetMargin( Edge.Start, StyleLength.Undefined );
		Assert.AreEqual( 6f, style.ComputeMargin( PhysicalEdge.Left, Direction.LTR ).Value );
		style.SetMargin( Edge.Horizontal, StyleLength.Undefined );
		Assert.AreEqual( 4f, style.ComputeMargin( PhysicalEdge.Left, Direction.LTR ).Value );
	}

	[TestMethod]
	public void MinAndMaxBitsArePerAxis()
	{
		var style = new LayoutStyle();
		style.SetMinDimension( Dimension.Width, SizeLength.Points( 10 ) );
		Assert.IsTrue( style.HasMinOrMaxDimension( Dimension.Width ) );
		Assert.IsFalse( style.HasMinOrMaxDimension( Dimension.Height ) );

		style.SetMaxDimension( Dimension.Height, SizeLength.Percent( 50 ) );
		Assert.IsTrue( style.HasMinOrMaxDimension( Dimension.Height ) );

		style.SetMinDimension( Dimension.Width, SizeLength.Undefined );
		Assert.IsFalse( style.HasMinOrMaxDimension( Dimension.Width ) );
		style.SetMaxDimension( Dimension.Height, SizeLength.Undefined );
		Assert.IsFalse( style.HasMinOrMaxDimension( Dimension.Height ) );
	}

	[TestMethod]
	public void CopyCarriesTheMasks()
	{
		var source = new LayoutStyle();
		source.SetMargin( Edge.Left, StyleLength.Points( 5 ) );
		source.SetBorder( Edge.All, StyleLength.Points( 1 ) );
		source.SetMaxDimension( Dimension.Width, SizeLength.Points( 90 ) );

		var copy = new LayoutStyle();
		copy.CopyFrom( source );
		Assert.IsTrue( copy.SameAs( source ) );
		Assert.IsTrue( copy.HasMargin );
		Assert.IsTrue( copy.HasPaddingOrBorder );
		Assert.IsTrue( copy.HasMinOrMaxDimension( Dimension.Width ) );
		Assert.AreEqual( 5f, copy.ComputeMarginForAxis( FlexDirection.Row, 100 ) );
		Assert.AreEqual( 2f, copy.ComputeBorderForAxis( FlexDirection.Column ) );
	}
}
