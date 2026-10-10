using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Rinten.Layout;

//Flexbox ported from Yoga's CalculateLayout.cpp; undefined sizes are NaN, so comparisons are false
internal static partial class FlexLayout
{
	//Counted up once a layout, so a dirty node is visited at least once per pass and skipped on any repeat visit within it.
	private static uint currentGenerationCount;

	private const int MaxDepth = 256;

	//-- entry point -------------------------------------------------------------------

	/// <summary>Lays out a tree. The owner size is what percentages resolve against and what a stretched root fills; either may be undefined.</summary>
	public static void Calculate( LayoutNode node, float ownerWidth, float ownerHeight, Direction ownerDirection )
	{
		var generationCount = ++currentGenerationCount;

		node.ProcessDimensions();

		var direction = node.ResolveDirection( ownerDirection );

		float width;
		SizingMode widthSizingMode;

		if ( node.HasDefiniteLength( Dimension.Width, ownerWidth ) )
		{
			width = node.GetResolvedDimension( direction, Axis.DimensionOf( FlexDirection.Row ), ownerWidth, ownerWidth )
				+ node.Style.ComputeMarginForAxis( FlexDirection.Row, ownerWidth );

			widthSizingMode = SizingMode.StretchFit;
		}
		else if ( Num.IsDefined( node.Style.ResolvedMaxDimension( direction, Dimension.Width, ownerWidth, ownerWidth ) ) )
		{
			width = node.Style.ResolvedMaxDimension( direction, Dimension.Width, ownerWidth, ownerWidth );
			widthSizingMode = SizingMode.FitContent;
		}
		else
		{
			width = ownerWidth;
			widthSizingMode = Num.IsUndefined( width ) ? SizingMode.MaxContent : SizingMode.StretchFit;
		}

		float height;
		SizingMode heightSizingMode;

		if ( node.HasDefiniteLength( Dimension.Height, ownerHeight ) )
		{
			height = node.GetResolvedDimension( direction, Axis.DimensionOf( FlexDirection.Column ), ownerHeight, ownerWidth )
				+ node.Style.ComputeMarginForAxis( FlexDirection.Column, ownerWidth );

			heightSizingMode = SizingMode.StretchFit;
		}
		else if ( Num.IsDefined( node.Style.ResolvedMaxDimension( direction, Dimension.Height, ownerHeight, ownerWidth ) ) )
		{
			height = node.Style.ResolvedMaxDimension( direction, Dimension.Height, ownerHeight, ownerWidth );
			heightSizingMode = SizingMode.FitContent;
		}
		else
		{
			height = ownerHeight;
			heightSizingMode = Num.IsUndefined( height ) ? SizingMode.MaxContent : SizingMode.StretchFit;
		}

		if ( CalculateLayoutInternal( node, width, height, ownerDirection, widthSizingMode, heightSizingMode,
			ownerWidth, ownerHeight, true, 0, generationCount ) )
		{
			node.SetPosition( node.Layout.Direction, ownerWidth, ownerHeight );
			PixelGrid.RoundLayoutResults( node, 0.0f, 0.0f );
		}
	}

	//-- the cache in front of the algorithm ---------------------------------------------

	//Skips work if a cached layout or measurement still answers; returns whether anything was computed
	private static bool CalculateLayoutInternal( LayoutNode node, float availableWidth, float availableHeight,
		Direction ownerDirection, SizingMode widthSizingMode, SizingMode heightSizingMode,
		float ownerWidth, float ownerHeight, bool performLayout, int depth, uint generationCount )
	{
		//Max-content carries no size, so drop any size passed alongside it before it leaks into children
		if ( widthSizingMode == SizingMode.MaxContent ) availableWidth = Num.Undefined;
		if ( heightSizingMode == SizingMode.MaxContent ) availableHeight = Num.Undefined;

		ref var layout = ref node.Layout;

		depth++;

		if ( depth > MaxDepth ) return false;

		var needToVisitNode = ( node.IsDirty && layout.GenerationCount != generationCount )
			|| layout.ConfigVersion != node.Config.Version
			|| layout.LastOwnerDirection != ownerDirection;

		if ( needToVisitNode )
		{
			layout.NextCachedMeasurementsIndex = 0;
			layout.CachedLayout = CachedMeasurement.Empty;
		}

		var cachedIndex = -1;
		var cachedIsLayout = false;
		var haveCached = false;

		//Room-independent subtrees reuse old answers like text leaves; others need an exact cache match
		var leaf = node.HasMeasureFunc;
		var reusable = leaf || ( !node.SubtreeBlocksReuse && !DisableContainerReuse );
		var keyedOnOwner = leaf ? node.Style.UsesPercentBesidesSize : node.Style.UsesPercent;

		var marginAxisRow = 0.0f;
		var marginAxisColumn = 0.0f;

		if ( reusable )
		{
			marginAxisRow = node.Style.ComputeMarginForAxis( FlexDirection.Row, ownerWidth );
			marginAxisColumn = node.Style.ComputeMarginForAxis( FlexDirection.Column, ownerWidth );

			//A measurement whose constraints settle both axes is answered without looking inside, a layout under the same
			//constraints is not, so a layout entry cannot stand in for such a measurement even when it is the very same question.
			var settledMeasurement = !leaf && !performLayout
				&& IsFixedSize( availableWidth - marginAxisRow, widthSizingMode )
				&& IsFixedSize( availableHeight - marginAxisColumn, heightSizingMode );

			if ( !settledMeasurement && Serves( in layout.CachedLayout, leaf, widthSizingMode, availableWidth, heightSizingMode, availableHeight,
				marginAxisRow, marginAxisColumn, keyedOnOwner, ownerWidth, ownerHeight, node.Config ) )
			{
				haveCached = true;
				cachedIsLayout = true;
			}
			else if ( !performLayout || leaf )
			{
				//A measurement never placed any children, so it can stand in for a layout only where there are none to place.
				for ( var i = 0; i < layout.NextCachedMeasurementsIndex; i++ )
				{
					if ( Serves( in layout.CachedMeasurements[i], leaf, widthSizingMode, availableWidth, heightSizingMode, availableHeight,
						marginAxisRow, marginAxisColumn, keyedOnOwner, ownerWidth, ownerHeight, node.Config ) )
					{
						haveCached = true;
						cachedIndex = i;
						break;
					}
				}
			}
		}
		else if ( performLayout )
		{
			if ( Exactly( in layout.CachedLayout, widthSizingMode, availableWidth, heightSizingMode, availableHeight,
				keyedOnOwner, ownerWidth, ownerHeight ) )
			{
				haveCached = true;
				cachedIsLayout = true;
			}
		}
		else
		{
			for ( var i = 0; i < layout.NextCachedMeasurementsIndex; i++ )
			{
				if ( Exactly( in layout.CachedMeasurements[i], widthSizingMode, availableWidth, heightSizingMode, availableHeight,
					keyedOnOwner, ownerWidth, ownerHeight ) )
				{
					haveCached = true;
					cachedIndex = i;
					break;
				}
			}
		}


		//Nothing is computed ahead of the question any more; a hit stands only where the node was not dirtied.
		const bool probed = false;

		if ( haveCached && ( !needToVisitNode || probed ) )
		{
			ref readonly var hit = ref cachedIsLayout ? ref layout.CachedLayout : ref layout.CachedMeasurements[cachedIndex];

			if ( VerifyCache )
			{
				VerifyHit( node, in hit, cachedIsLayout, probed, availableWidth, widthSizingMode, availableHeight, heightSizingMode,
					ownerDirection, ownerWidth, ownerHeight, performLayout, depth, generationCount );
			}

			layout.SetMeasuredDimension( Dimension.Width, hit.ComputedWidth );
			layout.SetMeasuredDimension( Dimension.Height, hit.ComputedHeight );
			layout.HadOverflow = hit.HadOverflow;
			layout.Baseline = hit.Baseline;
			node.BlockMargins?.Load( cachedIsLayout ? BlockMargins.LayoutSlot : cachedIndex );
		}
		else
		{
			Trace?.Invoke( node, availableWidth, widthSizingMode, availableHeight, heightSizingMode, performLayout, depth );

			CalculateLayoutImpl( node, availableWidth, availableHeight, ownerDirection,
				widthSizingMode, heightSizingMode, ownerWidth, ownerHeight, performLayout, depth, generationCount );

			layout.LastOwnerDirection = ownerDirection;
			layout.ConfigVersion = node.Config.Version;


			if ( !haveCached )
			{
				var entry = Entry( in layout, availableWidth, widthSizingMode, availableHeight, heightSizingMode, ownerWidth, ownerHeight );

				if ( performLayout )
				{
					layout.CachedLayout = entry;
					node.BlockMargins?.Save( BlockMargins.LayoutSlot );
				}
				else
				{
					//Stored first, on its own line: a null-conditional call skips its arguments, and a measurement that is not stored is a cache that never hits.
					var slot = StoreMeasurement( ref layout, entry );
					node.BlockMargins?.Save( slot );
				}
			}
		}

		if ( VerifyCache )
		{
			Note( node, depth, availableWidth, widthSizingMode, availableHeight, heightSizingMode, performLayout,
				haveCached && ( !needToVisitNode || probed ), probed, cachedIsLayout, cachedIndex, ref layout );
		}

		if ( performLayout )
		{
			node.SetLayoutDimension( layout.MeasuredDimension( Dimension.Width ), Dimension.Width );
			node.SetLayoutDimension( layout.MeasuredDimension( Dimension.Height ), Dimension.Height );

			node.HasNewLayout = true;
			node.SetDirty( false );

			//A child that is only its contents has no box of its own: its children were laid
			//out as this node's, and it gets an empty rect at the origin, as a browser gives it.
			if ( node.HasContentsChildren ) ZeroOutContentsChildren( node );
		}

		layout.GenerationCount = generationCount;

		return needToVisitNode || !haveCached;
	}

