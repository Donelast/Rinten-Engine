//Frozen phase-7 copy of Rinten.Layout (Yoga port, MIT) for differential tests; do not edit
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Rinten.Layout.Reference;

//One track of a grid while it is being sized: its sizing function, its base size and growth limit, and where it ends up.
internal struct GridTrack
{
	public GridTrackSize Size;
	public float Base;
	//Undefined means unlimited.
	public float Limit;
	public float Offset;
	public float Planned;
	public bool Frozen;
	public bool Explicit;
	public bool Repeated;
	public bool Collapsed;

}

//One item of a grid while it is being placed and sized: its node, its lines, and the contributions it has been asked for.
internal struct GridItem
{
	public LayoutNode Node;
	public int ColumnStart, ColumnEnd, RowStart, RowEnd;
	public bool ColumnDefinite, RowDefinite;
	public int ColumnSpan, RowSpan;
	public float MinContentWidth, MaxContentWidth;
	public bool WidthsMeasured;
	public float Height;
	public bool HeightMeasured;
}

//The lists a grid layout works in, rented per layout and cleared, never allocated per pass.
internal sealed class GridScratch
{
	public readonly List<GridItem> Items = new();
	public readonly List<GridTrack> Columns = new();
	public readonly List<GridTrack> Rows = new();
	public readonly HashSet<long> Occupied = new();
	public readonly List<int> Cursors = new();

	public void Reset()
	{
		Items.Clear();
		Columns.Clear();
		Rows.Clear();
		Occupied.Clear();
		Cursors.Clear();
	}
}

internal static class GridScratchPool
{
	[ThreadStatic] private static Stack<GridScratch> pool;

	public static GridScratch Rent()
	{
		pool ??= new Stack<GridScratch>();
		return pool.Count > 0 ? pool.Pop() : new GridScratch();
	}

	public static void Return( GridScratch scratch )
	{
		scratch.Reset();
		pool.Push( scratch );
	}
}

//CSS Grid Level 1 layout (sections 7, 8, 10.5, 11); no line names, areas, subgrid or order
internal static partial class FlexLayout
{
	private static void LayoutGrid( LayoutNode node, float availableWidth, float availableHeight, Direction direction,
		SizingMode widthSizingMode, SizingMode heightSizingMode, float ownerWidth, float ownerHeight,
		bool performLayout, int depth, uint generationCount, float marginAxisRow, float marginAxisColumn )
	{
		ref var layout = ref node.Layout;
		layout.HadOverflow = false;

		var style = node.Style;
		var paddingAndBorderAxisRow = Bounds.PaddingAndBorderForAxis( node, FlexDirection.Row, direction, ownerWidth );
		var paddingAndBorderAxisColumn = Bounds.PaddingAndBorderForAxis( node, FlexDirection.Column, direction, ownerWidth );

		var availableInnerWidth = CalculateAvailableInnerDimension( node, direction, Dimension.Width,
			availableWidth - marginAxisRow, paddingAndBorderAxisRow, ownerWidth, ownerWidth );

		var availableInnerHeight = CalculateAvailableInnerDimension( node, direction, Dimension.Height,
			availableHeight - marginAxisColumn, paddingAndBorderAxisColumn, ownerHeight, ownerWidth );

		var contentLeft = layout.Padding( PhysicalEdge.Left ) + layout.Border( PhysicalEdge.Left );
		var contentTop = layout.Padding( PhysicalEdge.Top ) + layout.Border( PhysicalEdge.Top );

		//The room the tracks share: the inner size where it is settled, a limit where it is one.
		var columnRoom = widthSizingMode == SizingMode.MaxContent ? Num.Undefined : availableInnerWidth;
		var rowRoom = heightSizingMode == SizingMode.MaxContent ? Num.Undefined : availableInnerHeight;
		var columnGap = GapOrZero( style.ComputeGapForAxis( FlexDirection.Row, availableInnerWidth ) );
		var rowGap = GapOrZero( style.ComputeGapForAxis( FlexDirection.Column, availableInnerHeight ) );

		var scratch = GridScratchPool.Rent();

		try
		{
			var items = scratch.Items;
			var columns = scratch.Columns;
			var rows = scratch.Rows;

			foreach ( var child in node.LayoutChildren )
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
					//The static position of a box out of the flow is the start of the padding box:
					//with no grid lines of its own, that box is what contains it (CSS Grid 9.1).
					if ( performLayout )
					{
						child.Layout.SetPosition( PhysicalEdge.Left, layout.Border( PhysicalEdge.Left ) );
						child.Layout.SetPosition( PhysicalEdge.Top, layout.Border( PhysicalEdge.Top ) );
					}

					continue;
				}

				items.Add( new GridItem { Node = child } );
			}

			//-- the grid -------------------------------------------------------------------

			BuildExplicitTracks( columns, style.GridTemplateColumns, columnRoom, columnGap );
			BuildExplicitTracks( rows, style.GridTemplateRows, rowRoom, rowGap );

			var explicitColumns = columns.Count;
			var explicitRows = rows.Count;

			var flow = style.GridAutoFlow;
			var columnFlow = flow is GridAutoFlow.Column or GridAutoFlow.ColumnDense;
			var dense = flow is GridAutoFlow.RowDense or GridAutoFlow.ColumnDense;

			PlaceItems( scratch, explicitColumns, explicitRows, columnFlow, dense );

			//Lines beyond the explicit grid, on either side, are implicit tracks.
			var minColumn = 0;
			var maxColumn = Math.Max( explicitColumns, 1 );
			var minRow = 0;
			var maxRow = Math.Max( explicitRows, 1 );

			for ( var i = 0; i < items.Count; i++ )
			{
				var item = items[i];
				minColumn = Math.Min( minColumn, item.ColumnStart );
				maxColumn = Math.Max( maxColumn, item.ColumnEnd );
				minRow = Math.Min( minRow, item.RowStart );
				maxRow = Math.Max( maxRow, item.RowEnd );
			}

			if ( minColumn < 0 || maxColumn > columns.Count ) AddImplicitTracks( columns, -minColumn, maxColumn - columns.Count, style.GridAutoColumns );
			if ( minRow < 0 || maxRow > rows.Count ) AddImplicitTracks( rows, -minRow, maxRow - rows.Count, style.GridAutoRows );

			if ( minColumn < 0 || minRow < 0 )
			{
				for ( var i = 0; i < items.Count; i++ )
				{
					var item = items[i];
					item.ColumnStart -= minColumn;
					item.ColumnEnd -= minColumn;
					item.RowStart -= minRow;
					item.RowEnd -= minRow;
					items[i] = item;
				}
			}

