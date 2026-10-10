//Frozen phase-7 copy of Rinten.Layout (Yoga port, MIT) for differential tests; do not edit
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Rinten.Layout.Reference;

//Margins collapsing out through a block's top and bottom, cached per slot like size and baseline
internal sealed class BlockMargins
{
	public const int LayoutSlot = LayoutResults.MaxCachedMeasurements;

	public float TopPositive, TopNegative, BottomPositive, BottomNegative;

	private readonly float[] slots = new float[( LayoutResults.MaxCachedMeasurements + 1 ) * 4];

	public void Set( float topPositive, float topNegative, float bottomPositive, float bottomNegative )
	{
		TopPositive = topPositive;
		TopNegative = topNegative;
		BottomPositive = bottomPositive;
		BottomNegative = bottomNegative;
	}

	public void Save( int slot )
	{
		var i = slot * 4;
		slots[i] = TopPositive;
		slots[i + 1] = TopNegative;
		slots[i + 2] = BottomPositive;
		slots[i + 3] = BottomNegative;
	}

	public void Load( int slot )
	{
		var i = slot * 4;
		TopPositive = slots[i];
		TopNegative = slots[i + 1];
		BottomPositive = slots[i + 2];
		BottomNegative = slots[i + 3];
	}
}

//Block layout per CSS 2 (9.4.1, 10.3.3, 10.6.3, 8.3.1), including margin collapsing; not from Yoga
internal static partial class FlexLayout
{
	//The block counterpart of the eleven steps, entered from CalculateLayoutImpl once the box's
	//own margins, padding and border are known and the leaf and empty cases have been dealt with.
	private static void LayoutBlock( LayoutNode node, float availableWidth, float availableHeight, Direction direction,
		SizingMode widthSizingMode, SizingMode heightSizingMode, float ownerWidth, float ownerHeight,
		bool performLayout, int depth, uint generationCount, float marginAxisRow, float marginAxisColumn )
	{
		ref var layout = ref node.Layout;
		layout.HadOverflow = false;

		var paddingAndBorderAxisRow = Bounds.PaddingAndBorderForAxis( node, FlexDirection.Row, direction, ownerWidth );
		var paddingAndBorderAxisColumn = Bounds.PaddingAndBorderForAxis( node, FlexDirection.Column, direction, ownerWidth );

		var availableInnerWidth = CalculateAvailableInnerDimension( node, direction, Dimension.Width,
			availableWidth - marginAxisRow, paddingAndBorderAxisRow, ownerWidth, ownerWidth );

		var availableInnerHeight = CalculateAvailableInnerDimension( node, direction, Dimension.Height,
			availableHeight - marginAxisColumn, paddingAndBorderAxisColumn, ownerHeight, ownerWidth );

		var contentLeft = layout.Padding( PhysicalEdge.Left ) + layout.Border( PhysicalEdge.Left );
		var contentTop = layout.Padding( PhysicalEdge.Top ) + layout.Border( PhysicalEdge.Top );

		var children = node.LayoutChildren;

		//What a child's percentage height is a percentage of: the box's inner height where that
		//is settled, nothing where the box is still finding it from its contents (CSS Sizing 3).
		var heightReference = heightSizingMode == SizingMode.StretchFit ? availableInnerHeight : Num.Undefined;

		//-- the width ----------------------------------------------------------------------

		//A box sized by its contents is as wide as its widest child wants to be, and no wider than the room where the room is a limit:
		//shrink-to-fit, CSS 2 10.3.5. A child's want is measured with no room at all, so the answer is one cached answer whatever the room.
		float innerWidth;

		if ( widthSizingMode == SizingMode.StretchFit )
		{
			innerWidth = availableInnerWidth;
		}
		else
		{
			var preferred = 0.0f;

			foreach ( var child in children )
			{
				if ( child.Style.Display == Display.None || child.IsOutOfFlow ) continue;

				child.ProcessDimensions();
				preferred = MathF.Max( preferred, PreferredOuterWidth( child, direction, depth, generationCount ) );
			}

			innerWidth = widthSizingMode == SizingMode.FitContent && Num.IsDefined( availableInnerWidth )
				? MathF.Min( preferred, availableInnerWidth )
				: preferred;

			innerWidth = Bounds.BoundAxis( node, FlexDirection.Row, direction, innerWidth + paddingAndBorderAxisRow, ownerWidth, ownerWidth )
				- paddingAndBorderAxisRow;
		}

		//-- the flow -----------------------------------------------------------------------

		var startsFormattingContext = StartsFormattingContext( node, depth );
		var collapsesTop = !startsFormattingContext && contentTop == 0;
		var collapsesBottom = !startsFormattingContext
			&& layout.Padding( PhysicalEdge.Bottom ) + layout.Border( PhysicalEdge.Bottom ) == 0
			&& !node.ProcessedDimension( Dimension.Height ).IsPoints && !node.ProcessedDimension( Dimension.Height ).IsPercent;

		//The margins waiting at the current position to collapse with whatever comes next, and the ones that left through the top.
		var pendingPositive = 0.0f;
		var pendingNegative = 0.0f;
		var escapedTopPositive = 0.0f;
		var escapedTopNegative = 0.0f;
		var y = 0.0f;
		var anyInFlow = false;
		var widest = 0.0f;

		foreach ( var child in children )
		{
			child.ProcessDimensions();

			if ( child.Style.Display == Display.None )
			{
				if ( performLayout )
				{
					ZeroOutLayoutRecursively( child );
					child.HasNewLayout = true;
					child.SetDirty( false );
				}

				continue;
			}

			if ( child.IsOutOfFlow )
			{
				//The static position: where the flow would have put the child's top left corner had it been in
				//the flow with no margins of its own. The absolute walk keeps it where no inset says otherwise.
				if ( performLayout )
				{
					child.Layout.SetPosition( PhysicalEdge.Left, contentLeft );
					child.Layout.SetPosition( PhysicalEdge.Top, contentTop + y + ( collapsesTop && !anyInFlow ? 0.0f : pendingPositive + pendingNegative ) );
				}

				continue;
			}

			child.LineIndex = 0;

			//The width, CSS 2 10.3.3: margins, border, padding and width add up to the room, and what is automatic takes up the slack.
			//Automatic side margins with a definite width centre the box or push it to one side; with an automatic width they are nought.
			var marginLeft = child.Style.ComputeFlexStartMargin( FlexDirection.Row, direction, innerWidth );
			var marginRight = child.Style.ComputeFlexEndMargin( FlexDirection.Row, direction, innerWidth );
			var leftAuto = child.Style.FlexStartMarginIsAuto( FlexDirection.Row, direction );
			var rightAuto = child.Style.FlexEndMarginIsAuto( FlexDirection.Row, direction );
			var styleMarginRow = child.Style.ComputeMarginForAxis( FlexDirection.Row, innerWidth );

			float childWidth;

			if ( child.HasDefiniteLength( Dimension.Width, innerWidth ) )
			{
				childWidth = Bounds.BoundAxis( child, FlexDirection.Row, direction,
					child.GetResolvedDimension( direction, Dimension.Width, innerWidth, innerWidth ), innerWidth, innerWidth );

				var slack = innerWidth - childWidth - marginLeft - marginRight;

				if ( Num.IsDefined( slack ) && slack > 0 )
				{
					if ( leftAuto && rightAuto ) marginLeft += slack / 2;
					else if ( leftAuto ) marginLeft += slack;
					//Over-constrained: the far-side margin gives way, so RTL boxes shift and LTR boxes stay
					else if ( !rightAuto && direction == Direction.RTL ) marginLeft += slack;
				}
				else if ( Num.IsDefined( slack ) && direction == Direction.RTL )
				{
					//Too wide for the room: automatic margins are nought, and it is again the margin on the far side of the writing direction that gives way.
					marginLeft = innerWidth - childWidth - marginRight;
				}
			}
			else
			{
				childWidth = Num.IsDefined( innerWidth )
					? Bounds.BoundAxis( child, FlexDirection.Row, direction, innerWidth - marginLeft - marginRight, innerWidth, innerWidth )
					: Num.Undefined;
			}

			//The height: definite, or the content's.
			var childHeightAvailable = Num.Undefined;
			var childHeightSizingMode = SizingMode.MaxContent;

			if ( child.HasDefiniteLength( Dimension.Height, heightReference ) )
			{
				childHeightAvailable = child.GetResolvedDimension( direction, Dimension.Height, heightReference, innerWidth )
					+ child.Style.ComputeMarginForAxis( FlexDirection.Column, innerWidth );
				childHeightSizingMode = SizingMode.StretchFit;
			}

			var childWidthSizingMode = Num.IsDefined( childWidth ) ? SizingMode.StretchFit : SizingMode.MaxContent;

			CalculateLayoutInternal( child, Num.IsDefined( childWidth ) ? childWidth + styleMarginRow : Num.Undefined, childHeightAvailable,
				direction, childWidthSizingMode, childHeightSizingMode, innerWidth, heightReference, performLayout, depth, generationCount );

			var childHeight = child.Layout.MeasuredDimension( Dimension.Height );
			widest = MathF.Max( widest, child.Layout.MeasuredDimension( Dimension.Width ) + marginLeft + marginRight );

			//The margins meeting at the child's top: its own, and those that pass out through it.
			MarginsAtTop( child, direction, innerWidth, ref pendingPositive, ref pendingNegative );

			if ( CollapsesThrough( child ) )
			{
				//An empty box: its top and bottom margins meet each other and everything around them, and the box sits where the collapsed margin lands.
				if ( performLayout )
				{
					var relativeTop = child.RelativePosition( FlexDirection.Column, direction, availableInnerHeight );
					child.Layout.SetPosition( PhysicalEdge.Left, contentLeft + marginLeft + child.RelativePosition( FlexDirection.Row, direction, innerWidth ) );
					child.Layout.SetPosition( PhysicalEdge.Top, contentTop + y + ( collapsesTop && !anyInFlow ? 0.0f : pendingPositive + pendingNegative ) + relativeTop );
				}

				MarginsAtBottom( child, direction, innerWidth, ref pendingPositive, ref pendingNegative );
				continue;
			}

			//The first margin in the box passes out through its top where nothing holds it in;
			//the owner reads it off the box's answer, so it is left out of the height here.
			if ( collapsesTop && !anyInFlow )
			{
				escapedTopPositive = pendingPositive;
				escapedTopNegative = pendingNegative;
			}
			else
			{
				y += pendingPositive + pendingNegative;
			}

			if ( performLayout )
			{
				child.Layout.SetPosition( PhysicalEdge.Left, contentLeft + marginLeft + child.RelativePosition( FlexDirection.Row, direction, innerWidth ) );
				child.Layout.SetPosition( PhysicalEdge.Top, contentTop + y + child.RelativePosition( FlexDirection.Column, direction, availableInnerHeight ) );
			}

			y += childHeight;
			anyInFlow = true;

			pendingPositive = 0.0f;
			pendingNegative = 0.0f;
			MarginsAtBottom( child, direction, innerWidth, ref pendingPositive, ref pendingNegative );
		}

		//The last margin passes out through the bottom where nothing holds it in there either.
		//With nothing in the flow but margins, they all meet, and leave whichever way is open.
		var escapedBottomPositive = 0.0f;
		var escapedBottomNegative = 0.0f;

		if ( anyInFlow )
		{
			if ( collapsesBottom )
			{
				escapedBottomPositive = pendingPositive;
				escapedBottomNegative = pendingNegative;
			}
			else
			{
				y += pendingPositive + pendingNegative;
			}
		}
		else if ( collapsesTop || collapsesBottom )
		{
			if ( collapsesTop )
			{
				escapedTopPositive = pendingPositive;
				escapedTopNegative = pendingNegative;
			}

			if ( collapsesBottom )
			{
				escapedBottomPositive = pendingPositive;
				escapedBottomNegative = pendingNegative;
			}
		}
		else
		{
			y += pendingPositive + pendingNegative;
		}

		( node.BlockMargins ??= new BlockMargins() ).Set( escapedTopPositive, escapedTopNegative, escapedBottomPositive, escapedBottomNegative );

		var contentHeight = y;

		if ( Num.IsDefined( availableInnerHeight ) && heightSizingMode != SizingMode.MaxContent && contentHeight > availableInnerHeight )
			layout.HadOverflow = true;

		if ( Num.IsDefined( innerWidth ) && widest > innerWidth )
			layout.HadOverflow = true;

		//-- the sizes ------------------------------------------------------------------------

		node.SetLayoutMeasuredDimension(
			Bounds.BoundAxis( node, FlexDirection.Row, direction,
				widthSizingMode == SizingMode.StretchFit ? availableWidth - marginAxisRow : innerWidth + paddingAndBorderAxisRow,
				ownerWidth, ownerWidth ),
			Dimension.Width );

		if ( heightSizingMode == SizingMode.StretchFit )
		{
			node.SetLayoutMeasuredDimension(
				Bounds.BoundAxis( node, FlexDirection.Column, direction, availableHeight - marginAxisColumn, ownerHeight, ownerWidth ),
				Dimension.Height );
		}
		else if ( heightSizingMode == SizingMode.FitContent && node.Style.Overflow == Overflow.Scroll )
		{
			node.SetLayoutMeasuredDimension(
				Num.MaxOrDefined(
					Num.MinOrDefined( availableInnerHeight + paddingAndBorderAxisColumn,
						Bounds.WithinMinAndMax( node, direction, FlexDirection.Column, contentHeight + paddingAndBorderAxisColumn, ownerHeight, ownerWidth ) ),
					paddingAndBorderAxisColumn ),
				Dimension.Height );
		}
		else
		{
			node.SetLayoutMeasuredDimension(
				Bounds.BoundAxis( node, FlexDirection.Column, direction, contentHeight + paddingAndBorderAxisColumn, ownerHeight, ownerWidth ),
				Dimension.Height );
		}

		if ( !performLayout ) return;

		//The far edges, for the children of a right-to-left box.
		if ( direction == Direction.RTL )
		{
			foreach ( var child in children )
			{
				if ( child.Style.Display == Display.None || child.IsOutOfFlow ) continue;
				Bounds.SetChildTrailingPosition( node, child, FlexDirection.Row );
			}
		}

		//The absolute children, against this box where it contains them.
		if ( node.Style.PositionType != PositionType.Static || node.AlwaysFormsContainingBlock || depth == 1 )
		{
			node.SetLayoutDimension( layout.MeasuredDimension( Dimension.Width ), Dimension.Width );
			node.SetLayoutDimension( layout.MeasuredDimension( Dimension.Height ), Dimension.Height );

			LayoutAbsoluteDescendants( node, node, widthSizingMode, direction, depth, generationCount, 0.0f, 0.0f,
				availableInnerWidth, availableInnerHeight );
		}

		if ( depth == 1 && node.SubtreeHasFixed )
			LayoutFixedDescendants( node, node, widthSizingMode, direction, depth, generationCount, 0.0f, 0.0f );

		layout.Baseline = Baseline.Compute( node );
	}

