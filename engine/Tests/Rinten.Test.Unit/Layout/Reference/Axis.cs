//Frozen phase-7 copy of Rinten.Layout (Yoga port, MIT) for differential tests; do not edit
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
namespace Rinten.Layout.Reference;

//Axis helpers from Yoga's FlexDirection.h; flex start (items) and inline start (margins) differ
internal static class Axis
{
	public static bool IsRow( FlexDirection direction )
		=> direction == FlexDirection.Row || direction == FlexDirection.RowReverse;

	public static bool IsColumn( FlexDirection direction )
		=> direction == FlexDirection.Column || direction == FlexDirection.ColumnReverse;

	//A row turns around under a right-to-left direction; a column never does.
	public static FlexDirection Resolve( FlexDirection direction, Direction writing )
	{
		if ( writing == Direction.RTL )
		{
			if ( direction == FlexDirection.Row ) return FlexDirection.RowReverse;
			if ( direction == FlexDirection.RowReverse ) return FlexDirection.Row;
		}

		return direction;
	}

	public static FlexDirection ResolveCross( FlexDirection direction, Direction writing )
		=> IsColumn( direction ) ? Resolve( FlexDirection.Row, writing ) : FlexDirection.Column;

	public static PhysicalEdge FlexStartEdge( FlexDirection direction ) => direction switch
	{
		FlexDirection.Column => PhysicalEdge.Top,
		FlexDirection.ColumnReverse => PhysicalEdge.Bottom,
		FlexDirection.Row => PhysicalEdge.Left,
		_ => PhysicalEdge.Right,
	};

	public static PhysicalEdge FlexEndEdge( FlexDirection direction ) => direction switch
	{
		FlexDirection.Column => PhysicalEdge.Bottom,
		FlexDirection.ColumnReverse => PhysicalEdge.Top,
		FlexDirection.Row => PhysicalEdge.Right,
		_ => PhysicalEdge.Left,
	};

	public static PhysicalEdge InlineStartEdge( FlexDirection direction, Direction writing )
	{
		if ( IsRow( direction ) ) return writing == Direction.RTL ? PhysicalEdge.Right : PhysicalEdge.Left;

		return PhysicalEdge.Top;
	}

	public static PhysicalEdge InlineEndEdge( FlexDirection direction, Direction writing )
	{
		if ( IsRow( direction ) ) return writing == Direction.RTL ? PhysicalEdge.Left : PhysicalEdge.Right;

		return PhysicalEdge.Bottom;
	}

	public static Dimension DimensionOf( FlexDirection direction )
		=> IsRow( direction ) ? Dimension.Width : Dimension.Height;

	//-- sizing modes ---------------------------------------------------------------

	public static MeasureMode ToMeasureMode( SizingMode mode ) => mode switch
	{
		SizingMode.StretchFit => MeasureMode.Exactly,
		SizingMode.MaxContent => MeasureMode.Undefined,
		_ => MeasureMode.AtMost,
	};

	public static SizingMode ToSizingMode( MeasureMode mode ) => mode switch
	{
		MeasureMode.Exactly => SizingMode.StretchFit,
		MeasureMode.Undefined => SizingMode.MaxContent,
		_ => SizingMode.FitContent,
	};

	//-- alignment ------------------------------------------------------------------

	//What a child is aligned by: its own align-self , or the owner's align-items where it has none. A baseline alignment
	//in a column has nothing to align to - the baselines all run the same way as the axis - so it falls back to the start.
	public static Align ResolveChildAlignment( LayoutNode node, LayoutNode child )
	{
		var align = child.Style.AlignSelf == Align.Auto ? node.Style.AlignItems : child.Style.AlignSelf;

		if ( node.Style.Display == Display.Flex && align == Align.Baseline && IsColumn( node.Style.FlexDirection ) )
			return Align.FlexStart;

		return align;
	}

	//Whether a child asks for baseline alignment in a column, where there is no
	//baseline to line up and the items line their leading edges up as a group instead.
	public static bool IsColumnBaseline( LayoutNode node, LayoutNode child )
		=> node.Style.Display == Display.Flex && IsColumn( node.Style.FlexDirection )
			&& ( child.Style.AlignSelf == Align.Auto ? node.Style.AlignItems : child.Style.AlignSelf ) == Align.Baseline;

	//What a distribution falls back to when there is not enough room for it. https://www.w3.org/TR/css-align-3/#distribution-values
	public static Align Fallback( Align align ) => align switch
	{
		Align.SpaceBetween or Align.Stretch or Align.SpaceAround or Align.SpaceEvenly => Align.FlexStart,
		_ => align,
	};

	//Overflow fallback: space-between packs from flex start, around/evenly from the writing start
	public static Justify Fallback( Justify align, FlexDirection mainAxis, Direction direction ) => align switch
	{
		Justify.SpaceBetween => Justify.FlexStart,
		Justify.SpaceAround or Justify.SpaceEvenly => FlexStartEdge( mainAxis ) == StartEdge( mainAxis, direction ) ? Justify.FlexStart : Justify.FlexEnd,
		_ => align,
	};

	//The physical edge the writing direction starts from along an axis.
	private static PhysicalEdge StartEdge( FlexDirection axis, Direction direction )
		=> IsRow( axis ) ? ( direction == Direction.RTL ? PhysicalEdge.Right : PhysicalEdge.Left ) : PhysicalEdge.Top;
}