			//An auto-fit repeat collapses the repeated tracks nothing landed in.
			if ( style.GridTemplateColumns.Repeat == GridRepeat.AutoFit ) CollapseEmptyRepeatedTracks( columns, items, true );
			if ( style.GridTemplateRows.Repeat == GridRepeat.AutoFit ) CollapseEmptyRepeatedTracks( rows, items, false );

			//-- the tracks -----------------------------------------------------------------

			//Under a limit the box is sized by its contents: the tracks take their content sizes,
			//and only where those overrun the limit is the limit shared out as a settled size.
			SizeTracks( scratch, true, widthSizingMode == SizingMode.StretchFit ? columnRoom : Num.Undefined, widthSizingMode, columnGap,
				direction, depth, generationCount, JustifyKind( style.JustifyContent ), availableInnerWidth, availableInnerHeight );

			var gridWidth = TotalSize( columns, columnGap );

			if ( widthSizingMode == SizingMode.FitContent && Num.IsDefined( columnRoom ) && gridWidth > columnRoom )
			{
				for ( var i = 0; i < items.Count; i++ ) { var item = items[i]; item.HeightMeasured = false; items[i] = item; }
				SizeTracks( scratch, true, columnRoom, SizingMode.StretchFit, columnGap, direction, depth, generationCount,
					JustifyKind( style.JustifyContent ), availableInnerWidth, availableInnerHeight );
				gridWidth = TotalSize( columns, columnGap );
			}

			SizeTracks( scratch, false, heightSizingMode == SizingMode.StretchFit ? rowRoom : Num.Undefined, heightSizingMode, rowGap,
				direction, depth, generationCount, AlignKind( style.AlignContent ), availableInnerWidth, availableInnerHeight );

			var gridHeight = TotalSize( rows, rowGap );

			if ( heightSizingMode == SizingMode.FitContent && Num.IsDefined( rowRoom ) && gridHeight > rowRoom )
			{
				SizeTracks( scratch, false, rowRoom, SizingMode.StretchFit, rowGap, direction, depth, generationCount,
					AlignKind( style.AlignContent ), availableInnerWidth, availableInnerHeight );
				gridHeight = TotalSize( rows, rowGap );
			}

			//The inner width the box ends up with, which a right-to-left grid lays its columns from the far side of.
			var finalInnerWidth = Bounds.BoundAxis( node, FlexDirection.Row, direction,
				widthSizingMode == SizingMode.StretchFit ? availableWidth - marginAxisRow : gridWidth + paddingAndBorderAxisRow,
				ownerWidth, ownerWidth ) - paddingAndBorderAxisRow;

			//-- the items ------------------------------------------------------------------

			for ( var i = 0; i < items.Count; i++ )
			{
				var item = items[i];
				var child = item.Node;
				child.LineIndex = item.RowStart;

				var areaLeft = columns[item.ColumnStart].Offset;
				var areaWidth = SpanSize( columns, item.ColumnStart, item.ColumnEnd, columnGap );
				var areaTop = rows[item.RowStart].Offset;
				var areaHeight = SpanSize( rows, item.RowStart, item.RowEnd, rowGap );

				var justify = ResolveItemAlignment( child.Style.JustifySelf, style.JustifyItems );
				var align = ResolveItemAlignment( child.Style.AlignSelf, style.AlignItems );

				var childWidth = ItemSize( child, direction, FlexDirection.Row, areaWidth, justify, ref item, depth, generationCount, areaHeight );
				var childHeight = ItemSize( child, direction, FlexDirection.Column, areaHeight, align, ref item, depth, generationCount, childWidth );

				var marginRow = child.Style.ComputeMarginForAxis( FlexDirection.Row, areaWidth );
				var marginColumn = child.Style.ComputeMarginForAxis( FlexDirection.Column, areaWidth );

				CalculateLayoutInternal( child, childWidth + marginRow, childHeight + marginColumn, direction,
					SizingMode.StretchFit, SizingMode.StretchFit, areaWidth, areaHeight, performLayout, depth, generationCount );

				if ( performLayout )
				{
					var x = areaLeft + Place( child, direction, FlexDirection.Row, areaWidth, child.Layout.MeasuredDimension( Dimension.Width ), justify );
					var y = areaTop + Place( child, direction, FlexDirection.Column, areaHeight, child.Layout.MeasuredDimension( Dimension.Height ), align );

					if ( direction == Direction.RTL )
						x = finalInnerWidth - x - child.Layout.MeasuredDimension( Dimension.Width );

					child.Layout.SetPosition( PhysicalEdge.Left, contentLeft + x + child.RelativePosition( FlexDirection.Row, direction, availableInnerWidth ) );
					child.Layout.SetPosition( PhysicalEdge.Top, contentTop + y + child.RelativePosition( FlexDirection.Column, direction, availableInnerHeight ) );
				}

				items[i] = item;
			}

			//-- the sizes ------------------------------------------------------------------

			if ( Num.IsDefined( availableInnerWidth ) && widthSizingMode != SizingMode.MaxContent && gridWidth > availableInnerWidth + 0.01f )
				layout.HadOverflow = true;

			if ( Num.IsDefined( availableInnerHeight ) && heightSizingMode != SizingMode.MaxContent && gridHeight > availableInnerHeight + 0.01f )
				layout.HadOverflow = true;

			node.SetLayoutMeasuredDimension(
				Bounds.BoundAxis( node, FlexDirection.Row, direction,
					widthSizingMode == SizingMode.StretchFit ? availableWidth - marginAxisRow : gridWidth + paddingAndBorderAxisRow,
					ownerWidth, ownerWidth ),
				Dimension.Width );

			node.SetLayoutMeasuredDimension(
				Bounds.BoundAxis( node, FlexDirection.Column, direction,
					heightSizingMode == SizingMode.StretchFit ? availableHeight - marginAxisColumn : gridHeight + paddingAndBorderAxisColumn,
					ownerHeight, ownerWidth ),
				Dimension.Height );

			if ( !performLayout ) return;