	//How wide a child would like to be, margins included: its definite width, or its content measured under
	//no limit. A percentage width is cyclic here - the owner is finding its own width - and behaves as auto.
	private static float PreferredOuterWidth( LayoutNode child, Direction direction, int depth, uint generationCount )
	{
		var margins = child.Style.ComputeMarginForAxis( FlexDirection.Row, Num.Undefined );

		if ( child.HasDefiniteLength( Dimension.Width, Num.Undefined ) )
		{
			return Bounds.BoundAxis( child, FlexDirection.Row, direction,
				child.GetResolvedDimension( direction, Dimension.Width, Num.Undefined, Num.Undefined ), Num.Undefined, Num.Undefined ) + margins;
		}

		var heightAvailable = Num.Undefined;
		var heightMode = SizingMode.MaxContent;

		if ( child.HasDefiniteLength( Dimension.Height, Num.Undefined ) )
		{
			heightAvailable = child.GetResolvedDimension( direction, Dimension.Height, Num.Undefined, Num.Undefined )
				+ child.Style.ComputeMarginForAxis( FlexDirection.Column, Num.Undefined );
			heightMode = SizingMode.StretchFit;
		}

		CalculateLayoutInternal( child, Num.Undefined, heightAvailable, direction, SizingMode.MaxContent, heightMode,
			Num.Undefined, Num.Undefined, false, depth, generationCount );

		return child.Layout.MeasuredDimension( Dimension.Width ) + margins;
	}

