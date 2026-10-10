using System.Runtime.CompilerServices;

namespace Rinten.Layout;

//Absolute child positioning, ported from Yoga's AbsoluteLayout.cpp (grid branches omitted)
internal static partial class FlexLayout
{
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static void SetFlexStartLayoutPosition( LayoutNode parent, LayoutNode child, Direction direction,
		FlexDirection axis, float containingBlockWidth )
	{
		var position = child.Style.ComputeFlexStartMargin( axis, direction, containingBlockWidth )
			+ parent.Layout.Border( Axis.FlexStartEdge( axis ) )
			+ parent.Layout.Padding( Axis.FlexStartEdge( axis ) );

		child.Layout.SetPosition( Axis.FlexStartEdge( axis ), position );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static void SetFlexEndLayoutPosition( LayoutNode parent, LayoutNode child, Direction direction,
		FlexDirection axis, float containingBlockWidth )
	{
		var flexEndPosition = parent.Layout.Border( Axis.FlexEndEdge( axis ) )
			+ child.Style.ComputeFlexEndMargin( axis, direction, containingBlockWidth )
			+ parent.Layout.Padding( Axis.FlexEndEdge( axis ) );

		child.Layout.SetPosition( Axis.FlexStartEdge( axis ),
			OppositeEdge( flexEndPosition, axis, parent, child ) );
	}

	//The box an absolute child is placed against has been laid out by now, and its laid-out size
	//is what it is placed against: a measurement made since could have left another number behind.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static float OppositeEdge( float position, FlexDirection axis, LayoutNode box, LayoutNode child )
		=> box.Layout.GetDimension( Axis.DimensionOf( axis ) ) - child.Layout.MeasuredDimension( Axis.DimensionOf( axis ) ) - position;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static void SetTrailingPosition( LayoutNode box, LayoutNode child, FlexDirection axis )
	{
		child.Layout.SetPosition( Axis.FlexEndEdge( axis ), OppositeEdge( child.Layout.Position( Axis.FlexStartEdge( axis ) ), axis, box, child ) );
	}

	private static void SetCenterLayoutPosition( LayoutNode parent, LayoutNode child, Direction direction,
		FlexDirection axis, float containingBlockWidth )
	{
		var parentContentBoxSize = parent.Layout.GetDimension( Axis.DimensionOf( axis ) )
			- parent.Layout.Border( Axis.FlexStartEdge( axis ) )
			- parent.Layout.Border( Axis.FlexEndEdge( axis ) )
			- parent.Layout.Padding( Axis.FlexStartEdge( axis ) )
			- parent.Layout.Padding( Axis.FlexEndEdge( axis ) );

		var childOuterSize = child.Layout.MeasuredDimension( Axis.DimensionOf( axis ) )
			+ child.Style.ComputeMarginForAxis( axis, containingBlockWidth );

		var position = ( ( parentContentBoxSize - childOuterSize ) / 2.0f )
			+ parent.Layout.Border( Axis.FlexStartEdge( axis ) )
			+ child.Style.ComputeFlexStartMargin( axis, direction, containingBlockWidth )
			+ parent.Layout.Padding( Axis.FlexStartEdge( axis ) );

		child.Layout.SetPosition( Axis.FlexStartEdge( axis ), position );
	}

	private static void JustifyAbsoluteChild( LayoutNode parent, LayoutNode child, Direction direction,
		FlexDirection mainAxis, float containingBlockWidth )
	{
		switch ( parent.Style.JustifyContent )
		{
			case Justify.Start:
			case Justify.Stretch:
			case Justify.FlexStart:
			case Justify.SpaceBetween:
				SetFlexStartLayoutPosition( parent, child, direction, mainAxis, containingBlockWidth );
				break;

			case Justify.End:
			case Justify.FlexEnd:
				SetFlexEndLayoutPosition( parent, child, direction, mainAxis, containingBlockWidth );
				break;

			case Justify.Center:
			case Justify.SpaceAround:
			case Justify.SpaceEvenly:
				SetCenterLayoutPosition( parent, child, direction, mainAxis, containingBlockWidth );
				break;
		}
	}

	private static void AlignAbsoluteChild( LayoutNode parent, LayoutNode child, Direction direction,
		FlexDirection crossAxis, float containingBlockWidth )
	{
		var itemAlign = Axis.ResolveChildAlignment( parent, child );

		//A reversed wrap turns the cross axis over, so the two ends swap and anything that was not centred lands at the far end.
		if ( parent.Style.FlexWrap == Wrap.WrapReverse )
		{
			if ( itemAlign == Align.FlexEnd ) itemAlign = Align.FlexStart;
			else if ( itemAlign != Align.Center ) itemAlign = Align.FlexEnd;
		}

		switch ( itemAlign )
		{
			case Align.Start:
			case Align.Auto:
			case Align.FlexStart:
			case Align.Baseline:
			case Align.SpaceAround:
			case Align.SpaceBetween:
			case Align.Stretch:
			case Align.SpaceEvenly:
				SetFlexStartLayoutPosition( parent, child, direction, crossAxis, containingBlockWidth );
				break;

			case Align.End:
			case Align.FlexEnd:
				SetFlexEndLayoutPosition( parent, child, direction, crossAxis, containingBlockWidth );
				break;

			case Align.Center:
				SetCenterLayoutPosition( parent, child, direction, crossAxis, containingBlockWidth );
				break;
		}
	}

	//Places an absolute child on one axis, writing to the flex start edge; inline start inset wins
	private static void PositionAbsoluteChild( LayoutNode containingNode, LayoutNode parent, LayoutNode child,
		Direction direction, FlexDirection axis, bool isMainAxis, float containingBlockWidth, float containingBlockHeight )
	{
		var isAxisRow = Axis.IsRow( axis );
		var containingBlockSize = isAxisRow ? containingBlockWidth : containingBlockHeight;

		if ( child.Style.IsInlineStartPositionDefined( axis, direction ) && !child.Style.IsInlineStartPositionAuto( axis, direction ) )
		{
			var relativeToInlineStart = child.Style.ComputeInlineStartPosition( axis, direction, containingBlockSize )
				+ containingNode.Style.ComputeInlineStartBorder( axis, direction )
				+ child.Style.ComputeInlineStartMargin( axis, direction, containingBlockSize );

			var relativeToFlexStart = Axis.InlineStartEdge( axis, direction ) != Axis.FlexStartEdge( axis )
				? OppositeEdge( relativeToInlineStart, axis, containingNode, child )
				: relativeToInlineStart;

			child.Layout.SetPosition( Axis.FlexStartEdge( axis ), relativeToFlexStart );
		}
		else if ( child.Style.IsInlineEndPositionDefined( axis, direction ) && !child.Style.IsInlineEndPositionAuto( axis, direction ) )
		{
			var relativeToInlineStart = containingNode.Layout.GetDimension( Axis.DimensionOf( axis ) )
				- child.Layout.MeasuredDimension( Axis.DimensionOf( axis ) )
				- containingNode.Style.ComputeInlineEndBorder( axis, direction )
				- child.Style.ComputeInlineEndMargin( axis, direction, containingBlockSize )
				- child.Style.ComputeInlineEndPosition( axis, direction, containingBlockSize );

			var relativeToFlexStart = Axis.InlineStartEdge( axis, direction ) != Axis.FlexStartEdge( axis )
				? OppositeEdge( relativeToInlineStart, axis, containingNode, child )
				: relativeToInlineStart;

			child.Layout.SetPosition( Axis.FlexStartEdge( axis ), relativeToFlexStart );
		}
		else if ( parent.Style.Display is Display.Block or Display.Grid )
		{
			//A block or grid owner wrote the static position - where the flow would have put the child, or the start
			//of the grid's content box - as it laid out; with no inset on this axis that is where the child stays.
		}
		else if ( isMainAxis )
		{
			JustifyAbsoluteChild( parent, child, direction, axis, containingBlockWidth );
		}
		else
		{
			AlignAbsoluteChild( parent, child, direction, axis, containingBlockWidth );
		}
	}

	private static void LayoutAbsoluteChild( LayoutNode containingNode, LayoutNode node, LayoutNode child,
		float containingBlockWidth, float containingBlockHeight, SizingMode widthMode, Direction direction,
		int depth, uint generationCount )
	{
		var mainAxis = Axis.Resolve( node.Style.FlexDirection, direction );
		var crossAxis = Axis.ResolveCross( mainAxis, direction );
		var isMainAxisRow = Axis.IsRow( mainAxis );

		var childWidth = Num.Undefined;
		var childHeight = Num.Undefined;
		var childWidthSizingMode = SizingMode.MaxContent;
		var childHeightSizingMode = SizingMode.MaxContent;

		var marginRow = child.Style.ComputeMarginForAxis( FlexDirection.Row, containingBlockWidth );
		var marginColumn = child.Style.ComputeMarginForAxis( FlexDirection.Column, containingBlockWidth );

		if ( child.HasDefiniteLength( Dimension.Width, containingBlockWidth ) )
		{
			childWidth = child.GetResolvedDimension( direction, Dimension.Width, containingBlockWidth, containingBlockWidth )
				+ marginRow;
		}
		else if ( child.Style.IsFlexStartPositionDefined( FlexDirection.Row, direction )
			&& child.Style.IsFlexEndPositionDefined( FlexDirection.Row, direction )
			&& !child.Style.IsFlexStartPositionAuto( FlexDirection.Row, direction )
			&& !child.Style.IsFlexEndPositionAuto( FlexDirection.Row, direction ) )
		{
			//No width of its own, but pinned at both ends: the gap between them is the width.
			childWidth = containingNode.Layout.GetDimension( Dimension.Width )
				- ( containingNode.Style.ComputeFlexStartBorder( FlexDirection.Row, direction )
					+ containingNode.Style.ComputeFlexEndBorder( FlexDirection.Row, direction ) )
				- ( child.Style.ComputeFlexStartPosition( FlexDirection.Row, direction, containingBlockWidth )
					+ child.Style.ComputeFlexEndPosition( FlexDirection.Row, direction, containingBlockWidth ) );

			childWidth = Bounds.BoundAxis( child, FlexDirection.Row, direction, childWidth,
				containingBlockWidth, containingBlockWidth );
		}

		if ( child.HasDefiniteLength( Dimension.Height, containingBlockHeight ) )
		{
			childHeight = child.GetResolvedDimension( direction, Dimension.Height, containingBlockHeight, containingBlockWidth )
				+ marginColumn;
		}
		else if ( child.Style.IsFlexStartPositionDefined( FlexDirection.Column, direction )
			&& child.Style.IsFlexEndPositionDefined( FlexDirection.Column, direction )
			&& !child.Style.IsFlexStartPositionAuto( FlexDirection.Column, direction )
			&& !child.Style.IsFlexEndPositionAuto( FlexDirection.Column, direction ) )
		{
			childHeight = containingNode.Layout.GetDimension( Dimension.Height )
				- ( containingNode.Style.ComputeFlexStartBorder( FlexDirection.Column, direction )
					+ containingNode.Style.ComputeFlexEndBorder( FlexDirection.Column, direction ) )
				- ( child.Style.ComputeFlexStartPosition( FlexDirection.Column, direction, containingBlockHeight )
					+ child.Style.ComputeFlexEndPosition( FlexDirection.Column, direction, containingBlockHeight ) );

			childHeight = Bounds.BoundAxis( child, FlexDirection.Column, direction, childHeight,
				containingBlockHeight, containingBlockWidth );
		}

		//A ratio needs exactly one side settled: one to anchor it, one to follow. Two settled sides, or none, and it has nothing to say.
		if ( Num.IsUndefined( childWidth ) ^ Num.IsUndefined( childHeight ) )
		{
			if ( Num.IsDefined( child.Style.AspectRatio ) )
			{
				if ( Num.IsUndefined( childWidth ) )
					childWidth = marginRow + ( ( childHeight - marginColumn ) * child.Style.AspectRatio );
				else
					childHeight = marginColumn + ( ( childWidth - marginRow ) / child.Style.AspectRatio );
			}
		}

		//Still short of a side: measure the content to find it.
		if ( Num.IsUndefined( childWidth ) || Num.IsUndefined( childHeight ) )
		{
			childWidthSizingMode = Num.IsUndefined( childWidth ) ? SizingMode.MaxContent : SizingMode.StretchFit;
			childHeightSizingMode = Num.IsUndefined( childHeight ) ? SizingMode.MaxContent : SizingMode.StretchFit;

			//Where the owner has a width, hold the child to it so text inside wraps to the owner rather than running on. Browsers do the same.
			if ( !isMainAxisRow && Num.IsUndefined( childWidth ) && widthMode != SizingMode.MaxContent
				&& Num.IsDefined( containingBlockWidth ) && containingBlockWidth > 0 )
			{
				childWidth = containingBlockWidth;
				childWidthSizingMode = SizingMode.FitContent;
			}

			CalculateLayoutInternal( child, childWidth, childHeight, direction,
				childWidthSizingMode, childHeightSizingMode,
				containingBlockWidth, containingBlockHeight, false, depth, generationCount );

			childWidth = child.Layout.MeasuredDimension( Dimension.Width )
				+ child.Style.ComputeMarginForAxis( FlexDirection.Row, containingBlockWidth );

			childHeight = child.Layout.MeasuredDimension( Dimension.Height )
				+ child.Style.ComputeMarginForAxis( FlexDirection.Column, containingBlockWidth );
		}

		CalculateLayoutInternal( child, childWidth, childHeight, direction,
			SizingMode.StretchFit, SizingMode.StretchFit,
			containingBlockWidth, containingBlockHeight, true, depth, generationCount );

		PositionAbsoluteChild( containingNode, node, child, direction, mainAxis, true, containingBlockWidth, containingBlockHeight );
		PositionAbsoluteChild( containingNode, node, child, direction, crossAxis, false, containingBlockWidth, containingBlockHeight );
	}

	//Lays out every absolute node under a containing block, including ones several levels down whose parents are static.
	private static bool LayoutAbsoluteDescendants( LayoutNode containingNode, LayoutNode currentNode,
		SizingMode widthSizingMode, Direction currentNodeDirection, int currentDepth, uint generationCount,
		float currentNodeLeftOffsetFromContainingBlock, float currentNodeTopOffsetFromContainingBlock,
		float containingNodeAvailableInnerWidth, float containingNodeAvailableInnerHeight )
	{
		var hasNewLayout = false;

		foreach ( var child in currentNode.LayoutChildren )
		{
			if ( child.Style.Display == Display.None ) continue;

			if ( child.Style.PositionType == PositionType.Absolute )
			{
				hasNewLayout = PlaceOutOfFlowChild( containingNode, currentNode, child, widthSizingMode, currentNodeDirection,
					currentDepth, generationCount, currentNodeLeftOffsetFromContainingBlock, currentNodeTopOffsetFromContainingBlock ) || hasNewLayout;
			}
			else if ( child.Style.PositionType == PositionType.Static && !child.AlwaysFormsContainingBlock )
			{
				var childDirection = child.ResolveDirection( currentNodeDirection );

				//Everything under the containing block that is not absolute already has its left and top by now.
				var childLeftOffset = currentNodeLeftOffsetFromContainingBlock + child.Layout.Position( PhysicalEdge.Left );
				var childTopOffset = currentNodeTopOffsetFromContainingBlock + child.Layout.Position( PhysicalEdge.Top );

				hasNewLayout = LayoutAbsoluteDescendants( containingNode, child, widthSizingMode, childDirection,
					currentDepth + 1, generationCount, childLeftOffset, childTopOffset,
					containingNodeAvailableInnerWidth, containingNodeAvailableInnerHeight ) || hasNewLayout;

				if ( hasNewLayout ) child.HasNewLayout = true;
			}
		}

		return hasNewLayout;
	}

	//Places fixed nodes against the root, walking only branches that hold one; returns whether any moved
	private static bool LayoutFixedDescendants( LayoutNode root, LayoutNode currentNode, SizingMode widthSizingMode,
		Direction currentNodeDirection, int currentDepth, uint generationCount, float leftOffset, float topOffset )
	{
		var hasNewLayout = false;

		foreach ( var child in currentNode.LayoutChildren )
		{
			if ( child.Style.Display == Display.None || !child.SubtreeHasFixed ) continue;

			if ( child.Style.PositionType == PositionType.Fixed )
			{
				hasNewLayout = PlaceOutOfFlowChild( root, currentNode, child, widthSizingMode, currentNodeDirection,
					currentDepth, generationCount, leftOffset, topOffset ) || hasNewLayout;
			}

			//A fixed node may hold fixed nodes of its own; they are placed once it is.
			hasNewLayout = LayoutFixedDescendants( root, child, widthSizingMode, child.ResolveDirection( currentNodeDirection ),
				currentDepth + 1, generationCount,
				leftOffset + child.Layout.Position( PhysicalEdge.Left ), topOffset + child.Layout.Position( PhysicalEdge.Top ) ) || hasNewLayout;

			if ( hasNewLayout ) child.HasNewLayout = true;
		}

		return hasNewLayout;
	}

	//Places one out-of-flow child against its containing block, and leaves its
	//position relative to its parent. Returns whether the child was laid out afresh.
	private static bool PlaceOutOfFlowChild( LayoutNode containingNode, LayoutNode currentNode, LayoutNode child,
		SizingMode widthSizingMode, Direction currentNodeDirection, int currentDepth, uint generationCount,
		float currentNodeLeftOffsetFromContainingBlock, float currentNodeTopOffsetFromContainingBlock )
	{
		var containingBlockWidth = containingNode.Layout.GetDimension( Dimension.Width )
			- containingNode.Style.ComputeBorderForAxis( FlexDirection.Row );

		var containingBlockHeight = containingNode.Layout.GetDimension( Dimension.Height )
			- containingNode.Style.ComputeBorderForAxis( FlexDirection.Column );

		LayoutAbsoluteChild( containingNode, currentNode, child, containingBlockWidth, containingBlockHeight,
			widthSizingMode, currentNodeDirection, currentDepth, generationCount );

		//The child is positioned on its parent's flex start edge, but where it had insets that position is relative to the containing block
		//instead. The two nodes may not even share a main axis, so the far edges are written first and the offset is taken off afterwards.
		var parentMainAxis = Axis.Resolve( currentNode.Style.FlexDirection, currentNodeDirection );
		var parentCrossAxis = Axis.ResolveCross( parentMainAxis, currentNodeDirection );

		if ( Bounds.NeedsTrailingPosition( parentMainAxis ) )
		{
			var mainInsetsDefined = Axis.IsRow( parentMainAxis )
				? child.Style.HorizontalInsetsDefined
				: child.Style.VerticalInsetsDefined;

			SetTrailingPosition( mainInsetsDefined ? containingNode : currentNode, child, parentMainAxis );
		}

		if ( Bounds.NeedsTrailingPosition( parentCrossAxis ) )
		{
			var crossInsetsDefined = Axis.IsRow( parentCrossAxis )
				? child.Style.HorizontalInsetsDefined
				: child.Style.VerticalInsetsDefined;

			SetTrailingPosition( crossInsetsDefined ? containingNode : currentNode, child, parentCrossAxis );
		}

		var childLeftPosition = child.Layout.Position( PhysicalEdge.Left );
		var childTopPosition = child.Layout.Position( PhysicalEdge.Top );

		var childLeftOffsetFromParent = child.Style.HorizontalInsetsDefined
			? childLeftPosition - currentNodeLeftOffsetFromContainingBlock
			: childLeftPosition;

		var childTopOffsetFromParent = child.Style.VerticalInsetsDefined
			? childTopPosition - currentNodeTopOffsetFromContainingBlock
			: childTopPosition;

		child.Layout.SetPosition( PhysicalEdge.Left, childLeftOffsetFromParent );
		child.Layout.SetPosition( PhysicalEdge.Top, childTopOffsetFromParent );

		return child.HasNewLayout;
	}
}