			if ( direction == Direction.RTL )
			{
				for ( var i = 0; i < items.Count; i++ )
					Bounds.SetChildTrailingPosition( node, items[i].Node, FlexDirection.Row );
			}

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
		finally
		{
			GridScratchPool.Return( scratch );
		}
	}

	private static float GapOrZero( float gap ) => Num.IsDefined( gap ) ? gap : 0.0f;

	//-- the grid -------------------------------------------------------------------------

	//The explicit tracks, with an automatic repeat expanded to as many repetitions as fit
	//the room (at least one), or one where the room is not settled (CSS Grid 7.2.3.2).
	private static void BuildExplicitTracks( List<GridTrack> tracks, GridTrackList list, float room, float gap )
	{
		foreach ( var size in list.Leading ) tracks.Add( new GridTrack { Size = size, Explicit = true } );

		if ( list.Repeated is not null )
		{
			var repetitions = 1;

			if ( Num.IsDefined( room ) )
			{
				var fixedSum = 0.0f;
				foreach ( var size in list.Leading ) fixedSum += FixedBreadth( size, room );
				foreach ( var size in list.Trailing ) fixedSum += FixedBreadth( size, room );

				var repeatSum = 0.0f;
				foreach ( var size in list.Repeated ) repeatSum += FixedBreadth( size, room );

				var perRepeat = repeatSum + gap * list.Repeated.Length;

				if ( perRepeat > 0 )
				{
					//fixed + n * repeat + gap * (fixed count + n * repeat count - 1) <= room
					var free = room - fixedSum - gap * ( list.FixedCount - 1 );
					repetitions = Math.Max( 1, (int)MathF.Floor( ( free + gap ) / perRepeat + 0.0001f ) );
				}
			}

			for ( var r = 0; r < repetitions; r++ )
				foreach ( var size in list.Repeated ) tracks.Add( new GridTrack { Size = size, Explicit = true, Repeated = true } );
		}

		foreach ( var size in list.Trailing ) tracks.Add( new GridTrack { Size = size, Explicit = true } );
	}

	//What a track counts for when repetitions are counted: its fixed maximum, else its fixed minimum, else nothing.
	private static float FixedBreadth( GridTrackSize size, float room )
	{
		var max = size.Max.Resolve( room );
		if ( Num.IsDefined( max ) ) return max;
		var min = size.Min.Resolve( room );
		return Num.IsDefined( min ) ? min : 0.0f;
	}

	//Implicit tracks before and after the explicit ones, sized from the automatic list in turn.
	private static void AddImplicitTracks( List<GridTrack> tracks, int before, int after, GridTrackList auto )
	{
		var pattern = auto.IsEmpty ? GridTrackList.SingleAuto : auto;
		var sizes = pattern.Leading.Length > 0 ? pattern.Leading : pattern.Trailing.Length > 0 ? pattern.Trailing : GridTrackList.SingleAuto.Leading;

		if ( before > 0 )
		{
			for ( var i = 0; i < before; i++ )
				tracks.Insert( 0, new GridTrack { Size = sizes[( sizes.Length - 1 - i % sizes.Length + sizes.Length ) % sizes.Length] } );
		}

		for ( var i = 0; i < after; i++ )
			tracks.Add( new GridTrack { Size = sizes[i % sizes.Length] } );
	}

	private static void CollapseEmptyRepeatedTracks( List<GridTrack> tracks, List<GridItem> items, bool columns )
	{
		for ( var t = 0; t < tracks.Count; t++ )
		{
			var track = tracks[t];
			if ( !track.Repeated ) continue;

			var occupied = false;
			for ( var i = 0; i < items.Count && !occupied; i++ )
			{
				var item = items[i];
				occupied = columns ? item.ColumnStart <= t && t < item.ColumnEnd : item.RowStart <= t && t < item.RowEnd;
			}

			if ( !occupied )
			{
				track.Collapsed = true;
				tracks[t] = track;
			}
		}
	}

	//-- placement ------------------------------------------------------------------------

	//Where a pair of lines puts an item in one axis (CSS Grid 8.3): two lines, a line and a span, or nothing definite
	//yet. Lines are 1-based in the style and 0-based here; a negative line counts back from the end of the explicit grid.
	private static void ResolveLines( GridLine start, GridLine end, int explicitTracks, out int from, out int to, out bool definite, out int span )
	{
		var startLine = start.IsLine ? ToIndex( start.Value, explicitTracks ) : (int?)null;
		var endLine = end.IsLine ? ToIndex( end.Value, explicitTracks ) : (int?)null;

		if ( startLine.HasValue && endLine.HasValue )
		{
			from = startLine.Value;
			to = endLine.Value;
			if ( to < from ) ( from, to ) = ( to, from );
			if ( to == from ) to = from + 1;
			definite = true;
			span = to - from;
			return;
		}

		if ( startLine.HasValue )
		{
			from = startLine.Value;
			span = end.IsSpan ? end.Value : 1;
			to = from + span;
			definite = true;
			return;
		}

		if ( endLine.HasValue )
		{
			to = endLine.Value;
			span = start.IsSpan ? start.Value : 1;
			from = to - span;
			definite = true;
			return;
		}

		span = start.IsSpan ? start.Value : end.IsSpan ? end.Value : 1;
		from = 0;
		to = span;
		definite = false;
	}

	private static int ToIndex( int line, int explicitTracks )
		=> line > 0 ? line - 1 : explicitTracks + 1 + line;

	private static void PlaceItems( GridScratch scratch, int explicitColumns, int explicitRows, bool columnFlow, bool dense )
	{
		var items = scratch.Items;
		var occupied = scratch.Occupied;

		//Step 1: everything with a definite position in both axes.
		for ( var i = 0; i < items.Count; i++ )
		{
			var item = items[i];
			var s = item.Node.Style;

			ResolveLines( s.GridColumnStart, s.GridColumnEnd, explicitColumns, out item.ColumnStart, out item.ColumnEnd, out item.ColumnDefinite, out item.ColumnSpan );
			ResolveLines( s.GridRowStart, s.GridRowEnd, explicitRows, out item.RowStart, out item.RowEnd, out item.RowDefinite, out item.RowSpan );

			if ( item.ColumnDefinite && item.RowDefinite ) Occupy( occupied, item );
			items[i] = item;
		}

		//The flow's two axes: the major one the cursor runs down, the minor one it runs along. Row flow fills each row's columns before the next row.
		var minorCount = columnFlow ? Math.Max( explicitRows, 1 ) : Math.Max( explicitColumns, 1 );

		for ( var i = 0; i < items.Count; i++ )
		{
			var item = items[i];
			if ( columnFlow ? item.RowDefinite : item.ColumnDefinite )
				minorCount = Math.Max( minorCount, columnFlow ? item.RowEnd : item.ColumnEnd );
		}

		//Step 2: items locked to a major line, placed along it past whatever already sits there.
		var cursors = scratch.Cursors;

		for ( var i = 0; i < items.Count; i++ )
		{
			var item = items[i];
			var majorDefinite = columnFlow ? item.ColumnDefinite : item.RowDefinite;
			var minorDefinite = columnFlow ? item.RowDefinite : item.ColumnDefinite;
			if ( !majorDefinite || minorDefinite ) continue;

			var major = columnFlow ? item.ColumnStart : item.RowStart;
			var majorSpan = columnFlow ? item.ColumnSpan : item.RowSpan;
			var minorSpan = columnFlow ? item.RowSpan : item.ColumnSpan;

			while ( cursors.Count <= major ) cursors.Add( 0 );
			var minor = dense ? 0 : cursors[major];

			while ( minor + minorSpan > minorCount && minor > 0 ) minor = 0;
			while ( !Fits( occupied, columnFlow, major, majorSpan, minor, minorSpan ) ) minor++;

			SetMinor( ref item, columnFlow, minor, minorSpan );
			Occupy( occupied, item );
			cursors[major] = minor + minorSpan;
			items[i] = item;
		}

		//Step 3: the rest, from a cursor that only goes forward - unless dense, when it starts over.
		var cursorMajor = 0;
		var cursorMinor = 0;

		for ( var i = 0; i < items.Count; i++ )
		{
			var item = items[i];
			var majorDefinite = columnFlow ? item.ColumnDefinite : item.RowDefinite;
			if ( majorDefinite ) continue;

			var minorDefinite = columnFlow ? item.RowDefinite : item.ColumnDefinite;
			var majorSpan = columnFlow ? item.ColumnSpan : item.RowSpan;
			var minorSpan = columnFlow ? item.RowSpan : item.ColumnSpan;

			if ( dense )
			{
				cursorMajor = 0;
				cursorMinor = 0;
			}

			if ( minorDefinite )
			{
				var minor = columnFlow ? item.RowStart : item.ColumnStart;
				if ( minor < cursorMinor ) cursorMajor++;
				cursorMinor = minor;

				while ( !Fits( occupied, columnFlow, cursorMajor, majorSpan, minor, minorSpan ) ) cursorMajor++;

				SetMajor( ref item, columnFlow, cursorMajor, majorSpan );
			}
			else
			{
				var limit = Math.Max( minorCount, minorSpan );

				while ( true )
				{
					if ( cursorMinor + minorSpan > limit )
					{
						cursorMajor++;
						cursorMinor = 0;
						continue;
					}

					if ( Fits( occupied, columnFlow, cursorMajor, majorSpan, cursorMinor, minorSpan ) ) break;
					cursorMinor++;
				}

				SetMajor( ref item, columnFlow, cursorMajor, majorSpan );
				SetMinor( ref item, columnFlow, cursorMinor, minorSpan );
			}

			Occupy( occupied, item );
			items[i] = item;
		}
	}

	private static void SetMajor( ref GridItem item, bool columnFlow, int start, int span )
	{
		if ( columnFlow ) { item.ColumnStart = start; item.ColumnEnd = start + span; item.ColumnDefinite = true; }
		else { item.RowStart = start; item.RowEnd = start + span; item.RowDefinite = true; }
	}

	private static void SetMinor( ref GridItem item, bool columnFlow, int start, int span )
	{
		if ( columnFlow ) { item.RowStart = start; item.RowEnd = start + span; item.RowDefinite = true; }
		else { item.ColumnStart = start; item.ColumnEnd = start + span; item.ColumnDefinite = true; }
	}

	private static bool Fits( HashSet<long> occupied, bool columnFlow, int major, int majorSpan, int minor, int minorSpan )
	{
		for ( var a = major; a < major + majorSpan; a++ )
		{
			for ( var b = minor; b < minor + minorSpan; b++ )
			{
				var column = columnFlow ? a : b;
				var row = columnFlow ? b : a;
				if ( occupied.Contains( Cell( column, row ) ) ) return false;
			}
		}

		return true;
	}

	private static void Occupy( HashSet<long> occupied, in GridItem item )
	{
		for ( var c = item.ColumnStart; c < item.ColumnEnd; c++ )
			for ( var r = item.RowStart; r < item.RowEnd; r++ )
				occupied.Add( Cell( c, r ) );
	}

	//Cells may sit before the explicit grid, so both coordinates are offset into the positive range.
	private static long Cell( int column, int row ) => ( (long)( column + 1_000_000 ) << 32 ) | (uint)( row + 1_000_000 );

	//-- track sizing ---------------------------------------------------------------------

	private enum Distribution { Start, Center, End, SpaceBetween, SpaceAround, SpaceEvenly, Stretch }

	private static Distribution JustifyKind( Justify justify ) => justify switch
	{
		Justify.Center => Distribution.Center,
		Justify.FlexEnd or Justify.End => Distribution.End,
		Justify.SpaceBetween => Distribution.SpaceBetween,
		Justify.SpaceAround => Distribution.SpaceAround,
		Justify.SpaceEvenly => Distribution.SpaceEvenly,
		Justify.Start => Distribution.Start,
		_ => Distribution.Stretch,
	};

	private static Distribution AlignKind( Align align ) => align switch
	{
		Align.Center => Distribution.Center,
		Align.FlexEnd or Align.End => Distribution.End,
		Align.SpaceBetween => Distribution.SpaceBetween,
		Align.SpaceAround => Distribution.SpaceAround,
		Align.SpaceEvenly => Distribution.SpaceEvenly,
		Align.Start => Distribution.Start,
		_ => Distribution.Stretch,
	};

	//CSS Grid 11 track sizing; columns first, then rows from items laid out at their column widths
	private static void SizeTracks( GridScratch scratch, bool columns, float room, SizingMode mode, float gap,
		Direction direction, int depth, uint generationCount, Distribution distribution, float availableInnerWidth, float availableInnerHeight )
	{
		var tracks = columns ? scratch.Columns : scratch.Rows;
		var items = scratch.Items;

		//11.4: initialise.
		for ( var t = 0; t < tracks.Count; t++ )
		{
			var track = tracks[t];
			var min = track.Size.Min.Resolve( room );
			var max = track.Size.Max.Resolve( room );

			track.Base = Num.IsDefined( min ) ? min : 0.0f;
			track.Limit = track.Size.Max.IsFlexible || !Num.IsDefined( max ) ? Num.Undefined : max;
			if ( Num.IsDefined( track.Limit ) && track.Limit < track.Base ) track.Limit = track.Base;
			track.Planned = 0;
			track.Frozen = false;
			tracks[t] = track;
		}

		//11.5: what the items contribute, by how many tracks they span.
		var maxSpan = 1;
		for ( var i = 0; i < items.Count; i++ ) maxSpan = Math.Max( maxSpan, columns ? items[i].ColumnSpan : items[i].RowSpan );

		for ( var span = 1; span <= maxSpan; span++ )
		{
			for ( var i = 0; i < items.Count; i++ )
			{
				var item = items[i];
				var from = columns ? item.ColumnStart : item.RowStart;
				var to = columns ? item.ColumnEnd : item.RowEnd;
				if ( to - from != span ) continue;
				if ( SpansFlexible( tracks, from, to ) ) continue;
				if ( !SpansIntrinsic( tracks, from, to ) ) continue;

				var minContribution = MinContribution( ref item, columns, scratch, direction, depth, generationCount, gap, availableInnerWidth, availableInnerHeight );
				var maxContribution = MaxContribution( ref item, columns, scratch, direction, depth, generationCount, gap, availableInnerWidth, availableInnerHeight );
				items[i] = item;

				if ( span == 1 )
				{
					var track = tracks[from];

					switch ( track.Size.Min.Kind )
					{
						case TrackBreadthKind.MinContent:
						case TrackBreadthKind.Auto:
							track.Base = MathF.Max( track.Base, minContribution );
							break;
						case TrackBreadthKind.MaxContent:
							track.Base = MathF.Max( track.Base, maxContribution );
							break;
					}

					switch ( track.Size.Max.Kind )
					{
						case TrackBreadthKind.MinContent:
							track.Limit = Num.IsDefined( track.Limit ) ? MathF.Max( track.Limit, minContribution ) : minContribution;
							break;
						case TrackBreadthKind.MaxContent:
						case TrackBreadthKind.Auto:
							track.Limit = Num.IsDefined( track.Limit ) ? MathF.Max( track.Limit, maxContribution ) : maxContribution;
							break;
					}

					if ( Num.IsDefined( track.Limit ) && track.Limit < track.Base ) track.Limit = track.Base;
					tracks[from] = track;
				}
				else
				{
					//11.5.1: the shortfall over the span goes to the tracks with intrinsic sizing, first into
					//their base sizes and then, for a max-content contribution, into their growth limits.
					DistributeExtra( tracks, from, to, minContribution - SpanBases( tracks, from, to, gap ), false, false );
					DistributeExtra( tracks, from, to, maxContribution - SpanLimits( tracks, from, to, gap ), true, false );
				}
			}
		}

		//Items over flexible tracks only ever raise base sizes, and only those of the flexible tracks.
		for ( var i = 0; i < items.Count; i++ )
		{
			var item = items[i];
			var from = columns ? item.ColumnStart : item.RowStart;
			var to = columns ? item.ColumnEnd : item.RowEnd;
			if ( !SpansFlexible( tracks, from, to ) ) continue;

			var minContribution = MinContribution( ref item, columns, scratch, direction, depth, generationCount, gap, availableInnerWidth, availableInnerHeight );
			items[i] = item;
			DistributeExtra( tracks, from, to, minContribution - SpanBases( tracks, from, to, gap ), false, true );
		}

		//A growth limit still unlimited is the base size.
		for ( var t = 0; t < tracks.Count; t++ )
		{
			var track = tracks[t];
			if ( !Num.IsDefined( track.Limit ) ) track.Limit = track.Base;
			tracks[t] = track;
		}

		//11.6: maximise. With room to share, every track grows towards its limit, equally; with
		//none settled - the box is asked how big it wants to be - each track is its limit.
		var gaps = gap * Math.Max( 0, ActiveCount( tracks ) - 1 );

		if ( Num.IsDefined( room ) )
		{
			var free = room - gaps - SumBases( tracks );
			if ( free > 0 ) GrowEqually( tracks, free, false );

			if ( mode == SizingMode.FitContent )
			{
				//At most the room: the tracks may take their limits if that is narrower still.
				var atLimits = gaps + SumLimits( tracks );
				if ( atLimits < room )
					for ( var t = 0; t < tracks.Count; t++ ) { var track = tracks[t]; track.Base = track.Limit; tracks[t] = track; }
			}
		}
		else
		{
			for ( var t = 0; t < tracks.Count; t++ ) { var track = tracks[t]; track.Base = track.Limit; tracks[t] = track; }
		}

		//11.7: the flexible tracks.
		ExpandFlexibleTracks( tracks, items, columns, room, mode, gap, scratch, direction, depth, generationCount, availableInnerWidth, availableInnerHeight );

		//11.8 and 10.5: what is left goes to the automatic tracks where the box stretches them, and between or around the tracks otherwise.
		var leading = 0.0f;
		var between = gap;

		if ( Num.IsDefined( room ) && mode != SizingMode.MaxContent )
		{
			var free = room - gaps - SumBases( tracks );

			if ( free > 0 )
			{
				var active = ActiveCount( tracks );

				switch ( distribution )
				{
					case Distribution.Stretch:
						var autoTracks = 0;
						for ( var t = 0; t < tracks.Count; t++ ) if ( !tracks[t].Collapsed && tracks[t].Size.Max.Kind == TrackBreadthKind.Auto ) autoTracks++;
						if ( autoTracks > 0 )
						{
							var share = free / autoTracks;
							for ( var t = 0; t < tracks.Count; t++ )
							{
								var track = tracks[t];
								if ( track.Collapsed || track.Size.Max.Kind != TrackBreadthKind.Auto ) continue;
								track.Base += share;
								tracks[t] = track;
							}
						}
						break;
					case Distribution.Center:
						leading = free / 2;
						break;
					case Distribution.End:
						leading = free;
						break;
					case Distribution.SpaceBetween:
						if ( active > 1 ) between += free / ( active - 1 );
						break;
					case Distribution.SpaceAround:
						if ( active > 0 ) { leading = free / ( 2 * active ); between += free / active; }
						break;
					case Distribution.SpaceEvenly:
						if ( active > 0 ) { leading = free / ( active + 1 ); between += free / ( active + 1 ); }
						break;
				}
			}
		}

		//Where each track starts. A collapsed track takes no room and no gap.
		var offset = leading;
		var first = true;

		for ( var t = 0; t < tracks.Count; t++ )
		{
			var track = tracks[t];

			if ( track.Collapsed )
			{
				track.Offset = offset;
				tracks[t] = track;
				continue;
			}

			if ( !first ) offset += between;
			first = false;
			track.Offset = offset;
			offset += track.Base;
			tracks[t] = track;
		}
	}

	private static bool SpansFlexible( List<GridTrack> tracks, int from, int to )
	{
		for ( var t = from; t < to; t++ ) if ( tracks[t].Size.Max.IsFlexible ) return true;
		return false;
	}

	private static bool SpansIntrinsic( List<GridTrack> tracks, int from, int to )
	{
		for ( var t = from; t < to; t++ ) if ( tracks[t].Size.Min.IsIntrinsic || tracks[t].Size.Max.IsIntrinsic ) return true;
		return false;
	}

	private static float SpanBases( List<GridTrack> tracks, int from, int to, float gap )
	{
		var sum = 0.0f;
		var active = 0;
		for ( var t = from; t < to; t++ ) { if ( tracks[t].Collapsed ) continue; sum += tracks[t].Base; active++; }
		return sum + gap * Math.Max( 0, active - 1 );
	}

	private static float SpanLimits( List<GridTrack> tracks, int from, int to, float gap )
	{
		var sum = 0.0f;
		var active = 0;
		for ( var t = from; t < to; t++ )
		{
			if ( tracks[t].Collapsed ) continue;
			sum += Num.IsDefined( tracks[t].Limit ) ? tracks[t].Limit : tracks[t].Base;
			active++;
		}
		return sum + gap * Math.Max( 0, active - 1 );
	}

	private static float SpanSize( List<GridTrack> tracks, int from, int to, float gap )
	{
		var sum = 0.0f;
		var active = 0;
		for ( var t = from; t < to; t++ ) { if ( tracks[t].Collapsed ) continue; sum += tracks[t].Base; active++; }
		return sum + gap * Math.Max( 0, active - 1 );
	}

	private static float SumBases( List<GridTrack> tracks )
	{
		var sum = 0.0f;
		for ( var t = 0; t < tracks.Count; t++ ) if ( !tracks[t].Collapsed ) sum += tracks[t].Base;
		return sum;
	}

	private static float SumLimits( List<GridTrack> tracks )
	{
		var sum = 0.0f;
		for ( var t = 0; t < tracks.Count; t++ ) if ( !tracks[t].Collapsed ) sum += tracks[t].Limit;
		return sum;
	}

	private static int ActiveCount( List<GridTrack> tracks )
	{
		var count = 0;
		for ( var t = 0; t < tracks.Count; t++ ) if ( !tracks[t].Collapsed ) count++;
		return count;
	}

	//Where the last track that takes any room ends: the size of the grid along the axis.
	private static float TotalSize( List<GridTrack> tracks, float gap )
	{
		_ = gap;
		for ( var t = tracks.Count - 1; t >= 0; t-- )
			if ( !tracks[t].Collapsed ) return tracks[t].Offset + tracks[t].Base;
		return 0.0f;
	}

	//CSS Grid 11.5.1: share extra span space equally among tracks that can take it
	private static void DistributeExtra( List<GridTrack> tracks, int from, int to, float extra, bool intoLimits, bool flexibleOnly )
	{
		if ( !( extra > 0 ) ) return;

		var affected = 0;
		for ( var t = from; t < to; t++ )
		{
			var track = tracks[t];
			if ( track.Collapsed ) continue;
			var takes = flexibleOnly ? track.Size.Max.IsFlexible : ( intoLimits ? track.Size.Max.IsIntrinsic : track.Size.Min.IsIntrinsic || track.Size.Max.IsIntrinsic );
			if ( takes ) affected++;
		}

		if ( affected == 0 ) return;

		if ( intoLimits )
		{
			var share = extra / affected;
			for ( var t = from; t < to; t++ )
			{
				var track = tracks[t];
				if ( track.Collapsed || !track.Size.Max.IsIntrinsic ) continue;
				var limit = Num.IsDefined( track.Limit ) ? track.Limit : track.Base;
				track.Limit = limit + share;
				tracks[t] = track;
			}
			return;
		}

		//Into the base sizes: up to each track's limit first, then what is left beyond the limits.
		var remaining = extra;
		var unfrozen = affected;

		for ( var t = from; t < to; t++ ) { var track = tracks[t]; track.Planned = 0; track.Frozen = false; tracks[t] = track; }

		while ( remaining > 0.0001f && unfrozen > 0 )
		{
			var share = remaining / unfrozen;
			var distributed = 0.0f;

			for ( var t = from; t < to; t++ )
			{
				var track = tracks[t];
				if ( track.Collapsed || track.Frozen ) continue;
				var takes = flexibleOnly ? track.Size.Max.IsFlexible : track.Size.Min.IsIntrinsic || track.Size.Max.IsIntrinsic;
				if ( !takes ) continue;

				var headroom = Num.IsDefined( track.Limit ) ? track.Limit - track.Base - track.Planned : float.PositiveInfinity;
				var take = MathF.Min( share, MathF.Max( 0, headroom ) );
				track.Planned += take;
				distributed += take;

				if ( take < share - 0.0001f )
				{
					track.Frozen = true;
					unfrozen--;
				}

				tracks[t] = track;
			}

			remaining -= distributed;
			if ( distributed <= 0.0001f ) break;
		}

		//Beyond the limits: the tracks whose maximum is intrinsic, or all affected ones where none is.
		if ( remaining > 0.0001f )
		{
			var beyond = 0;
			for ( var t = from; t < to; t++ )
			{
				var track = tracks[t];
				if ( track.Collapsed ) continue;
				var takes = flexibleOnly ? track.Size.Max.IsFlexible : track.Size.Max.IsIntrinsic;
				if ( takes ) beyond++;
			}

			var useAll = beyond == 0;
			var count = useAll ? affected : beyond;
			var share = remaining / count;

			for ( var t = from; t < to; t++ )
			{
				var track = tracks[t];
				if ( track.Collapsed ) continue;
				var takes = flexibleOnly ? track.Size.Max.IsFlexible : useAll ? track.Size.Min.IsIntrinsic || track.Size.Max.IsIntrinsic : track.Size.Max.IsIntrinsic;
				if ( !takes ) continue;
				track.Planned += share;
				tracks[t] = track;
			}
		}

		for ( var t = from; t < to; t++ )
		{
			var track = tracks[t];
			if ( track.Planned > 0 )
			{
				track.Base += track.Planned;
				if ( Num.IsDefined( track.Limit ) && track.Limit < track.Base ) track.Limit = track.Base;
			}
			track.Planned = 0;
			track.Frozen = false;
			tracks[t] = track;
		}
	}

	//CSS Grid 11.6: free space shared equally, each track stopping at its limit and the rest going on.
	private static void GrowEqually( List<GridTrack> tracks, float free, bool flexibleOnly )
	{
		var remaining = free;
		var unfrozen = 0;

		for ( var t = 0; t < tracks.Count; t++ )
		{
			var track = tracks[t];
			track.Frozen = track.Collapsed || ( flexibleOnly && !track.Size.Max.IsFlexible ) || !( track.Limit > track.Base );
			if ( !track.Frozen ) unfrozen++;
			tracks[t] = track;
		}

		while ( remaining > 0.0001f && unfrozen > 0 )
		{
			var share = remaining / unfrozen;
			var distributed = 0.0f;

			for ( var t = 0; t < tracks.Count; t++ )
			{
				var track = tracks[t];
				if ( track.Frozen ) continue;

				var take = MathF.Min( share, track.Limit - track.Base );
				track.Base += take;
				distributed += take;

				if ( take < share - 0.0001f )
				{
					track.Frozen = true;
					unfrozen--;
				}

				tracks[t] = track;
			}

			remaining -= distributed;
			if ( distributed <= 0.0001f ) break;
		}

		for ( var t = 0; t < tracks.Count; t++ ) { var track = tracks[t]; track.Frozen = false; tracks[t] = track; }
	}

	//CSS Grid 11.7: a flexible track is its share of the free space. With room settled, the share is found by treating flexible
	//tracks too big for it as fixed and trying again; with none, it is the largest any flexible track or any item over one needs.
	private static void ExpandFlexibleTracks( List<GridTrack> tracks, List<GridItem> items, bool columns, float room, SizingMode mode, float gap,
		GridScratch scratch, Direction direction, int depth, uint generationCount, float availableInnerWidth, float availableInnerHeight )
	{
		var anyFlexible = false;
		for ( var t = 0; t < tracks.Count; t++ ) if ( !tracks[t].Collapsed && tracks[t].Size.Max.IsFlexible ) { anyFlexible = true; break; }
		if ( !anyFlexible ) return;

		float fraction;

		if ( Num.IsDefined( room ) && mode != SizingMode.MaxContent )
		{
			//The free space is shared out by flex factor; a track whose base size is already more
			//than its share keeps its base size and drops out, and the rest is shared again.
			for ( var t = 0; t < tracks.Count; t++ ) { var track = tracks[t]; track.Frozen = false; tracks[t] = track; }

			var gaps = gap * Math.Max( 0, ActiveCount( tracks ) - 1 );

			while ( true )
			{
				var leftover = room - gaps;
				var factors = 0.0f;

				for ( var t = 0; t < tracks.Count; t++ )
				{
					var track = tracks[t];
					if ( track.Collapsed ) continue;
					if ( track.Size.Max.IsFlexible && !track.Frozen ) factors += track.Size.Max.Value;
					else leftover -= track.Base;
				}

				fraction = factors > 0 ? MathF.Max( 0, leftover ) / MathF.Max( factors, 1.0f ) : 0;

				var frozeOne = false;
				for ( var t = 0; t < tracks.Count; t++ )
				{
					var track = tracks[t];
					if ( track.Collapsed || !track.Size.Max.IsFlexible || track.Frozen ) continue;
					if ( track.Base > fraction * track.Size.Max.Value + 0.0001f )
					{
						track.Frozen = true;
						tracks[t] = track;
						frozeOne = true;
					}
				}

				if ( !frozeOne ) break;
			}
		}
		else
		{
			//11.7.1: the size of an fr with no room to share: what the tracks and the items over them need, per unit of flex.
			fraction = 0;

			for ( var t = 0; t < tracks.Count; t++ )
			{
				var track = tracks[t];
				if ( track.Collapsed || !track.Size.Max.IsFlexible || track.Size.Max.Value <= 0 ) continue;
				fraction = MathF.Max( fraction, track.Base / track.Size.Max.Value );
			}

			for ( var i = 0; i < items.Count; i++ )
			{
				var item = items[i];
				var from = columns ? item.ColumnStart : item.RowStart;
				var to = columns ? item.ColumnEnd : item.RowEnd;
				if ( !SpansFlexible( tracks, from, to ) ) continue;

				var contribution = MaxContribution( ref item, columns, scratch, direction, depth, generationCount, gap, availableInnerWidth, availableInnerHeight );
				items[i] = item;

				var factors = 0.0f;
				var fixedPart = 0.0f;
				var active = 0;
				for ( var t = from; t < to; t++ )
				{
					var track = tracks[t];
					if ( track.Collapsed ) continue;
					active++;
					if ( track.Size.Max.IsFlexible ) factors += track.Size.Max.Value;
					else fixedPart += track.Base;
				}

				var space = contribution - fixedPart - gap * Math.Max( 0, active - 1 );
				if ( factors > 0 && space > 0 ) fraction = MathF.Max( fraction, space / MathF.Max( factors, 1.0f ) );
			}
		}

		for ( var t = 0; t < tracks.Count; t++ )
		{
			var track = tracks[t];
			if ( track.Collapsed || !track.Size.Max.IsFlexible ) continue;
			if ( !track.Frozen ) track.Base = MathF.Max( track.Base, fraction * track.Size.Max.Value );
			track.Limit = track.Base;
			track.Frozen = false;
			tracks[t] = track;
		}
	}

	//-- what the items contribute --------------------------------------------------------

	//An item's smallest size along the axis, margins included: its definite size, or its content measured
	//with no room at all across, or, along the rows, its height at the width its columns give it.
	private static float MinContribution( ref GridItem item, bool columns, GridScratch scratch, Direction direction, int depth, uint generationCount,
		float gap, float availableInnerWidth, float availableInnerHeight )
	{
		if ( !columns ) return HeightContribution( ref item, scratch, direction, depth, generationCount, gap, availableInnerWidth, availableInnerHeight );
		MeasureWidths( ref item, direction, depth, generationCount );
		return item.MinContentWidth;
	}

	private static float MaxContribution( ref GridItem item, bool columns, GridScratch scratch, Direction direction, int depth, uint generationCount,
		float gap, float availableInnerWidth, float availableInnerHeight )
	{
		if ( !columns ) return HeightContribution( ref item, scratch, direction, depth, generationCount, gap, availableInnerWidth, availableInnerHeight );
		MeasureWidths( ref item, direction, depth, generationCount );
		return item.MaxContentWidth;
	}

	private static void MeasureWidths( ref GridItem item, Direction direction, int depth, uint generationCount )
	{
		if ( item.WidthsMeasured ) return;
		item.WidthsMeasured = true;

		var child = item.Node;
		var margins = child.Style.ComputeMarginForAxis( FlexDirection.Row, Num.Undefined );

		if ( child.HasDefiniteLength( Dimension.Width, Num.Undefined ) )
		{
			var width = Bounds.BoundAxis( child, FlexDirection.Row, direction,
				child.GetResolvedDimension( direction, Dimension.Width, Num.Undefined, Num.Undefined ), Num.Undefined, Num.Undefined );
			item.MinContentWidth = width + margins;
			item.MaxContentWidth = width + margins;
			return;
		}

		var heightAvailable = Num.Undefined;
		var heightMode = SizingMode.MaxContent;

		if ( child.HasDefiniteLength( Dimension.Height, Num.Undefined ) )
		{
			heightAvailable = child.GetResolvedDimension( direction, Dimension.Height, Num.Undefined, Num.Undefined )
				+ child.Style.ComputeMarginForAxis( FlexDirection.Column, Num.Undefined );
			heightMode = SizingMode.StretchFit;
		}

		//Min-content: the content given no width at all, so text breaks at every opportunity.
		CalculateLayoutInternal( child, margins, heightAvailable, direction, SizingMode.FitContent, heightMode,
			Num.Undefined, Num.Undefined, false, depth, generationCount );
		item.MinContentWidth = child.Layout.MeasuredDimension( Dimension.Width ) + margins;

		CalculateLayoutInternal( child, Num.Undefined, heightAvailable, direction, SizingMode.MaxContent, heightMode,
			Num.Undefined, Num.Undefined, false, depth, generationCount );
		item.MaxContentWidth = MathF.Max( item.MinContentWidth, child.Layout.MeasuredDimension( Dimension.Width ) + margins );
	}

	//An item's height, margins included, laid out at the width its column area gives it.
	private static float HeightContribution( ref GridItem item, GridScratch scratch, Direction direction, int depth, uint generationCount,
		float gap, float availableInnerWidth, float availableInnerHeight )
	{
		if ( item.HeightMeasured ) return item.Height;
		item.HeightMeasured = true;

		var child = item.Node;
		var areaWidth = SpanSize( scratch.Columns, item.ColumnStart, item.ColumnEnd, gap );
		var marginColumn = child.Style.ComputeMarginForAxis( FlexDirection.Column, areaWidth );

		if ( child.HasDefiniteLength( Dimension.Height, Num.Undefined ) )
		{
			item.Height = Bounds.BoundAxis( child, FlexDirection.Column, direction,
				child.GetResolvedDimension( direction, Dimension.Height, Num.Undefined, areaWidth ), Num.Undefined, areaWidth ) + marginColumn;
			return item.Height;
		}

		var justify = ResolveItemAlignment( child.Style.JustifySelf, child.Owner?.Style.JustifyItems ?? Align.Stretch );
		var width = ItemSize( child, direction, FlexDirection.Row, areaWidth, justify, ref item, depth, generationCount, Num.Undefined );
		var marginRow = child.Style.ComputeMarginForAxis( FlexDirection.Row, areaWidth );

		CalculateLayoutInternal( child, width + marginRow, Num.Undefined, direction, SizingMode.StretchFit, SizingMode.MaxContent,
			areaWidth, Num.Undefined, false, depth, generationCount );

		item.Height = child.Layout.MeasuredDimension( Dimension.Height ) + marginColumn;
		return item.Height;
	}

	//-- the items in their areas ---------------------------------------------------------

	private static Align ResolveItemAlignment( Align self, Align items )
	{
		var align = self == Align.Auto ? items : self;
		return align switch
		{
			Align.Auto or Align.Baseline => Align.Stretch,
			_ => align,
		};
	}

	//How big an item is along one axis of its area, margins excluded: its definite size, the area less its margins where it stretches, or
	//as much as its content wants, within the area, where it is aligned instead. Automatic margins take up the slack and never stretch.
	private static float ItemSize( LayoutNode child, Direction direction, FlexDirection axis, float area, Align alignment,
		ref GridItem item, int depth, uint generationCount, float crossSize )
	{
		var dimension = Axis.DimensionOf( axis );
		var margins = child.Style.ComputeMarginForAxis( axis, area );
		var startAuto = child.Style.FlexStartMarginIsAuto( axis, direction );
		var endAuto = child.Style.FlexEndMarginIsAuto( axis, direction );

		float size;

		if ( child.HasDefiniteLength( dimension, area ) )
		{
			size = child.GetResolvedDimension( direction, dimension, area, axis == FlexDirection.Row ? area : crossSize );
		}
		else if ( alignment == Align.Stretch && !startAuto && !endAuto && Num.IsDefined( area ) )
		{
			size = area - margins;
		}
		else
		{
			//Fit the content: as wide as it wants, within the area.
			if ( axis == FlexDirection.Row )
			{
				MeasureWidths( ref item, direction, depth, generationCount );
				size = item.MaxContentWidth - margins;
				if ( Num.IsDefined( area ) ) size = MathF.Min( size, MathF.Max( area - margins, item.MinContentWidth - margins ) );
			}
			else
			{
				var marginRow = child.Style.ComputeMarginForAxis( FlexDirection.Row, area );
				CalculateLayoutInternal( child, crossSize + marginRow, Num.Undefined, direction, SizingMode.StretchFit, SizingMode.MaxContent,
					crossSize, Num.Undefined, false, depth, generationCount );
				size = child.Layout.MeasuredDimension( Dimension.Height );
			}
		}

		return Bounds.BoundAxis( child, axis, direction, size, area, axis == FlexDirection.Row ? area : crossSize );
	}

	//Where an item's box sits along one axis of its area: after its start margin, moved along by the alignment or by automatic margins.
	private static float Place( LayoutNode child, Direction direction, FlexDirection axis, float area, float size, Align alignment )
	{
		var start = child.Style.ComputeFlexStartMargin( axis, direction, area );
		var margins = child.Style.ComputeMarginForAxis( axis, area );
		var startAuto = child.Style.FlexStartMarginIsAuto( axis, direction );
		var endAuto = child.Style.FlexEndMarginIsAuto( axis, direction );
		var slack = area - size - margins;

		if ( !Num.IsDefined( slack ) || slack <= 0 ) return start;

		if ( startAuto && endAuto ) return start + slack / 2;
		if ( startAuto ) return start + slack;
		if ( endAuto ) return start;

		return alignment switch
		{
			Align.Center => start + slack / 2,
			Align.FlexEnd or Align.End => start + slack,
			_ => start,
		};
	}
}