	//-- the cache entries ---------------------------------------------------------------

	//Whether an entry answers the question under the reuse rules of MeasurementCache,
	//and against the same owner size where a percentage below makes that matter.
	private static bool Serves( in CachedMeasurement entry, bool leaf, SizingMode widthMode, float availableWidth,
		SizingMode heightMode, float availableHeight, float marginRow, float marginColumn,
		bool keyedOnOwner, float ownerWidth, float ownerHeight, LayoutConfig config )
	{
		if ( keyedOnOwner && !entry.SameOwner( ownerWidth, ownerHeight ) ) return false;

		return leaf
			? MeasurementCache.CanUseCachedMeasurement( widthMode, availableWidth, heightMode, availableHeight,
			entry.WidthSizingMode, entry.AvailableWidth, entry.HeightSizingMode, entry.AvailableHeight,
				entry.ComputedWidth, entry.ComputedHeight, marginRow, marginColumn, config )
			: MeasurementCache.CanUseCachedContainerMeasurement( widthMode, availableWidth, heightMode, availableHeight,
				entry.WidthSizingMode, entry.AvailableWidth, entry.HeightSizingMode, entry.AvailableHeight,
				entry.ComputedWidth, entry.ComputedHeight, marginRow, marginColumn, config );
	}

	//Whether an entry is the very same question.
	private static bool Exactly( in CachedMeasurement entry, SizingMode widthMode, float availableWidth,
		SizingMode heightMode, float availableHeight, bool keyedOnOwner, float ownerWidth, float ownerHeight )
	{
		return Num.InexactEquals( entry.AvailableWidth, availableWidth )
			&& Num.InexactEquals( entry.AvailableHeight, availableHeight )
			&& entry.WidthSizingMode == widthMode
			&& entry.HeightSizingMode == heightMode
			&& ( !keyedOnOwner || entry.SameOwner( ownerWidth, ownerHeight ) );
	}