	//Whether a block holds its children's margins in rather than letting them pass out through its edges: it starts a block formatting context
	//(CSS 2 9.4.1). The root, a box out of the flow, a box that scrolls or clips, and a block whose owner is not itself a block all do.
	private static bool StartsFormattingContext( LayoutNode node, int depth )
	{
		if ( depth == 1 || node.IsOutOfFlow || node.Style.Overflow != Overflow.Visible ) return true;

		var owner = node.Owner;
		while ( owner is not null && owner.Style.Display == Display.Contents ) owner = owner.Owner;

		return owner is null || owner.Style.Display != Display.Block;
	}

	//Whether a box's own top and bottom margins meet each other: a block with no height, no padding or border above or below, and nothing
	//inside that keeps them apart. The height is the one the box just answered with, which a cache hit restores along with the rest.
	private static bool CollapsesThrough( LayoutNode box )
	{
		if ( box.Style.Display != Display.Block || box.IsOutOfFlow || box.Style.Overflow != Overflow.Visible ) return false;
		if ( box.Layout.MeasuredDimension( Dimension.Height ) != 0 ) return false;

		ref var layout = ref box.Layout;
		return layout.Padding( PhysicalEdge.Top ) + layout.Border( PhysicalEdge.Top )
			+ layout.Padding( PhysicalEdge.Bottom ) + layout.Border( PhysicalEdge.Bottom ) == 0;
	}

