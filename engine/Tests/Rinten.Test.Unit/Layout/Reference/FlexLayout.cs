//Frozen phase-7 copy of Rinten.Layout (Yoga port, MIT) for differential tests; do not edit
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
namespace Rinten.Layout.Reference;

//The eleven steps that size and place one node and its children. Ported from Yoga's calculateLayoutImpl . The step numbers
//in the comments are the original's, and they are worth keeping: the algorithm is read and argued about in those terms.
internal static partial class FlexLayout
{
	private static void CalculateLayoutImpl( LayoutNode node, float availableWidth, float availableHeight,
		Direction ownerDirection, SizingMode widthSizingMode, SizingMode heightSizingMode,
		float ownerWidth, float ownerHeight, bool performLayout, int depth, uint generationCount )
	{
		var direction = node.ResolveDirection( ownerDirection );
		node.Layout.Direction = direction;

		var flexRowDirection = Axis.Resolve( FlexDirection.Row, direction );
		var flexColumnDirection = Axis.Resolve( FlexDirection.Column, direction );

		var startEdge = direction == Direction.LTR ? PhysicalEdge.Left : PhysicalEdge.Right;
		var endEdge = direction == Direction.LTR ? PhysicalEdge.Right : PhysicalEdge.Left;

		var marginRowLeading = node.Style.ComputeInlineStartMargin( flexRowDirection, direction, ownerWidth );
		node.Layout.SetMargin( startEdge, marginRowLeading );

		var marginRowTrailing = node.Style.ComputeInlineEndMargin( flexRowDirection, direction, ownerWidth );
		node.Layout.SetMargin( endEdge, marginRowTrailing );

		var marginColumnLeading = node.Style.ComputeInlineStartMargin( flexColumnDirection, direction, ownerWidth );
		node.Layout.SetMargin( PhysicalEdge.Top, marginColumnLeading );

		var marginColumnTrailing = node.Style.ComputeInlineEndMargin( flexColumnDirection, direction, ownerWidth );
		node.Layout.SetMargin( PhysicalEdge.Bottom, marginColumnTrailing );

		var marginAxisRow = marginRowLeading + marginRowTrailing;
		var marginAxisColumn = marginColumnLeading + marginColumnTrailing;

		node.Layout.SetBorder( startEdge, node.Style.ComputeInlineStartBorder( flexRowDirection, direction ) );
		node.Layout.SetBorder( endEdge, node.Style.ComputeInlineEndBorder( flexRowDirection, direction ) );
		node.Layout.SetBorder( PhysicalEdge.Top, node.Style.ComputeInlineStartBorder( flexColumnDirection, direction ) );
		node.Layout.SetBorder( PhysicalEdge.Bottom, node.Style.ComputeInlineEndBorder( flexColumnDirection, direction ) );

		node.Layout.SetPadding( startEdge, node.Style.ComputeInlineStartPadding( flexRowDirection, direction, ownerWidth ) );
		node.Layout.SetPadding( endEdge, node.Style.ComputeInlineEndPadding( flexRowDirection, direction, ownerWidth ) );
		node.Layout.SetPadding( PhysicalEdge.Top, node.Style.ComputeInlineStartPadding( flexColumnDirection, direction, ownerWidth ) );
		node.Layout.SetPadding( PhysicalEdge.Bottom, node.Style.ComputeInlineEndPadding( flexColumnDirection, direction, ownerWidth ) );

		//A block that is not laid out as one this time - a leaf, an empty box, a settled
		//measurement - lets no margin out; what it let out last time must not stand.
		node.BlockMargins?.Set( 0.0f, 0.0f, 0.0f, 0.0f );

		if ( node.HasMeasureFunc )
		{
			MeasureNodeWithMeasureFunc( node, direction, availableWidth - marginAxisRow, availableHeight - marginAxisColumn,
				widthSizingMode, heightSizingMode, ownerWidth, ownerHeight );

			return;
		}

		var childCount = node.LayoutChildren.Count;

		if ( childCount == 0 )
		{
			MeasureNodeWithoutChildren( node, direction, availableWidth - marginAxisRow, availableHeight - marginAxisColumn,
				widthSizingMode, heightSizingMode, ownerWidth, ownerHeight );

			return;
		}

		//Only measuring, and the constraints already settle it: nothing below needs looking at. Except in a block whose margins
		//can pass out through its edges: what passes out depends on the children whatever the size, so it is laid out in full.
		if ( !performLayout && !( node.Style.Display == Display.Block && !StartsFormattingContext( node, depth ) )
			&& MeasureNodeWithFixedSize( node, direction,
			availableWidth - marginAxisRow, availableHeight - marginAxisColumn,
			widthSizingMode, heightSizingMode, ownerWidth, ownerHeight ) )
		{
			return;
		}

		//A block stacks its children instead of flexing them; everything up to here is shared.
		if ( node.Style.Display == Display.Block )
		{
			LayoutBlock( node, availableWidth, availableHeight, direction, widthSizingMode, heightSizingMode,
				ownerWidth, ownerHeight, performLayout, depth, generationCount, marginAxisRow, marginAxisColumn );
			return;
		}

		//A grid places them in tracks.
		if ( node.Style.Display == Display.Grid )
		{
			LayoutGrid( node, availableWidth, availableHeight, direction, widthSizingMode, heightSizingMode,
				ownerWidth, ownerHeight, performLayout, depth, generationCount, marginAxisRow, marginAxisColumn );
			return;
		}

		node.Layout.HadOverflow = false;

		//STEP 1: WHAT THE REST OF THE ALGORITHM WORKS IN

		var mainAxis = Axis.Resolve( node.Style.FlexDirection, direction );
		var crossAxis = Axis.ResolveCross( mainAxis, direction );
		var isMainAxisRow = Axis.IsRow( mainAxis );
		var isNodeFlexWrap = node.Style.FlexWrap != Wrap.NoWrap;

		var mainAxisOwnerSize = isMainAxisRow ? ownerWidth : ownerHeight;
		var crossAxisOwnerSize = isMainAxisRow ? ownerHeight : ownerWidth;

		var paddingAndBorderAxisMain = Bounds.PaddingAndBorderForAxis( node, mainAxis, direction, ownerWidth );
		var paddingAndBorderAxisCross = Bounds.PaddingAndBorderForAxis( node, crossAxis, direction, ownerWidth );
		var leadingPaddingAndBorderCross = node.Style.ComputeFlexStartPaddingAndBorder( crossAxis, direction, ownerWidth );

		var sizingModeMainDim = isMainAxisRow ? widthSizingMode : heightSizingMode;
		var sizingModeCrossDim = isMainAxisRow ? heightSizingMode : widthSizingMode;

		var paddingAndBorderAxisRow = isMainAxisRow ? paddingAndBorderAxisMain : paddingAndBorderAxisCross;
		var paddingAndBorderAxisColumn = isMainAxisRow ? paddingAndBorderAxisCross : paddingAndBorderAxisMain;

		//STEP 2: THE ROOM THERE IS, ACROSS AND ALONG

		var availableInnerWidth = CalculateAvailableInnerDimension( node, direction, Dimension.Width,
			availableWidth - marginAxisRow, paddingAndBorderAxisRow, ownerWidth, ownerWidth );

		var availableInnerHeight = CalculateAvailableInnerDimension( node, direction, Dimension.Height,
			availableHeight - marginAxisColumn, paddingAndBorderAxisColumn, ownerHeight, ownerWidth );

		var availableInnerMainDim = isMainAxisRow ? availableInnerWidth : availableInnerHeight;
		var availableInnerCrossDim = isMainAxisRow ? availableInnerHeight : availableInnerWidth;

		//STEP 3: HOW BIG EACH ITEM WANTS TO BE

		var totalMainDim = ComputeFlexBasisForChildren( node, availableInnerWidth, availableInnerHeight,
			availableInnerWidth, availableInnerHeight, widthSizingMode, heightSizingMode,
			direction, mainAxis, performLayout, depth, generationCount );

		if ( childCount > 1 )
			totalMainDim += node.Style.ComputeGapForAxis( mainAxis, availableInnerMainDim ) * ( childCount - 1 );

		var mainAxisOverflows = sizingModeMainDim != SizingMode.MaxContent && totalMainDim > availableInnerMainDim;

		if ( isNodeFlexWrap && mainAxisOverflows && sizingModeMainDim == SizingMode.FitContent )
			sizingModeMainDim = SizingMode.StretchFit;

		//STEP 4: GATHER THE ITEMS INTO LINES

		var startOfLine = 0;
		var lineCount = 0;
		var totalLineCrossDim = 0.0f;

		var crossAxisGap = node.Style.ComputeGapForAxis( crossAxis, availableInnerCrossDim );
		var maxLineMainDim = 0.0f;

		//What an item's percentage minimum or maximum is a percentage of: the container's inner main
		//size when that is definite, nothing when the container is itself sized by its content.
		var childMainReference = sizingModeMainDim == SizingMode.StretchFit ? availableInnerMainDim : Num.Undefined;


		var flexLine = FlexLinePool.Rent();

		for ( ; startOfLine < node.LayoutChildren.Count; lineCount++ )
		{
			FlexLineBuilder.Calculate( flexLine, node, ownerDirection, ownerWidth, childMainReference,
				availableInnerWidth, availableInnerMainDim, ref startOfLine, lineCount );

			//With the cross size already settled and nothing to place, the whole flexing step can be skipped.
			var canSkipFlex = !performLayout && sizingModeCrossDim == SizingMode.StretchFit;

			//STEP 5: SETTLE THE FLEXIBLE LENGTHS ALONG THE MAIN AXIS

			var sizeBasedOnContent = false;

			if ( sizingModeMainDim != SizingMode.StretchFit )
			{
				var style = node.Style;

				var minInnerWidth = style.ResolvedMinDimension( direction, Dimension.Width, ownerWidth, ownerWidth ) - paddingAndBorderAxisRow;
				var maxInnerWidth = style.ResolvedMaxDimension( direction, Dimension.Width, ownerWidth, ownerWidth ) - paddingAndBorderAxisRow;
				var minInnerHeight = style.ResolvedMinDimension( direction, Dimension.Height, ownerHeight, ownerWidth ) - paddingAndBorderAxisColumn;
				var maxInnerHeight = style.ResolvedMaxDimension( direction, Dimension.Height, ownerHeight, ownerWidth ) - paddingAndBorderAxisColumn;

				var minInnerMainDim = isMainAxisRow ? minInnerWidth : minInnerHeight;
				var maxInnerMainDim = isMainAxisRow ? maxInnerWidth : maxInnerHeight;

				if ( Num.IsDefined( minInnerMainDim ) && flexLine.SizeConsumed < minInnerMainDim )
				{
					availableInnerMainDim = minInnerMainDim;
				}
				else if ( Num.IsDefined( maxInnerMainDim ) && flexLine.SizeConsumed > maxInnerMainDim )
				{
					availableInnerMainDim = maxInnerMainDim;
				}
				else
				{
					//Nothing here can flex, so the room used is all the room wanted. A root shrinks to its contents the same way.
					if ( ( Num.IsDefined( flexLine.Layout.TotalFlexGrowFactors ) && flexLine.Layout.TotalFlexGrowFactors == 0 )
						|| ( Num.IsDefined( node.ResolveFlexGrow() ) && node.ResolveFlexGrow() == 0 ) )
					{
						availableInnerMainDim = flexLine.SizeConsumed;
					}

					sizeBasedOnContent = true;
				}
			}

			if ( !sizeBasedOnContent && Num.IsDefined( availableInnerMainDim ) )
			{
				flexLine.Layout.RemainingFreeSpace = availableInnerMainDim - flexLine.SizeConsumed;
			}
			else if ( flexLine.SizeConsumed < 0 )
			{
				//The main size is indefinite, so the node is sized by its contents; a negative consumption means it gives them nothing.
				flexLine.Layout.RemainingFreeSpace = -flexLine.SizeConsumed;
			}

			if ( !canSkipFlex )
			{
				ResolveFlexibleLength( node, flexLine, mainAxis, crossAxis, direction, ownerWidth, childMainReference,
					availableInnerMainDim, availableInnerCrossDim, availableInnerWidth, availableInnerHeight,
					mainAxisOverflows, sizingModeCrossDim, performLayout, depth, generationCount );
			}

			node.Layout.HadOverflow = node.Layout.HadOverflow || flexLine.Layout.RemainingFreeSpace < 0;

			//STEP 6: SPREAD ALONG THE MAIN AXIS, AND FIND THE CROSS SIZE

			JustifyMainAxis( node, flexLine, mainAxis, crossAxis, direction, sizingModeMainDim, sizingModeCrossDim,
				mainAxisOwnerSize, ownerWidth, childMainReference, availableInnerMainDim, availableInnerCrossDim, availableInnerWidth, availableInnerHeight, performLayout, depth, generationCount );

			var containerCrossAxis = availableInnerCrossDim;

			if ( sizingModeCrossDim is SizingMode.MaxContent or SizingMode.FitContent )
			{
				containerCrossAxis = Bounds.BoundAxis( node, crossAxis, direction,
					flexLine.Layout.CrossDim + paddingAndBorderAxisCross, crossAxisOwnerSize, ownerWidth )
					- paddingAndBorderAxisCross;
			}

			//Without wrapping, the container decides the cross size.
			if ( !isNodeFlexWrap && sizingModeCrossDim == SizingMode.StretchFit )
				flexLine.Layout.CrossDim = availableInnerCrossDim;

			//A single line's cross size is held within the minimum and maximum before anything is aligned inside it.
			if ( !isNodeFlexWrap )
			{
				flexLine.Layout.CrossDim = Bounds.BoundAxis( node, crossAxis, direction,
					flexLine.Layout.CrossDim + paddingAndBorderAxisCross, crossAxisOwnerSize, ownerWidth )
					- paddingAndBorderAxisCross;
			}

			//Baseline alignment in a column lines the items up on their left edges as a group at the flex start. Read from the left that
			//is where each would sit anyway; read from the right, under right-to-left writing, the group is as wide as its widest member.
			var baselineGroupCross = ColumnBaselineGroupCross( node, flexLine.ItemsInFlow, -1, crossAxis, availableInnerWidth );

			//STEP 7: ALIGN ACROSS THE LINE

			if ( performLayout )
			{
				foreach ( var child in flexLine.ItemsInFlow )
				{
					var leadingCrossDim = leadingPaddingAndBorderCross;

					var alignItem = Axis.ResolveChildAlignment( node, child );

					if ( alignItem == Align.Stretch
						&& !child.Style.FlexStartMarginIsAuto( crossAxis, direction )
						&& !child.Style.FlexEndMarginIsAuto( crossAxis, direction ) )
					{
						//A stretched child is laid out once more, this time held to the line's own cross size
						//- unless it has a definite one of its own, in which case there is nothing to stretch.
						if ( !child.HasDefiniteLength( Axis.DimensionOf( crossAxis ), availableInnerCrossDim ) )
						{
							var childMainSize = child.Layout.MeasuredDimension( Axis.DimensionOf( mainAxis ) );

							//A stretched child fills the line across whatever its ratio says: the stretch made its cross size definite, and the ratio only ever
							//follows a definite side, which it did when the main size was found. The original let the ratio win here, and a browser does not.
							var childCrossSize = flexLine.Layout.CrossDim;

							childMainSize += child.Style.ComputeMarginForAxis( mainAxis, availableInnerWidth );

							var childMainSizingMode = SizingMode.StretchFit;
							var childCrossSizingMode = SizingMode.StretchFit;

							ConstrainMaxSizeForMode( child, direction, mainAxis, availableInnerMainDim, availableInnerWidth,
								ref childMainSizingMode, ref childMainSize );

							ConstrainMaxSizeForMode( child, direction, crossAxis, availableInnerCrossDim, availableInnerWidth,
								ref childCrossSizingMode, ref childCrossSize );

							var childWidth = isMainAxisRow ? childMainSize : childCrossSize;
							var childHeight = !isMainAxisRow ? childMainSize : childCrossSize;

							var crossAxisDoesNotGrow = node.Style.AlignContent != Align.Stretch && isNodeFlexWrap;

							var childWidthSizingMode = Num.IsUndefined( childWidth ) || ( !isMainAxisRow && crossAxisDoesNotGrow )
								? SizingMode.MaxContent : SizingMode.StretchFit;

							var childHeightSizingMode = Num.IsUndefined( childHeight ) || ( isMainAxisRow && crossAxisDoesNotGrow )
								? SizingMode.MaxContent : SizingMode.StretchFit;

							CalculateLayoutInternal( child, childWidth, childHeight, direction,
								childWidthSizingMode, childHeightSizingMode, availableInnerWidth, availableInnerHeight,
								true, depth, generationCount );
						}
					}
					else
					{
						var remainingCrossDim = containerCrossAxis - child.DimensionWithMargin( crossAxis, availableInnerWidth );

						if ( child.Style.FlexStartMarginIsAuto( crossAxis, direction )
							&& child.Style.FlexEndMarginIsAuto( crossAxis, direction ) )
						{
							leadingCrossDim += Num.MaxOrDefined( 0.0f, remainingCrossDim / 2 );
						}
						else if ( child.Style.FlexEndMarginIsAuto( crossAxis, direction ) )
						{
							//Nothing: an automatic margin at the far end pins it to the start.
						}
						else if ( child.Style.FlexStartMarginIsAuto( crossAxis, direction ) )
						{
							leadingCrossDim += Num.MaxOrDefined( 0.0f, remainingCrossDim );
						}
						else if ( alignItem == Align.FlexStart )
						{
							if ( Num.IsDefined( baselineGroupCross ) && Axis.IsColumnBaseline( node, child ) )
								leadingCrossDim += baselineGroupCross - child.DimensionWithMargin( crossAxis, availableInnerWidth );
						}
						else if ( alignItem == Align.Center )
						{
							leadingCrossDim += remainingCrossDim / 2;
						}
						else
						{
							leadingCrossDim += remainingCrossDim;
						}
					}

					child.Layout.SetPosition( Axis.FlexStartEdge( crossAxis ),
						child.Layout.Position( Axis.FlexStartEdge( crossAxis ) ) + totalLineCrossDim + leadingCrossDim );
				}
			}


			var appliedCrossGap = lineCount != 0 ? crossAxisGap : 0.0f;

			totalLineCrossDim += flexLine.Layout.CrossDim + appliedCrossGap;
			maxLineMainDim = Num.MaxOrDefined( maxLineMainDim, flexLine.Layout.MainDim );
		}

		FlexLinePool.Return( flexLine );

		//STEP 8: SPREAD THE LINES THEMSELVES

		if ( performLayout && ( isNodeFlexWrap || Baseline.IsBaselineLayout( node ) ) )
		{
			var leadPerLine = 0.0f;
			var currentLead = leadingPaddingAndBorderCross;
			var extraSpacePerLine = 0.0f;

			var unclampedCrossDim = sizingModeCrossDim == SizingMode.StretchFit
				? availableInnerCrossDim + paddingAndBorderAxisCross
				: node.HasDefiniteLength( Axis.DimensionOf( crossAxis ), crossAxisOwnerSize )
					? node.GetResolvedDimension( direction, Axis.DimensionOf( crossAxis ), crossAxisOwnerSize, ownerWidth )
					: totalLineCrossDim + paddingAndBorderAxisCross;

			var innerCrossDim = Bounds.BoundAxis( node, crossAxis, direction, unclampedCrossDim,
				crossAxisOwnerSize, ownerWidth ) - paddingAndBorderAxisCross;

			var remainingAlignContentDim = innerCrossDim - totalLineCrossDim;

			var alignContent = remainingAlignContentDim >= 0
				? node.Style.AlignContent
				: Axis.Fallback( node.Style.AlignContent );

			switch ( alignContent )
			{
				case Align.FlexEnd:
					currentLead += remainingAlignContentDim;
					break;

				case Align.Center:
					currentLead += remainingAlignContentDim / 2;
					break;

				case Align.Stretch:
					extraSpacePerLine = remainingAlignContentDim / lineCount;
					break;

				case Align.SpaceAround:
					currentLead += remainingAlignContentDim / ( 2 * lineCount );
					leadPerLine = remainingAlignContentDim / lineCount;
					break;

				case Align.SpaceEvenly:
					currentLead += remainingAlignContentDim / ( lineCount + 1 );
					leadPerLine = remainingAlignContentDim / ( lineCount + 1 );
					break;

				case Align.SpaceBetween:
					if ( lineCount > 1 ) leadPerLine = remainingAlignContentDim / ( lineCount - 1 );
					break;

				default:
					break;
			}

			var children = node.LayoutChildren;
			var endIndex = 0;

			for ( var i = 0; i < lineCount; i++ )
			{
				var startIndex = endIndex;
				var index = startIndex;

				//How thick this line is, and where it ends.
				var lineHeight = 0.0f;
				var maxAscentForCurrentLine = 0.0f;
				var maxDescentForCurrentLine = 0.0f;

				for ( ; index < children.Count; index++ )
				{
					var child = children[index];

					if ( child.Style.Display == Display.None ) continue;

					if ( !child.IsOutOfFlow )
					{
						if ( child.LineIndex != i ) break;

						if ( child.IsLayoutDimensionDefined( crossAxis ) )
						{
							lineHeight = Num.MaxOrDefined( lineHeight,
								child.Layout.MeasuredDimension( Axis.DimensionOf( crossAxis ) )
									+ child.Style.ComputeMarginForAxis( crossAxis, availableInnerWidth ) );
						}

						if ( Axis.ResolveChildAlignment( node, child ) == Align.Baseline )
						{
							var ascent = Baseline.Calculate( child )
								+ child.Style.ComputeFlexStartMargin( FlexDirection.Column, direction, availableInnerWidth );

							var descent = child.Layout.MeasuredDimension( Dimension.Height )
								+ child.Style.ComputeMarginForAxis( FlexDirection.Column, availableInnerWidth ) - ascent;

							maxAscentForCurrentLine = Num.MaxOrDefined( maxAscentForCurrentLine, ascent );
							maxDescentForCurrentLine = Num.MaxOrDefined( maxDescentForCurrentLine, descent );

							lineHeight = Num.MaxOrDefined( lineHeight, maxAscentForCurrentLine + maxDescentForCurrentLine );
						}
					}
				}

				endIndex = index;
				currentLead += i != 0 ? crossAxisGap : 0;

				//A single line is as thick as the container is across, CSS Flexbox 9.4 step 8;
				//the children alone said 30 where the container said 200, and everything centred or ended against the wrong height.
				if ( !isNodeFlexWrap ) lineHeight = Num.MaxOrDefined( lineHeight, totalLineCrossDim );

				var lineBaselineGroupCross = ColumnBaselineGroupCross( node, children, i, crossAxis, availableInnerWidth );
				lineHeight += extraSpacePerLine;

				for ( index = startIndex; index < endIndex; index++ )
				{
					var child = children[index];

					if ( child.Style.Display == Display.None ) continue;
					if ( child.IsOutOfFlow ) continue;

					switch ( Axis.ResolveChildAlignment( node, child ) )
					{
						case Align.FlexStart:
							child.Layout.SetPosition( Axis.FlexStartEdge( crossAxis ),
								currentLead + child.Style.ComputeFlexStartPosition( crossAxis, direction, availableInnerWidth )
									+ ( Num.IsDefined( lineBaselineGroupCross ) && Axis.IsColumnBaseline( node, child )
										? lineBaselineGroupCross - child.DimensionWithMargin( crossAxis, availableInnerWidth ) : 0.0f ) );
							break;

						case Align.FlexEnd:
							child.Layout.SetPosition( Axis.FlexStartEdge( crossAxis ),
								currentLead + lineHeight
									- child.Style.ComputeFlexEndMargin( crossAxis, direction, availableInnerWidth )
									- child.Layout.MeasuredDimension( Axis.DimensionOf( crossAxis ) ) );
							break;

						case Align.Center:
							child.Layout.SetPosition( Axis.FlexStartEdge( crossAxis ),
								currentLead + ( ( lineHeight - child.Layout.MeasuredDimension( Axis.DimensionOf( crossAxis ) ) ) / 2 ) );
							break;

						case Align.Stretch:
							child.Layout.SetPosition( Axis.FlexStartEdge( crossAxis ),
								currentLead + child.Style.ComputeFlexStartMargin( crossAxis, direction, availableInnerWidth ) );

							//It was only ever measured against the owner's size; measure it again at the line's height.
							if ( !child.HasDefiniteLength( Axis.DimensionOf( crossAxis ), availableInnerCrossDim ) )
							{
								var childWidth = isMainAxisRow
									? child.Layout.MeasuredDimension( Dimension.Width )
										+ child.Style.ComputeMarginForAxis( mainAxis, availableInnerWidth )
									: leadPerLine + lineHeight;

								var childHeight = !isMainAxisRow
									? child.Layout.MeasuredDimension( Dimension.Height )
										+ child.Style.ComputeMarginForAxis( crossAxis, availableInnerWidth )
									: leadPerLine + lineHeight;

								if ( !( Num.InexactEquals( childWidth, child.Layout.MeasuredDimension( Dimension.Width ) )
									&& Num.InexactEquals( childHeight, child.Layout.MeasuredDimension( Dimension.Height ) ) ) )
								{
									CalculateLayoutInternal( child, childWidth, childHeight, direction,
										SizingMode.StretchFit, SizingMode.StretchFit,
										availableInnerWidth, availableInnerHeight, true, depth, generationCount );
								}
							}
							break;

						case Align.Baseline:
							child.Layout.SetPosition( PhysicalEdge.Top,
								currentLead + maxAscentForCurrentLine - Baseline.Calculate( child )
									+ child.Style.ComputeFlexStartPosition( FlexDirection.Column, direction, availableInnerCrossDim ) );
							break;

						default:
							break;
					}
				}

				currentLead = currentLead + leadPerLine + lineHeight;
			}
		}

		//STEP 9: THE FINAL SIZES

		node.SetLayoutMeasuredDimension(
			Bounds.BoundAxis( node, FlexDirection.Row, direction, availableWidth - marginAxisRow, ownerWidth, ownerWidth ),
			Dimension.Width );

		node.SetLayoutMeasuredDimension(
			Bounds.BoundAxis( node, FlexDirection.Column, direction, availableHeight - marginAxisColumn, ownerHeight, ownerWidth ),
			Dimension.Height );

		//Nothing was said about the size, so the children decide it.
		if ( sizingModeMainDim == SizingMode.MaxContent
			|| ( node.Style.Overflow != Overflow.Scroll && sizingModeMainDim == SizingMode.FitContent ) )
		{
			node.SetLayoutMeasuredDimension(
				Bounds.BoundAxis( node, mainAxis, direction, maxLineMainDim, mainAxisOwnerSize, ownerWidth ),
				Axis.DimensionOf( mainAxis ) );
		}
		else if ( sizingModeMainDim == SizingMode.FitContent && node.Style.Overflow == Overflow.Scroll )
		{
			node.SetLayoutMeasuredDimension(
				Num.MaxOrDefined(
					Num.MinOrDefined( availableInnerMainDim + paddingAndBorderAxisMain,
						Bounds.WithinMinAndMax( node, direction, mainAxis, maxLineMainDim, mainAxisOwnerSize, ownerWidth ) ),
					paddingAndBorderAxisMain ),
				Axis.DimensionOf( mainAxis ) );
		}

		if ( sizingModeCrossDim == SizingMode.MaxContent
			|| ( node.Style.Overflow != Overflow.Scroll && sizingModeCrossDim == SizingMode.FitContent ) )
		{
			node.SetLayoutMeasuredDimension(
				Bounds.BoundAxis( node, crossAxis, direction, totalLineCrossDim + paddingAndBorderAxisCross,
					crossAxisOwnerSize, ownerWidth ),
				Axis.DimensionOf( crossAxis ) );
		}
		else if ( sizingModeCrossDim == SizingMode.FitContent && node.Style.Overflow == Overflow.Scroll )
		{
			node.SetLayoutMeasuredDimension(
				Num.MaxOrDefined(
					Num.MinOrDefined( availableInnerCrossDim + paddingAndBorderAxisCross,
						Bounds.WithinMinAndMax( node, direction, crossAxis,
							totalLineCrossDim + paddingAndBorderAxisCross, crossAxisOwnerSize, ownerWidth ) ),
					paddingAndBorderAxisCross ),
				Axis.DimensionOf( crossAxis ) );
		}

		//The lines were only ever wrapped one way round, so a reversed wrap turns their positions over at the end.
		if ( performLayout && node.Style.FlexWrap == Wrap.WrapReverse )
		{
			foreach ( var child in node.LayoutChildren )
			{
				//A hidden child was zeroed, not measured; flipping it would write a NaN position.
				if ( child.IsOutOfFlow || child.Style.Display == Display.None ) continue;

				child.Layout.SetPosition( Axis.FlexStartEdge( crossAxis ),
					node.Layout.MeasuredDimension( Axis.DimensionOf( crossAxis ) )
						- child.Layout.Position( Axis.FlexStartEdge( crossAxis ) )
						- child.Layout.MeasuredDimension( Axis.DimensionOf( crossAxis ) ) );
			}
		}

		if ( performLayout )
		{
			//STEP 10: THE FAR EDGES

			var needsMainTrailingPos = Bounds.NeedsTrailingPosition( mainAxis );
			var needsCrossTrailingPos = Bounds.NeedsTrailingPosition( crossAxis );

			if ( needsMainTrailingPos || needsCrossTrailingPos )
			{
				foreach ( var child in node.LayoutChildren )
				{
					//An absolute child is placed by its containing block, which cannot be trusted to have finished yet.
					if ( child.Style.Display == Display.None || child.IsOutOfFlow )
						continue;

					if ( needsMainTrailingPos ) Bounds.SetChildTrailingPosition( node, child, mainAxis );
					if ( needsCrossTrailingPos ) Bounds.SetChildTrailingPosition( node, child, crossAxis );
				}
			}

			//STEP 11: THE ABSOLUTE CHILDREN

			if ( node.Style.PositionType != PositionType.Static || node.AlwaysFormsContainingBlock || depth == 1 )
			{
				//The absolute children are placed against their ancestors' laid-out sizes, which a later
				//measurement cannot overwrite; this box's own is written here rather than after the pass.
				node.SetLayoutDimension( node.Layout.MeasuredDimension( Dimension.Width ), Dimension.Width );
				node.SetLayoutDimension( node.Layout.MeasuredDimension( Dimension.Height ), Dimension.Height );

				LayoutAbsoluteDescendants( node, node,
					isMainAxisRow ? sizingModeMainDim : sizingModeCrossDim, direction,
					depth, generationCount, 0.0f, 0.0f, availableInnerWidth, availableInnerHeight );
			}

			//The root places the fixed nodes, wherever they are in the tree.
			if ( depth == 1 && node.SubtreeHasFixed )
			{
				LayoutFixedDescendants( node, node, isMainAxisRow ? sizingModeMainDim : sizingModeCrossDim, direction,
					depth, generationCount, 0.0f, 0.0f );
			}
		}

		//The baseline, read off the positions this layout wrote; a measurement leaves the last one.
		if ( performLayout ) node.Layout.Baseline = Baseline.Compute( node );
	}

	//Width of a column line's baseline group (matters only in RTL), or undefined; line -1 takes all
	private static float ColumnBaselineGroupCross( LayoutNode node, IReadOnlyList<LayoutNode> items, int lineIndex,
		FlexDirection crossAxis, float availableInnerWidth )
	{
		if ( Axis.IsRow( node.Style.FlexDirection ) || Axis.FlexStartEdge( crossAxis ) != PhysicalEdge.Right ) return Num.Undefined;

		var widest = Num.Undefined;

		for ( var i = 0; i < items.Count; i++ )
		{
			var child = items[i];

			if ( child.Style.Display == Display.None || child.IsOutOfFlow ) continue;
			if ( lineIndex >= 0 && child.LineIndex != lineIndex ) continue;
			if ( !Axis.IsColumnBaseline( node, child ) ) continue;

			widest = Num.MaxOrDefined( widest, child.DimensionWithMargin( crossAxis, availableInnerWidth ) );
		}

		return widest;
	}
}