	//The answer just computed, as an entry.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static CachedMeasurement Entry( in LayoutResults layout, float availableWidth, SizingMode widthMode,
		float availableHeight, SizingMode heightMode, float ownerWidth, float ownerHeight )
		=> new()
		{
			AvailableWidth = availableWidth,
			AvailableHeight = availableHeight,
			WidthSizingMode = widthMode,
			HeightSizingMode = heightMode,
			OwnerWidth = ownerWidth,
			OwnerHeight = ownerHeight,
			ComputedWidth = layout.MeasuredDimension( Dimension.Width ),
			ComputedHeight = layout.MeasuredDimension( Dimension.Height ),
			HadOverflow = layout.HadOverflow,
			Baseline = layout.Baseline,
		};

	//Puts a measurement into the ring of slots and says where it went.
	private static int StoreMeasurement( ref LayoutResults layout, in CachedMeasurement entry )
	{
		if ( layout.NextCachedMeasurementsIndex == LayoutResults.MaxCachedMeasurements )
			layout.NextCachedMeasurementsIndex = 0;

		var index = layout.NextCachedMeasurementsIndex++;
		layout.CachedMeasurements[index] = entry;
		return index;
	}

	//-- the pieces the main routine leans on ---------------------------------------------

	//Holds a size under the node's own maximum, tightening the mode where it has to.
	private static void ConstrainMaxSizeForMode( LayoutNode node, Direction direction, FlexDirection axis,
		float ownerAxisSize, float ownerWidth, ref SizingMode mode, ref float size )
	{
		var maxSize = node.Style.ResolvedMaxDimension( direction, Axis.DimensionOf( axis ), ownerAxisSize, ownerWidth )
			+ node.Style.ComputeMarginForAxis( axis, ownerWidth );

		switch ( mode )
		{
			case SizingMode.StretchFit:
			case SizingMode.FitContent:
				size = Num.IsUndefined( maxSize ) || size < maxSize ? size : maxSize;
				break;

			case SizingMode.MaxContent:
				if ( Num.IsDefined( maxSize ) )
				{
					mode = SizingMode.FitContent;
					size = maxSize;
				}
				break;
		}
	}

	//Works out how big a child wants to be before any flexing, and leaves it on the child as its computed flex basis.
	private static void ComputeFlexBasisForChild( LayoutNode node, LayoutNode child, float width, SizingMode widthMode,
		float height, float ownerWidth, float ownerHeight, SizingMode heightMode, Direction direction,
		int depth, uint generationCount )
	{
		var mainAxis = Axis.Resolve( node.Style.FlexDirection, direction );
		var isMainAxisRow = Axis.IsRow( mainAxis );
		var mainAxisSize = isMainAxisRow ? width : height;
		var mainAxisOwnerSize = isMainAxisRow ? ownerWidth : ownerHeight;

		var childWidth = Num.Undefined;
		var childHeight = Num.Undefined;
		SizingMode childWidthSizingMode;
		SizingMode childHeightSizingMode;

		//A percentage of a size still being found from content is cyclic and acts as auto (CSS Sizing 3)
		var widthReference = widthMode == SizingMode.StretchFit ? ownerWidth : Num.Undefined;
		var heightReference = heightMode == SizingMode.StretchFit ? ownerHeight : Num.Undefined;

		var resolvedFlexBasis = child.ResolveFlexBasis( direction, mainAxis, isMainAxisRow ? widthReference : heightReference, ownerWidth );
		var isRowStyleDimDefined = child.HasDefiniteLength( Dimension.Width, widthReference );
		var isColumnStyleDimDefined = child.HasDefiniteLength( Dimension.Height, heightReference );

		if ( Num.IsDefined( resolvedFlexBasis ) && Num.IsDefined( mainAxisSize ) )
		{
			//Taken afresh on every pass. The original kept the first value computed within a pass, so a content measurement made with no main
			//size to resolve against stuck to the child for the passes that had one, and the answer depended on the order of the questions.
			var paddingAndBorder = Bounds.PaddingAndBorderForAxis( child, mainAxis, direction, ownerWidth );

			child.SetLayoutComputedFlexBasis( Num.MaxOrDefined( resolvedFlexBasis, paddingAndBorder ) );
		}
		else if ( isMainAxisRow && isRowStyleDimDefined )
		{
			//The width is definite, so that is the basis.
			var paddingAndBorder = Bounds.PaddingAndBorderForAxis( child, FlexDirection.Row, direction, ownerWidth );

			child.SetLayoutComputedFlexBasis( Num.MaxOrDefined(
				child.GetResolvedDimension( direction, Dimension.Width, ownerWidth, ownerWidth ), paddingAndBorder ) );
		}
		else if ( !isMainAxisRow && isColumnStyleDimDefined )
		{
			var paddingAndBorder = Bounds.PaddingAndBorderForAxis( child, FlexDirection.Column, direction, ownerWidth );

			child.SetLayoutComputedFlexBasis( Num.MaxOrDefined(
				child.GetResolvedDimension( direction, Dimension.Height, ownerHeight, ownerWidth ), paddingAndBorder ) );
		}
		else
		{
			//Nothing definite to go on: measure the child and take what it says.
			childWidthSizingMode = SizingMode.MaxContent;
			childHeightSizingMode = SizingMode.MaxContent;

			var marginRow = child.Style.ComputeMarginForAxis( FlexDirection.Row, ownerWidth );
			var marginColumn = child.Style.ComputeMarginForAxis( FlexDirection.Column, ownerWidth );

			if ( isRowStyleDimDefined )
			{
				childWidth = child.GetResolvedDimension( direction, Dimension.Width, ownerWidth, ownerWidth ) + marginRow;
				childWidthSizingMode = SizingMode.StretchFit;
			}

			if ( isColumnStyleDimDefined )
			{
				childHeight = child.GetResolvedDimension( direction, Dimension.Height, ownerHeight, ownerWidth ) + marginColumn;
				childHeightSizingMode = SizingMode.StretchFit;
			}

			//Cyclic percentages are measured as content alone with no limit, so the answer is computed once
			var cyclicWidth = Num.IsUndefined( widthReference ) && child.ProcessedDimension( Dimension.Width ).IsPercent;
			var cyclicHeight = Num.IsUndefined( heightReference ) && child.ProcessedDimension( Dimension.Height ).IsPercent;

			//The specification says nothing about overflow here, but every major browser behaves this way.
			if ( ( !isMainAxisRow && node.Style.Overflow == Overflow.Scroll ) || node.Style.Overflow != Overflow.Scroll )
			{
				if ( Num.IsUndefined( childWidth ) && Num.IsDefined( width ) && !cyclicWidth )
				{
					childWidth = width;
					childWidthSizingMode = SizingMode.FitContent;
				}
			}

			if ( ( isMainAxisRow && node.Style.Overflow == Overflow.Scroll ) || node.Style.Overflow != Overflow.Scroll )
			{
				if ( Num.IsUndefined( childHeight ) && Num.IsDefined( height ) && !cyclicHeight )
				{
					childHeight = height;
					childHeightSizingMode = SizingMode.FitContent;
				}
			}

			//A ratio with both sides given has nothing to say: the sides win, as CSS Sizing 4 has it.
			if ( Num.IsDefined( child.Style.AspectRatio ) && !( isRowStyleDimDefined && isColumnStyleDimDefined ) )
			{
				if ( !isMainAxisRow && childWidthSizingMode == SizingMode.StretchFit )
				{
					childHeight = marginColumn + ( ( childWidth - marginRow ) / child.Style.AspectRatio );
					childHeightSizingMode = SizingMode.StretchFit;
				}
				else if ( isMainAxisRow && childHeightSizingMode == SizingMode.StretchFit )
				{
					childWidth = marginRow + ( ( childHeight - marginColumn ) * child.Style.AspectRatio );
					childWidthSizingMode = SizingMode.StretchFit;
				}
			}

			//A child with nothing said about its cross size and set to stretch is measured at exactly the room available.
			var hasExactWidth = Num.IsDefined( width ) && widthMode == SizingMode.StretchFit;
			var childWidthStretch = Axis.ResolveChildAlignment( node, child ) == Align.Stretch
				&& childWidthSizingMode != SizingMode.StretchFit;

			if ( !isMainAxisRow && !isRowStyleDimDefined && hasExactWidth && childWidthStretch )
			{
				childWidth = width;
				childWidthSizingMode = SizingMode.StretchFit;

				if ( Num.IsDefined( child.Style.AspectRatio ) && !isColumnStyleDimDefined )
				{
					childHeight = ( childWidth - marginRow ) / child.Style.AspectRatio;
					childHeightSizingMode = SizingMode.StretchFit;
				}
			}

			var hasExactHeight = Num.IsDefined( height ) && heightMode == SizingMode.StretchFit;
			var childHeightStretch = Axis.ResolveChildAlignment( node, child ) == Align.Stretch
				&& childHeightSizingMode != SizingMode.StretchFit;

			if ( isMainAxisRow && !isColumnStyleDimDefined && hasExactHeight && childHeightStretch )
			{
				childHeight = height;
				childHeightSizingMode = SizingMode.StretchFit;

				if ( Num.IsDefined( child.Style.AspectRatio ) && !isRowStyleDimDefined )
				{
					childWidth = ( childHeight - marginColumn ) * child.Style.AspectRatio;
					childWidthSizingMode = SizingMode.StretchFit;
				}
			}

			ConstrainMaxSizeForMode( child, direction, FlexDirection.Row, ownerWidth, ownerWidth, ref childWidthSizingMode, ref childWidth );
			ConstrainMaxSizeForMode( child, direction, FlexDirection.Column, ownerHeight, ownerWidth, ref childHeightSizingMode, ref childHeight );

			//Its own percentages have nothing to resolve against while it is measured as content alone, so the answer is keyed on no owner and given once.
			CalculateLayoutInternal( child, childWidth, childHeight, direction, childWidthSizingMode, childHeightSizingMode,
				cyclicWidth ? Num.Undefined : ownerWidth, cyclicHeight ? Num.Undefined : ownerHeight, false, depth, generationCount );

			child.SetLayoutComputedFlexBasis( Num.MaxOrDefined(
				child.Layout.MeasuredDimension( Axis.DimensionOf( mainAxis ) ),
				Bounds.PaddingAndBorderForAxis( child, mainAxis, direction, ownerWidth ) ) );
		}

		child.Layout.ComputedFlexBasisGeneration = generationCount;
	}

	private static void MeasureNodeWithMeasureFunc( LayoutNode node, Direction direction,
		float availableWidth, float availableHeight, SizingMode widthSizingMode, SizingMode heightSizingMode,
		float ownerWidth, float ownerHeight )
	{
		if ( widthSizingMode == SizingMode.MaxContent ) availableWidth = Num.Undefined;
		if ( heightSizingMode == SizingMode.MaxContent ) availableHeight = Num.Undefined;

		//Measure at most the max width, so the cached answer matches the width it reports
		if ( widthSizingMode == SizingMode.MaxContent )
		{
			var maxWidth = node.Style.ResolvedMaxDimension( direction, Dimension.Width, ownerWidth, ownerWidth );
			if ( Num.IsDefined( maxWidth ) )
			{
				availableWidth = maxWidth;
				widthSizingMode = SizingMode.FitContent;
			}
		}

		if ( heightSizingMode == SizingMode.MaxContent )
		{
			var maxHeight = node.Style.ResolvedMaxDimension( direction, Dimension.Height, ownerHeight, ownerWidth );
			if ( Num.IsDefined( maxHeight ) )
			{
				availableHeight = maxHeight;
				heightSizingMode = SizingMode.FitContent;
			}
		}

		//Clamp the room to the node's own min and max before measuring content, not after
		if ( Num.IsDefined( availableWidth ) )
			availableWidth = Bounds.WithinMinAndMax( node, direction, FlexDirection.Row, availableWidth, ownerWidth, ownerWidth );

		if ( Num.IsDefined( availableHeight ) )
			availableHeight = Bounds.WithinMinAndMax( node, direction, FlexDirection.Column, availableHeight, ownerHeight, ownerWidth );

		ref var layout = ref node.Layout;

		var paddingAndBorderAxisRow = layout.Padding( PhysicalEdge.Left ) + layout.Padding( PhysicalEdge.Right )
			+ layout.Border( PhysicalEdge.Left ) + layout.Border( PhysicalEdge.Right );

		var paddingAndBorderAxisColumn = layout.Padding( PhysicalEdge.Top ) + layout.Padding( PhysicalEdge.Bottom )
			+ layout.Border( PhysicalEdge.Top ) + layout.Border( PhysicalEdge.Bottom );

		//A measure function is never handed a negative size.
		var innerWidth = Num.IsUndefined( availableWidth )
			? availableWidth
			: Num.MaxOrDefined( 0.0f, availableWidth - paddingAndBorderAxisRow );

		var innerHeight = Num.IsUndefined( availableHeight )
			? availableHeight
			: Num.MaxOrDefined( 0.0f, availableHeight - paddingAndBorderAxisColumn );

		if ( widthSizingMode == SizingMode.StretchFit && heightSizingMode == SizingMode.StretchFit )
		{
			//Both sides are settled; there is nothing to ask.
			node.SetLayoutMeasuredDimension(
				Bounds.BoundAxis( node, FlexDirection.Row, direction, availableWidth, ownerWidth, ownerWidth ),
				Dimension.Width );

			node.SetLayoutMeasuredDimension(
				Bounds.BoundAxis( node, FlexDirection.Column, direction, availableHeight, ownerHeight, ownerWidth ),
				Dimension.Height );
		}
		else
		{
			var measured = node.Measure( innerWidth, Axis.ToMeasureMode( widthSizingMode ),
				innerHeight, Axis.ToMeasureMode( heightSizingMode ) );

			node.SetLayoutMeasuredDimension(
				Bounds.BoundAxis( node, FlexDirection.Row, direction,
					widthSizingMode is SizingMode.MaxContent or SizingMode.FitContent
						? measured.Width + paddingAndBorderAxisRow
						: availableWidth,
					ownerWidth, ownerWidth ),
				Dimension.Width );

			node.SetLayoutMeasuredDimension(
				Bounds.BoundAxis( node, FlexDirection.Column, direction,
					heightSizingMode is SizingMode.MaxContent or SizingMode.FitContent
						? measured.Height + paddingAndBorderAxisColumn
						: availableHeight,
					ownerHeight, ownerWidth ),
				Dimension.Height );
		}
	}

	//With nothing inside, a node is the room it was given, or just its own padding and border.
	private static void MeasureNodeWithoutChildren( LayoutNode node, Direction direction,
		float availableWidth, float availableHeight, SizingMode widthSizingMode, SizingMode heightSizingMode,
		float ownerWidth, float ownerHeight )
	{
		ref var layout = ref node.Layout;

		var width = availableWidth;
		if ( widthSizingMode is SizingMode.MaxContent or SizingMode.FitContent )
		{
			width = layout.Padding( PhysicalEdge.Left ) + layout.Padding( PhysicalEdge.Right )
				+ layout.Border( PhysicalEdge.Left ) + layout.Border( PhysicalEdge.Right );
		}

		node.SetLayoutMeasuredDimension(
			Bounds.BoundAxis( node, FlexDirection.Row, direction, width, ownerWidth, ownerWidth ), Dimension.Width );

		var height = availableHeight;
		if ( heightSizingMode is SizingMode.MaxContent or SizingMode.FitContent )
		{
			height = layout.Padding( PhysicalEdge.Top ) + layout.Padding( PhysicalEdge.Bottom )
				+ layout.Border( PhysicalEdge.Top ) + layout.Border( PhysicalEdge.Bottom );
		}

		node.SetLayoutMeasuredDimension(
			Bounds.BoundAxis( node, FlexDirection.Column, direction, height, ownerHeight, ownerWidth ), Dimension.Height );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool IsFixedSize( float dim, SizingMode sizingMode )
		=> sizingMode == SizingMode.StretchFit
			|| ( Num.IsDefined( dim ) && sizingMode == SizingMode.FitContent && dim <= 0.0 );

	//A measure-only pass whose answer is already settled by the constraints: no need to look inside at all.
	private static bool MeasureNodeWithFixedSize( LayoutNode node, Direction direction,
		float availableWidth, float availableHeight, SizingMode widthSizingMode, SizingMode heightSizingMode,
		float ownerWidth, float ownerHeight )
	{
		if ( !IsFixedSize( availableWidth, widthSizingMode ) || !IsFixedSize( availableHeight, heightSizingMode ) )
			return false;

		node.SetLayoutMeasuredDimension(
			Bounds.BoundAxis( node, FlexDirection.Row, direction,
				Num.IsUndefined( availableWidth ) || ( widthSizingMode == SizingMode.FitContent && availableWidth < 0.0f )
					? 0.0f : availableWidth,
				ownerWidth, ownerWidth ),
			Dimension.Width );

		node.SetLayoutMeasuredDimension(
			Bounds.BoundAxis( node, FlexDirection.Column, direction,
				Num.IsUndefined( availableHeight ) || ( heightSizingMode == SizingMode.FitContent && availableHeight < 0.0f )
					? 0.0f : availableHeight,
				ownerHeight, ownerWidth ),
			Dimension.Height );

		return true;
	}

	//A child that is only its contents gets an empty rect at the origin, and so do any contents
	//children of its own; their real children were laid out by the owner and keep their rects.
	private static void ZeroOutContentsChildren( LayoutNode node )
	{
		foreach ( var child in node.RawChildren )
		{
			if ( child.Style.Display != Display.Contents ) continue;

			child.Layout.Reset();
			child.SetLayoutDimension( 0, Dimension.Width );
			child.SetLayoutDimension( 0, Dimension.Height );
			child.Layout.SetMeasuredDimension( Dimension.Width, 0 );
			child.Layout.SetMeasuredDimension( Dimension.Height, 0 );
			child.HasNewLayout = true;
			child.SetDirty( false );

			if ( child.HasContentsChildren ) ZeroOutContentsChildren( child );
		}
	}

	//A subtree that is not displayed still gets a layout, and it is all noughts.
	private static void ZeroOutLayoutRecursively( LayoutNode node )
	{
		node.Layout.Reset();
		node.SetLayoutDimension( 0, Dimension.Width );
		node.SetLayoutDimension( 0, Dimension.Height );
		//Measured as well as laid out: a baseline walk may still read a hidden node.
		node.Layout.SetMeasuredDimension( Dimension.Width, 0 );
		node.Layout.SetMeasuredDimension( Dimension.Height, 0 );
		node.HasNewLayout = true;

		foreach ( var child in node.RawChildren ) ZeroOutLayoutRecursively( child );
	}

	//The room inside a box once its own padding and border are taken off, held within whatever minimum and maximum the author set.
	private static float CalculateAvailableInnerDimension( LayoutNode node, Direction direction, Dimension dimension,
		float availableDim, float paddingAndBorder, float ownerDim, float ownerWidth )
	{
		var availableInnerDim = availableDim - paddingAndBorder;

		if ( Num.IsDefined( availableInnerDim ) )
		{
			var min = node.Style.ResolvedMinDimension( direction, dimension, ownerDim, ownerWidth );
			var minInnerDim = Num.IsUndefined( min ) ? 0.0f : min - paddingAndBorder;

			var max = node.Style.ResolvedMaxDimension( direction, dimension, ownerDim, ownerWidth );
			var maxInnerDim = Num.IsUndefined( max ) ? float.MaxValue : max - paddingAndBorder;

			availableInnerDim = Num.MaxOrDefined( Num.MinOrDefined( availableInnerDim, maxInnerDim ), minInnerDim );
		}

		return availableInnerDim;
	}

	private static float ComputeFlexBasisForChildren( LayoutNode node, float availableInnerWidth, float availableInnerHeight,
		float ownerWidth, float ownerHeight, SizingMode widthSizingMode, SizingMode heightSizingMode,
		Direction direction, FlexDirection mainAxis, bool performLayout, int depth, uint generationCount )
	{
		var totalOuterFlexBasis = 0.0f;
		LayoutNode singleFlexChild = null;

		var children = node.LayoutChildren;
		var sizingModeMainDim = Axis.IsRow( mainAxis ) ? widthSizingMode : heightSizingMode;

		//One child that both grows and shrinks against a settled main size will end up
		//filling whatever is left, so its basis can be taken as nought rather than measured.
		if ( sizingModeMainDim == SizingMode.StretchFit )
		{
			foreach ( var child in children )
			{
				if ( !child.IsNodeFlexible() ) continue;

				if ( singleFlexChild is not null
					|| Num.InexactEquals( child.ResolveFlexGrow(), 0.0f )
					|| Num.InexactEquals( child.ResolveFlexShrink(), 0.0f ) )
				{
					singleFlexChild = null;
					break;
				}

				singleFlexChild = child;
			}
		}

		foreach ( var child in children )
		{
			child.ProcessDimensions();

			if ( child.Style.Display == Display.None )
			{
				//Only zeroed on a real layout pass. Doing it while merely measuring marks a node as having a new
				//layout that the owner's own layout pass may never visit, and the flag then outlives the pass.
				if ( performLayout )
				{
					ZeroOutLayoutRecursively( child );
					child.HasNewLayout = true;
					child.SetDirty( false );
				}

				continue;
			}

			if ( performLayout )
			{
				var childDirection = child.ResolveDirection( direction );
				child.SetPosition( childDirection, availableInnerWidth, availableInnerHeight );
			}

			if ( child.IsOutOfFlow ) continue;

			if ( ReferenceEquals( child, singleFlexChild ) )
			{
				child.Layout.ComputedFlexBasisGeneration = generationCount;
				child.SetLayoutComputedFlexBasis( 0 );
			}
			else
			{
				ComputeFlexBasisForChild( node, child, availableInnerWidth, widthSizingMode, availableInnerHeight,
					ownerWidth, ownerHeight, heightSizingMode, direction, depth, generationCount );
			}

			totalOuterFlexBasis += child.Layout.ComputedFlexBasis
				+ child.Style.ComputeMarginForAxis( mainAxis, availableInnerWidth );
		}

		return totalOuterFlexBasis;
	}

	//-- handing out the room that is left ------------------------------------------------

	//Settles every item's main size along the line, then lays each item out at that size, which is also where its cross size is found.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static void ResolveFlexibleLength( LayoutNode node, FlexLine flexLine, FlexDirection mainAxis,
		FlexDirection crossAxis, Direction direction, float ownerWidth, float childMainReference,
		float availableInnerMainDim, float availableInnerCrossDim, float availableInnerWidth, float availableInnerHeight,
		bool mainAxisOverflows, SizingMode sizingModeCrossDim, bool performLayout, int depth, uint generationCount )
	{
		var originalFreeSpace = flexLine.Layout.RemainingFreeSpace;

		SettleMainSizes( flexLine, direction, mainAxis, childMainReference, availableInnerMainDim, availableInnerWidth );

		var distributedFreeSpace = LayoutLineItems( flexLine, node, mainAxis, crossAxis, direction, childMainReference,
			availableInnerMainDim, availableInnerCrossDim, availableInnerWidth, availableInnerHeight,
			mainAxisOverflows, sizingModeCrossDim, performLayout, depth, generationCount );

		flexLine.Layout.RemainingFreeSpace = originalFreeSpace - distributedFreeSpace;
	}

	//CSS Flexbox 9.7: resolves each item's main size into TargetMainSize, freezing clamped items
	private static void SettleMainSizes( FlexLine line, Direction direction, FlexDirection mainAxis,
		float childMainReference, float availableInnerMainDim, float availableInnerWidth )
	{
		var items = line.ItemsInFlow;
		var targets = line.TargetMainSize;
		var frozen = line.Frozen;
		var tentative = line.Scratch;
		var innerBasis = line.InnerBasis;
		targets.Clear();
		frozen.Clear();
		tentative.Clear();
		innerBasis.Clear();

		var free = line.Layout.RemainingFreeSpace;
		var growing = Num.IsDefined( free ) && free > 0;
		var shrinking = Num.IsDefined( free ) && free < 0;

		//The scaled shrink factor is the factor by the inner flex base size, the basis less padding and border (CSS Flexbox 9.7); scaling by the
		//outer size, as the original did, hands a padded box more of the shortfall than its content can give up. Looked up once per item here.
		for ( var i = 0; i < items.Count; i++ )
			innerBasis.Add( shrinking ? InnerBasis( items[i], mainAxis, direction, availableInnerWidth ) : 0.0f );

		//Hypothetical sizes first. With no room to hand out either way, that is the answer.
		var unfrozen = 0;
		var hypotheticalSum = 0.0f;
		for ( var i = 0; i < items.Count; i++ )
		{
			var item = items[i];
			var basis = item.Layout.ComputedFlexBasis;
			var hypothetical = Bounds.WithinMinAndMax( item, direction, mainAxis, basis, childMainReference, availableInnerWidth );
			hypotheticalSum += hypothetical;

			var factor = growing ? item.ResolveFlexGrow() : shrinking ? item.ResolveFlexShrink() : 0.0f;
			var freeze = !(growing || shrinking)
				|| Num.IsUndefined( factor ) || factor == 0
				|| (growing && basis > hypothetical)
				|| (shrinking && basis < hypothetical);

			targets.Add( freeze ? hypothetical : basis );
			frozen.Add( freeze );
			tentative.Add( hypothetical );
			if ( !freeze ) unfrozen++;
		}

		if ( unfrozen == 0 ) return;

		//Margins and gaps take the same room whatever the items settle to: what the line consumed beyond the hypothetical sizes themselves.
		var overhead = line.SizeConsumed - hypotheticalSum;

		float FreeSpace()
		{
			var used = overhead;
			for ( var i = 0; i < items.Count; i++ ) used += targets[i];
			return availableInnerMainDim - used;
		}

		var initialFree = FreeSpace();

		for ( var round = 0; round <= items.Count && unfrozen > 0; round++ )
		{
			var remaining = FreeSpace();

			var sumFactors = 0.0f;
			var sumScaled = 0.0f;
			for ( var i = 0; i < items.Count; i++ )
			{
				if ( frozen[i] ) continue;
				var factor = growing ? items[i].ResolveFlexGrow() : items[i].ResolveFlexShrink();
				sumFactors += factor;
				sumScaled += factor * innerBasis[i];
			}

			//Factors that add up to less than one only claim that fraction of the room.
			if ( sumFactors < 1 )
			{
				var limited = initialFree * sumFactors;
				if ( MathF.Abs( limited ) < MathF.Abs( remaining ) ) remaining = limited;
			}

			var totalViolation = 0.0f;
			for ( var i = 0; i < items.Count; i++ )
			{
				if ( frozen[i] ) continue;

				var item = items[i];
				var basis = item.Layout.ComputedFlexBasis;
				var factor = growing ? item.ResolveFlexGrow() : item.ResolveFlexShrink();

				float target;
				if ( growing )
					target = basis + remaining * factor / sumFactors;
				else if ( sumScaled > 0 )
					target = basis - MathF.Abs( remaining ) * (factor * innerBasis[i]) / sumScaled;
				else
					target = basis;

				var clamped = Bounds.BoundAxis( item, mainAxis, direction, target, childMainReference, availableInnerWidth );
				tentative[i] = clamped;
				totalViolation += clamped - target;
			}

			//Freeze whichever clamps fired in the direction that dominates; with none, everything is settled.
			for ( var i = 0; i < items.Count; i++ )
			{
				if ( frozen[i] ) continue;

				var item = items[i];
				var basis = item.Layout.ComputedFlexBasis;
				var factor = growing ? item.ResolveFlexGrow() : item.ResolveFlexShrink();
				var unclamped = growing
					? basis + remaining * factor / sumFactors
					: sumScaled > 0 ? basis - MathF.Abs( remaining ) * (factor * innerBasis[i]) / sumScaled : basis;
				var violation = tentative[i] - unclamped;

				var settle = totalViolation == 0
					|| (totalViolation > 0 && violation > 0)
					|| (totalViolation < 0 && violation < 0);
				if ( !settle ) continue;

				targets[i] = tentative[i];
				frozen[i] = true;
				unfrozen--;
			}
		}

		//Anything the round cap left unfrozen takes its last clamped share.
		for ( var i = 0; i < items.Count; i++ )
		{
			if ( !frozen[i] ) targets[i] = tentative[i];
		}
	}

	//The inner flex base size: the basis less padding and border.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static float InnerBasis( LayoutNode item, FlexDirection mainAxis, Direction direction, float availableInnerWidth )
		=> MathF.Max( 0.0f, item.Layout.ComputedFlexBasis - Bounds.PaddingAndBorderForAxis( item, mainAxis, direction, availableInnerWidth ) );

	//Lays every item out at its settled main size. Returns how much the settled sizes
	//differ from the hypothetical ones, which is what the line's free space shrinks by.
	private static float LayoutLineItems( FlexLine flexLine, LayoutNode node, FlexDirection mainAxis,
		FlexDirection crossAxis, Direction direction, float childMainReference,
		float availableInnerMainDim, float availableInnerCrossDim, float availableInnerWidth, float availableInnerHeight,
		bool mainAxisOverflows, SizingMode sizingModeCrossDim, bool performLayout, int depth, uint generationCount )
	{
		var deltaFreeSpace = 0.0f;
		var isMainAxisRow = Axis.IsRow( mainAxis );
		var isNodeFlexWrap = node.Style.FlexWrap != Wrap.NoWrap;
		var items = flexLine.ItemsInFlow;

		for ( var i = 0; i < items.Count; i++ )
		{
			var currentLineChild = items[i];
			var hypothetical = Bounds.WithinMinAndMax( currentLineChild, direction, mainAxis,
				currentLineChild.Layout.ComputedFlexBasis, childMainReference, availableInnerWidth );
			var updatedMainSize = flexLine.TargetMainSize[i];

			deltaFreeSpace += updatedMainSize - hypothetical;

			var marginMain = currentLineChild.Style.ComputeMarginForAxis( mainAxis, availableInnerWidth );
			var marginCross = currentLineChild.Style.ComputeMarginForAxis( crossAxis, availableInnerWidth );

			float childCrossSize;
			var childMainSize = updatedMainSize + marginMain;
			SizingMode childCrossSizingMode;
			var childMainSizingMode = SizingMode.StretchFit;

			//A stretch makes the cross size definite before the ratio gets a say, and a ratio with
			//both sides given has none; CSS Sizing 4 orders them that way and the original did not.
			var stretches = !float.IsNaN( availableInnerCrossDim )
				&& !currentLineChild.HasDefiniteLength( Axis.DimensionOf( crossAxis ), availableInnerCrossDim )
				&& sizingModeCrossDim == SizingMode.StretchFit
				&& !(isNodeFlexWrap && mainAxisOverflows)
				&& Axis.ResolveChildAlignment( node, currentLineChild ) == Align.Stretch
				&& !currentLineChild.Style.FlexStartMarginIsAuto( crossAxis, direction )
				&& !currentLineChild.Style.FlexEndMarginIsAuto( crossAxis, direction );

			var ratioApplies = Num.IsDefined( currentLineChild.Style.AspectRatio )
				&& !stretches
				&& !( currentLineChild.HasDefiniteLength( Axis.DimensionOf( mainAxis ), availableInnerMainDim )
					&& currentLineChild.HasDefiniteLength( Axis.DimensionOf( crossAxis ), availableInnerCrossDim ) );

			if ( ratioApplies )
			{
				childCrossSize = isMainAxisRow
					? (childMainSize - marginMain) / currentLineChild.Style.AspectRatio
					: (childMainSize - marginMain) * currentLineChild.Style.AspectRatio;

				childCrossSizingMode = SizingMode.StretchFit;
				childCrossSize += marginCross;
			}
			else if ( stretches )
			{
				childCrossSize = availableInnerCrossDim;
				childCrossSizingMode = SizingMode.StretchFit;
			}
			else if ( !currentLineChild.HasDefiniteLength( Axis.DimensionOf( crossAxis ), availableInnerCrossDim ) )
			{
				childCrossSize = availableInnerCrossDim;
				childCrossSizingMode = Num.IsUndefined( childCrossSize ) ? SizingMode.MaxContent : SizingMode.FitContent;
			}
			else
			{
				childCrossSize = currentLineChild.GetResolvedDimension( direction, Axis.DimensionOf( crossAxis ),
					availableInnerCrossDim, availableInnerWidth ) + marginCross;

				//A percentage measured against something that is not itself settled is not a definite size, so it is measured rather than taken at face value.
				var isLoosePercentageMeasurement =
					currentLineChild.ProcessedDimension( Axis.DimensionOf( crossAxis ) ).IsPercent
					&& sizingModeCrossDim != SizingMode.StretchFit;

				childCrossSizingMode = Num.IsUndefined( childCrossSize ) || isLoosePercentageMeasurement
					? SizingMode.MaxContent
					: SizingMode.StretchFit;
			}

			ConstrainMaxSizeForMode( currentLineChild, direction, mainAxis, availableInnerMainDim, availableInnerWidth,
				ref childMainSizingMode, ref childMainSize );

			ConstrainMaxSizeForMode( currentLineChild, direction, crossAxis, availableInnerCrossDim, availableInnerWidth,
				ref childCrossSizingMode, ref childCrossSize );

			var requiresStretchLayout =
				!currentLineChild.HasDefiniteLength( Axis.DimensionOf( crossAxis ), availableInnerCrossDim )
				&& Axis.ResolveChildAlignment( node, currentLineChild ) == Align.Stretch
				&& !currentLineChild.Style.FlexStartMarginIsAuto( crossAxis, direction )
				&& !currentLineChild.Style.FlexEndMarginIsAuto( crossAxis, direction );

			var childWidth = isMainAxisRow ? childMainSize : childCrossSize;
			var childHeight = !isMainAxisRow ? childMainSize : childCrossSize;

			var childWidthSizingMode = isMainAxisRow ? childMainSizingMode : childCrossSizingMode;
			var childHeightSizingMode = !isMainAxisRow ? childMainSizingMode : childCrossSizingMode;

			//A stretched child is laid out again later at the line's own cross size, so this pass only measures it.
			var isLayoutPass = performLayout && !requiresStretchLayout;

			CalculateLayoutInternal( currentLineChild, childWidth, childHeight, node.Layout.Direction,
				childWidthSizingMode, childHeightSizingMode, availableInnerWidth, availableInnerHeight,
				isLayoutPass, depth, generationCount );

			node.Layout.HadOverflow = node.Layout.HadOverflow || currentLineChild.Layout.HadOverflow;
		}

		return deltaFreeSpace;
	}

	//Spreads the items along the main axis and works out how thick the line is across.
	private static void JustifyMainAxis( LayoutNode node, FlexLine flexLine, FlexDirection mainAxis, FlexDirection crossAxis,
		Direction direction, SizingMode sizingModeMainDim, SizingMode sizingModeCrossDim, float mainAxisOwnerSize,
		float ownerWidth, float childMainReference, float availableInnerMainDim, float availableInnerCrossDim, float availableInnerWidth,
		float availableInnerHeight, bool performLayout, int depth, uint generationCount )
	{
		var style = node.Style;

		var leadingPaddingAndBorderMain = style.ComputeFlexStartPaddingAndBorder( mainAxis, direction, ownerWidth );
		var trailingPaddingAndBorderMain = style.ComputeFlexEndPaddingAndBorder( mainAxis, direction, ownerWidth );

		var gap = style.ComputeGapForAxis( mainAxis, availableInnerMainDim );

		//Under "at most" rules there is no free space to spread unless a minimum pushes the box back out.
		if ( sizingModeMainDim == SizingMode.FitContent && flexLine.Layout.RemainingFreeSpace > 0 )
		{
			var min = style.ResolvedMinDimension( direction, Axis.DimensionOf( mainAxis ), mainAxisOwnerSize, ownerWidth );

			if ( style.MinDimension( Axis.DimensionOf( mainAxis ) ).IsDefined && Num.IsDefined( min ) )
			{
				var minAvailableMainDim = min - leadingPaddingAndBorderMain - trailingPaddingAndBorderMain;
				var occupiedSpaceByChildNodes = availableInnerMainDim - flexLine.Layout.RemainingFreeSpace;

				flexLine.Layout.RemainingFreeSpace = Num.MaxOrDefined( 0.0f, minAvailableMainDim - occupiedSpaceByChildNodes );
			}
			else
			{
				flexLine.Layout.RemainingFreeSpace = 0;
			}
		}

		//Two knobs place the items: what goes before the first, and what goes between each pair.
		var leadingMainDim = 0.0f;
		var betweenMainDim = gap;

		var justifyContent = flexLine.Layout.RemainingFreeSpace >= 0
			? style.JustifyContent
			: Axis.Fallback( style.JustifyContent, mainAxis, direction );

		//An automatic margin eats the free space, so nothing is left to distribute by justification.
		if ( flexLine.NumberOfAutoMargins == 0 )
		{
			switch ( justifyContent )
			{
				case Justify.Start:
				case Justify.End:
				case Justify.Stretch:
				case Justify.FlexStart:
					break;

				case Justify.Center:
					leadingMainDim = flexLine.Layout.RemainingFreeSpace / 2;
					break;

				case Justify.FlexEnd:
					leadingMainDim = flexLine.Layout.RemainingFreeSpace;
					break;

				case Justify.SpaceBetween:
					if ( flexLine.ItemsInFlow.Count > 1 )
						betweenMainDim += flexLine.Layout.RemainingFreeSpace / ( flexLine.ItemsInFlow.Count - 1 );
					break;

				case Justify.SpaceEvenly:
					leadingMainDim = flexLine.Layout.RemainingFreeSpace / ( flexLine.ItemsInFlow.Count + 1 );
					betweenMainDim += leadingMainDim;
					break;

				case Justify.SpaceAround:
					//Nothing on the line means nothing to put space around; the division would be by zero.
					leadingMainDim = flexLine.ItemsInFlow.Count > 0 ? 0.5f * flexLine.Layout.RemainingFreeSpace / flexLine.ItemsInFlow.Count : 0;
					betweenMainDim += leadingMainDim * 2;
					break;
			}
		}

		flexLine.Layout.MainDim = leadingPaddingAndBorderMain + leadingMainDim;
		flexLine.Layout.CrossDim = 0;

		var maxAscentForCurrentLine = 0.0f;
		var maxDescentForCurrentLine = 0.0f;
		var isNodeBaselineLayout = Baseline.IsBaselineLayout( node );

		var last = flexLine.ItemsInFlow.Count > 0 ? flexLine.ItemsInFlow[^1] : null;


		foreach ( var child in flexLine.ItemsInFlow )
		{
			ref var childLayout = ref child.Layout;

			if ( child.Style.FlexStartMarginIsAuto( mainAxis, direction ) && flexLine.Layout.RemainingFreeSpace > 0.0f )
				flexLine.Layout.MainDim += flexLine.Layout.RemainingFreeSpace / flexLine.NumberOfAutoMargins;


			if ( performLayout )
			{
				child.Layout.SetPosition( Axis.FlexStartEdge( mainAxis ),
					childLayout.Position( Axis.FlexStartEdge( mainAxis ) ) + flexLine.Layout.MainDim );
			}

			if ( !ReferenceEquals( child, last ) ) flexLine.Layout.MainDim += betweenMainDim;

			if ( child.Style.FlexEndMarginIsAuto( mainAxis, direction ) && flexLine.Layout.RemainingFreeSpace > 0.0f )
				flexLine.Layout.MainDim += flexLine.Layout.RemainingFreeSpace / flexLine.NumberOfAutoMargins;

			var canSkipFlex = !performLayout && sizingModeCrossDim == SizingMode.StretchFit;

			if ( canSkipFlex )
			{
				//The flexing step was skipped, so the measured sizes were never worked out and cannot be asked for.
				flexLine.Layout.MainDim += child.Style.ComputeMarginForAxis( mainAxis, availableInnerWidth )
					+ Bounds.WithinMinAndMax( child, direction, mainAxis, childLayout.ComputedFlexBasis,
						childMainReference, availableInnerWidth );

				flexLine.Layout.CrossDim = availableInnerCrossDim;
			}
			else
			{
				flexLine.Layout.MainDim += child.DimensionWithMargin( mainAxis, availableInnerWidth );

				if ( isNodeBaselineLayout )
				{
					//Measuring lays the child out for its baseline, then restores its subtree to the last real layout
					var restore = CachedMeasurement.Empty;
					var measuredWidth = 0.0f;
					var measuredHeight = 0.0f;
					var laidOutForBaseline = false;

					if ( !performLayout && !child.HasMeasureFunc && child.ChildCount > 0 )
					{
						restore = child.Layout.CachedLayout;
						measuredWidth = child.Layout.MeasuredDimension( Dimension.Width );
						measuredHeight = child.Layout.MeasuredDimension( Dimension.Height );
						laidOutForBaseline = true;

						CalculateLayoutInternal( child,
							measuredWidth + child.Style.ComputeMarginForAxis( FlexDirection.Row, availableInnerWidth ),
							measuredHeight + child.Style.ComputeMarginForAxis( FlexDirection.Column, availableInnerWidth ),
							node.Layout.Direction, SizingMode.StretchFit, SizingMode.StretchFit, availableInnerWidth, availableInnerHeight,
							true, depth, generationCount );
					}

					//Lined up on a baseline, the line is as thick as the furthest rise above it plus the furthest drop below.
					var ascent = Baseline.Calculate( child )
						+ child.Style.ComputeFlexStartMargin( FlexDirection.Column, direction, availableInnerWidth );

					var descent = child.Layout.MeasuredDimension( Dimension.Height )
						+ child.Style.ComputeMarginForAxis( FlexDirection.Column, availableInnerWidth ) - ascent;

					if ( laidOutForBaseline && restore.ComputedWidth >= 0
						&& !( Num.InexactEquals( restore.AvailableWidth, measuredWidth + child.Style.ComputeMarginForAxis( FlexDirection.Row, availableInnerWidth ) )
							&& Num.InexactEquals( restore.AvailableHeight, measuredHeight + child.Style.ComputeMarginForAxis( FlexDirection.Column, availableInnerWidth ) )
							&& restore.WidthSizingMode == SizingMode.StretchFit && restore.HeightSizingMode == SizingMode.StretchFit ) )
					{
						var baseline = child.Layout.Baseline;

						CalculateLayoutInternal( child, restore.AvailableWidth, restore.AvailableHeight, child.Layout.LastOwnerDirection,
							restore.WidthSizingMode, restore.HeightSizingMode, restore.OwnerWidth, restore.OwnerHeight,
							true, depth, generationCount );

						//This measurement's own answers stay: the size it came out as, and its baseline at it.
						child.Layout.SetMeasuredDimension( Dimension.Width, measuredWidth );
						child.Layout.SetMeasuredDimension( Dimension.Height, measuredHeight );
						child.Layout.Baseline = baseline;
					}

					maxAscentForCurrentLine = Num.MaxOrDefined( maxAscentForCurrentLine, ascent );
					maxDescentForCurrentLine = Num.MaxOrDefined( maxDescentForCurrentLine, descent );
				}
				else
				{
					flexLine.Layout.CrossDim = Num.MaxOrDefined( flexLine.Layout.CrossDim,
						child.DimensionWithMargin( crossAxis, availableInnerWidth ) );
				}
			}
		}

		flexLine.Layout.MainDim += trailingPaddingAndBorderMain;

		if ( isNodeBaselineLayout )
			flexLine.Layout.CrossDim = maxAscentForCurrentLine + maxDescentForCurrentLine;

	}
}