	//The margins that meet at a box's top edge: its own top margin, and whatever its answer says passed out through its top.
	private static void MarginsAtTop( LayoutNode box, Direction direction, float containingWidth, ref float positive, ref float negative )
	{
		Join( ref positive, ref negative, box.Style.ComputeFlexStartMargin( FlexDirection.Column, direction, containingWidth ) );

		if ( box.Style.Display == Display.Block && box.BlockMargins is { } through )
			Join( ref positive, ref negative, through.TopPositive, through.TopNegative );
	}

	//The same at the bottom edge.
	private static void MarginsAtBottom( LayoutNode box, Direction direction, float containingWidth, ref float positive, ref float negative )
	{
		Join( ref positive, ref negative, box.Style.ComputeFlexEndMargin( FlexDirection.Column, direction, containingWidth ) );

		if ( box.Style.Display == Display.Block && box.BlockMargins is { } through )
			Join( ref positive, ref negative, through.BottomPositive, through.BottomNegative );
	}

	//Collapsing, CSS 2 8.3.1: of the margins that meet, the largest positive and the most negative are kept, and the collapsed margin is their sum.
	private static void Join( ref float positive, ref float negative, float margin )
	{
		if ( Num.IsUndefined( margin ) ) return;
		if ( margin >= 0 ) positive = MathF.Max( positive, margin );
		else negative = MathF.Min( negative, margin );
	}

	private static void Join( ref float positive, ref float negative, float otherPositive, float otherNegative )
	{
		positive = MathF.Max( positive, otherPositive );
		negative = MathF.Min( negative, otherNegative );
	}
}
