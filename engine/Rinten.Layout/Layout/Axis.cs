using System.Runtime.CompilerServices;

namespace Rinten.Layout;

//Axis direction and start edges (Yoga FlexDirection.h); flex start and inline start differ
internal static class Axis
{
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool IsRow( FlexDirection direction )
		=> direction == FlexDirection.Row || direction == FlexDirection.RowReverse;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool IsColumn( FlexDirection direction )
		=> direction == FlexDirection.Column || direction == FlexDirection.ColumnReverse;

	/// <summary>A row turns around under a right-to-left direction; a column never does.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static FlexDirection Resolve( FlexDirection direction, Direction writing )
	{
		if ( writing == Direction.RTL )
		{
			if ( direction == FlexDirection.Row ) return FlexDirection.RowReverse;
			if ( direction == FlexDirection.RowReverse ) return FlexDirection.Row;
		}

		return direction;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static FlexDirection ResolveCross( FlexDirection direction, Direction writing )
		=> IsColumn( direction ) ? Resolve( FlexDirection.Row, writing ) : FlexDirection.Column;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static PhysicalEdge FlexStartEdge( FlexDirection direction ) => direction switch
	{
		FlexDirection.Column => PhysicalEdge.Top,
		FlexDirection.ColumnReverse => PhysicalEdge.Bottom,
		FlexDirection.Row => PhysicalEdge.Left,
		_ => PhysicalEdge.Right,
	};

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static PhysicalEdge FlexEndEdge( FlexDirection direction ) => direction switch
	{
		FlexDirection.Column => PhysicalEdge.Bottom,
		FlexDirection.ColumnReverse => PhysicalEdge.Top,
		FlexDirection.Row => PhysicalEdge.Right,
		_ => PhysicalEdge.Left,
	};

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static PhysicalEdge InlineStartEdge( FlexDirection direction, Direction writing )
	{
		if ( IsRow( direction ) ) return writing == Direction.RTL ? PhysicalEdge.Right : PhysicalEdge.Left;

		return PhysicalEdge.Top;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static PhysicalEdge InlineEndEdge( FlexDirection direction, Direction writing )
	{
		if ( IsRow( direction ) ) return writing == Direction.RTL ? PhysicalEdge.Left : PhysicalEdge.Right;

		return PhysicalEdge.Bottom;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Dimension DimensionOf( FlexDirection direction )
		=> IsRow( direction ) ? Dimension.Width : Dimension.Height;

	//-- sizing modes ---------------------------------------------------------------

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static MeasureMode ToMeasureMode( SizingMode mode ) => mode switch
	{
		SizingMode.StretchFit => MeasureMode.Exactly,
		SizingMode.MaxContent => MeasureMode.Undefined,
		_ => MeasureMode.AtMost,
	};

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static SizingMode ToSizingMode( MeasureMode mode ) => mode switch
	{
		MeasureMode.Exactly => SizingMode.StretchFit,
		MeasureMode.Undefined => SizingMode.MaxContent,
		_ => SizingMode.FitContent,
	};

	//-- alignment ------------------------------------------------------------------

	//A baseline alignment in a column has nothing to align to - the baselines all run the same way as the axis - so it falls back to the start.
	/// <summary>What a child is aligned by: its own <c>align-self</c>, or the owner's <c>align-items</c> where it has none.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Align ResolveChildAlignment( LayoutNode node, LayoutNode child )
	{
		var align = child.Style.AlignSelf == Align.Auto ? node.Style.AlignItems : child.Style.AlignSelf;

		if ( node.Style.Display == Display.Flex && align == Align.Baseline && IsColumn( node.Style.FlexDirection ) )
			return Align.FlexStart;

		return align;
	}

	/// <summary>Whether a child asks for baseline alignment in a column, where there is no baseline to line up and the items line their leading edges up as a group instead.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool IsColumnBaseline( LayoutNode node, LayoutNode child )
		=> node.Style.Display == Display.Flex && IsColumn( node.Style.FlexDirection )
			&& ( child.Style.AlignSelf == Align.Auto ? node.Style.AlignItems : child.Style.AlignSelf ) == Align.Baseline;

	/// <summary>What a distribution falls back to when there is not enough room for it. https://www.w3.org/TR/css-align-3/#distribution-values</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Align Fallback( Align align ) => align switch
	{
		Align.SpaceBetween or Align.Stretch or Align.SpaceAround or Align.SpaceEvenly => Align.FlexStart,
		_ => align,
	};

	/// <summary>Overflow fallback for justify: space-between packs from flex start, others from writing start</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Justify Fallback( Justify align, FlexDirection mainAxis, Direction direction ) => align switch
	{
		Justify.SpaceBetween => Justify.FlexStart,
		Justify.SpaceAround or Justify.SpaceEvenly => FlexStartEdge( mainAxis ) == StartEdge( mainAxis, direction ) ? Justify.FlexStart : Justify.FlexEnd,
		_ => align,
	};

	//The physical edge the writing direction starts from along an axis.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static PhysicalEdge StartEdge( FlexDirection axis, Direction direction )
		=> IsRow( axis ) ? ( direction == Direction.RTL ? PhysicalEdge.Right : PhysicalEdge.Left ) : PhysicalEdge.Top;
}
