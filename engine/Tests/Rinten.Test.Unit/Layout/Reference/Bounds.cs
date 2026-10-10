//Frozen phase-7 copy of Rinten.Layout (Yoga port, MIT) for differential tests; do not edit
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
namespace Rinten.Layout.Reference;

//Clamping a size between what an author allowed, and the trailing edge a reversed axis needs. Ported from Yoga's BoundAxis.h and TrailingPosition.h .
internal static class Bounds
{
	public static float PaddingAndBorderForAxis( LayoutNode node, FlexDirection axis, Direction direction, float widthSize )
	{
		//Most boxes have neither, and this runs several times per node per pass.
		var style = node.Style;
		if ( !style.HasPaddingOrBorder ) return 0.0f;

		return style.ComputeInlineStartPaddingAndBorder( axis, direction, widthSize )
			+ style.ComputeInlineEndPaddingAndBorder( axis, direction, widthSize );
	}

	//The value held inside the node's own minimum and maximum. A minimum or maximum that is undefined does not clamp - which is why
	//the comparison is against nought rather than a plain null check: an undefined is a NaN and every comparison with it is false.
	public static float WithinMinAndMax( LayoutNode node, Direction direction, FlexDirection axis,
		float value, float axisSize, float widthSize )
	{
		//With no minimum and no maximum on the axis both resolve to undefined and neither
		//clamp below fires, so the value goes back untouched without resolving anything.
		if ( !node.Style.HasMinOrMaxDimension( Axis.DimensionOf( axis ) ) ) return value;

		var min = Num.Undefined;
		var max = Num.Undefined;

		if ( Axis.IsColumn( axis ) )
		{
			min = node.Style.ResolvedMinDimension( direction, Dimension.Height, axisSize, widthSize );
			max = node.Style.ResolvedMaxDimension( direction, Dimension.Height, axisSize, widthSize );
		}
		else if ( Axis.IsRow( axis ) )
		{
			min = node.Style.ResolvedMinDimension( direction, Dimension.Width, axisSize, widthSize );
			max = node.Style.ResolvedMaxDimension( direction, Dimension.Width, axisSize, widthSize );
		}

		//Both clamps, in this order, so a minimum above the maximum wins as CSS says it should.
		//An early return after the maximum left such a value oscillating between the two.
		if ( max >= 0 && value > max ) value = max;
		if ( min >= 0 && value < min ) value = min;

		return value;
	}

	//The same, and never below the room the padding and border take.
	public static float BoundAxis( LayoutNode node, FlexDirection axis, Direction direction,
		float value, float axisSize, float widthSize )
		=> Num.MaxOrDefined(
			WithinMinAndMax( node, direction, axis, value, axisSize, widthSize ),
			PaddingAndBorderForAxis( node, axis, direction, widthSize ) );

	//Given where one edge sits, where the opposite one does. Both sizes have to be settled by the time this is asked.
	public static float PositionOfOppositeEdge( float position, FlexDirection axis, LayoutNode containing, LayoutNode node )
		=> containing.Layout.MeasuredDimension( Axis.DimensionOf( axis ) )
			- node.Layout.MeasuredDimension( Axis.DimensionOf( axis ) )
			- position;

	public static void SetChildTrailingPosition( LayoutNode node, LayoutNode child, FlexDirection axis )
	{
		child.Layout.SetPosition( Axis.FlexEndEdge( axis ),
			PositionOfOppositeEdge( child.Layout.Position( Axis.FlexStartEdge( axis ) ), axis, node, child ) );
	}

	//A reversed axis is laid out from its far end, so the far edge has to be written too.
	public static bool NeedsTrailingPosition( FlexDirection axis )
		=> axis == FlexDirection.RowReverse || axis == FlexDirection.ColumnReverse;
}
