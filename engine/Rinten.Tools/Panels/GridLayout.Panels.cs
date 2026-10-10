using Rinten;
using System;
using System.Collections.Generic;

namespace Editor;

/// <summary>A grid of widgets, arranged.</summary>
public partial class GridLayout
{
	private struct Placed
	{
		public int X;
		public int Y;
		public int SpanX;
		public int SpanY;

		public Widget Widget;
		public Layout Layout;
	}

	private readonly List<Placed> placed = new();

	private float[] rowStretch = Array.Empty<float>();
	private float[] columnStretch = Array.Empty<float>();

	//Minimum track sizes; what makes separate inspector rows agree on their label column width
	private readonly Dictionary<int, float> minimumColumn = new();
	private readonly Dictionary<int, float> minimumRow = new();

	internal void SetMinimumColumnWidthManaged( int column, float width )
	{
		minimumColumn[column] = width;
		OnGeometryChanged();
	}

	internal void SetMinimumRowHeightManaged( int row, float height )
	{
		minimumRow[row] = height;
		OnGeometryChanged();
	}

	//The floors, as an array the length of the tracks there are.
	private static float[] Floors( Dictionary<int, float> said, int count )
	{
		var floors = new float[count];

		foreach ( var (index, size) in said )
		{
			if ( index >= 0 && index < count ) floors[index] = size;
		}

		return floors;
	}

	private void AddPlaced( Placed cell )
	{
		placed.Add( cell );

		cell.Widget?.SetPanelParent( Owner );

		if ( cell.Layout is not null )
		{
			cell.Layout.Parent = this;
			cell.Layout.SetOwner( Owner );
		}

		//Everything is a cell of the layout as well, so what is arranged and what is parented stay the same list.
		Cells.Add( new Cell { Widget = cell.Widget, Layout = cell.Layout } );

		OnGeometryChanged();
	}

	//Size the whole grid wants: widest column across, all rows down
	internal Vector2 WantedGrid()
	{
		if ( placed.Count == 0 ) return 0;

		var columns = 0;
		var rows = 0;

		foreach ( var cell in placed )
		{
			columns = Math.Max( columns, cell.X + cell.SpanX );
			rows = Math.Max( rows, cell.Y + cell.SpanY );
		}

		var widths = new float[columns];
		var heights = new float[rows];

		foreach ( var cell in placed )
		{
			var wanted = Wanted( cell );

			if ( cell.SpanX == 1 ) widths[cell.X] = MathF.Max( widths[cell.X], wanted.x );
			if ( cell.SpanY == 1 ) heights[cell.Y] = MathF.Max( heights[cell.Y], wanted.y );
		}

		var widthFloors = Floors( minimumColumn, columns );
		var heightFloors = Floors( minimumRow, rows );

		var across = HorizontalSpacing * (columns - 1);
		for ( var i = 0; i < columns; i++ ) across += MathF.Max( widths[i], widthFloors[i] );

		var down = VerticalSpacing * (rows - 1);
		for ( var i = 0; i < rows; i++ ) down += MathF.Max( heights[i], heightFloors[i] );

		var edges = ManagedMargin.EdgeSize;

		return new Vector2( across + edges.x, down + edges.y );
	}

	//Where one cell of the grid ended up, last time it was arranged.
	internal Rect CellRectManaged( int x, int y )
	{
		foreach ( var cell in placed )
		{
			if ( cell.X != x || cell.Y != y ) continue;

			if ( cell.Widget is not null ) return new Rect( cell.Widget.Position, cell.Widget.Size );
			if ( cell.Layout is not null ) return cell.Layout.Area;
		}

		return default;
	}

	//Cell size by the shared layout rule, so nested layouts report wanted size, not default widget size
	private static Vector2 Wanted( Placed cell )
	{
		var measured = cell.Layout is { } layout
			? new Cell { Layout = layout }
			: new Cell { Widget = cell.Widget };

		if ( measured.Widget is not null && measured.Widget.Hidden ) return 0;

		return new Vector2( WantsOf( measured, vertical: false ), WantsOf( measured, vertical: true ) );
	}

	//Fits tracks to the space: spare goes to stretch tracks by weight, shortfall is taken back
	private static void Spread( float[] sizes, float[] stretch, float[] floors, float available )
	{
		if ( sizes.Length == 0 ) return;

		var total = 0.0f;
		foreach ( var size in sizes ) total += size;

		var difference = available - total;

		if ( MathF.Abs( difference ) < 0.01f ) return;

		//Shortfall comes from each track's room above its minimum, proportionally, so labels don't vanish
		if ( difference < 0.0f )
		{
			var wanted = -difference;

			for ( var pass = 0; pass < 4 && wanted > 0.01f; pass++ )
			{
				var spare = 0.0f;

				for ( var i = 0; i < sizes.Length; i++ )
					spare += MathF.Max( sizes[i] - Floor( floors, i ), 0.0f );

				if ( spare <= 0.01f ) break;

				var taking = MathF.Min( wanted, spare );

				for ( var i = 0; i < sizes.Length; i++ )
				{
					var mine = MathF.Max( sizes[i] - Floor( floors, i ), 0.0f );

					if ( mine <= 0.0f ) continue;

					var cut = taking * ( mine / spare );

					sizes[i] -= cut;
					wanted -= cut;
				}
			}

			return;
		}

		var weight = 0.0f;

		if ( stretch is not null )
		{
			for ( var i = 0; i < sizes.Length && i < stretch.Length; i++ )
				weight += MathF.Max( stretch[i], 0.0f );
		}

		if ( weight <= 0.0f )
			return;

		for ( var i = 0; i < sizes.Length; i++ )
		{
			var mine = i < stretch.Length ? MathF.Max( stretch[i], 0.0f ) : 0.0f;

			if ( mine <= 0.0f ) continue;

			sizes[i] = MathF.Max( sizes[i] + ( difference * ( mine / weight ) ), 0.0f );
		}
	}

	//Stretch weights: as told, else tracks with expanding cells; none means spare room is left unused
	private float[] Weights( float[] told, int tracks, bool vertical )
	{
		if ( told is not null )
		{
			foreach ( var weight in told )
			{
				if ( weight > 0.0f ) return told;
			}
		}

		var derived = new float[tracks];

		foreach ( var cell in placed )
		{
			var measured = cell.Layout is { } layout
				? new Cell { Layout = layout }
				: new Cell { Widget = cell.Widget };

			if ( measured.Widget is { Hidden: true } ) continue;
			if ( !GrowsOf( measured, vertical ) ) continue;

			//A cell laid across several tracks wants the room across all of them, and has nothing to say about which one should hold it.
			var first = vertical ? cell.Y : cell.X;
			var span = vertical ? cell.SpanY : cell.SpanX;

			for ( var i = first; i < first + span && i < tracks; i++ )
			{
				if ( i >= 0 ) derived[i] = 1.0f;
			}
		}

		return derived;
	}

	//The least a track may be, where anything said so.
	private static float Floor( float[] floors, int index )
	{
		if ( floors is null || index < 0 || index >= floors.Length ) return 0.0f;

		return MathF.Max( floors[index], 0.0f );
	}

	//Puts every cell in its row and column inside the rectangle.
	internal void ArrangeGrid( Rect area )
	{
		Area = area;

		if ( placed.Count == 0 ) return;

		var inner = area.Shrink( ManagedMargin );

		var columns = 0;
		var rows = 0;

		foreach ( var cell in placed )
		{
			columns = Math.Max( columns, cell.X + cell.SpanX );
			rows = Math.Max( rows, cell.Y + cell.SpanY );
		}

		if ( columns == 0 || rows == 0 ) return;

		//The grid's own spacing properties fall back to the layout's when there is no native grid, which is exactly this case.
		var gapX = HorizontalSpacing;
		var gapY = VerticalSpacing;

		var widths = new float[columns];
		var heights = new float[rows];

		foreach ( var cell in placed )
		{
			var wanted = Wanted( cell );

			//A cell that covers several of them only has to fit across all of them together, so it is left to the ones it covers.
			if ( cell.SpanX == 1 ) widths[cell.X] = MathF.Max( widths[cell.X], wanted.x );
			if ( cell.SpanY == 1 ) heights[cell.Y] = MathF.Max( heights[cell.Y], wanted.y );
		}

		//The floors first: a column told it is at least so wide is that wide before anything
		//is shared out, which is what lines the labels of an inspector up with each other.
		var widthFloors = Floors( minimumColumn, columns );
		var heightFloors = Floors( minimumRow, rows );

		for ( var i = 0; i < columns; i++ ) widths[i] = MathF.Max( widths[i], widthFloors[i] );
		for ( var i = 0; i < rows; i++ ) heights[i] = MathF.Max( heights[i], heightFloors[i] );

		Spread( widths, Weights( columnStretch, columns, vertical: false ), widthFloors, inner.Width - (gapX * (columns - 1)) );
		Spread( heights, Weights( rowStretch, rows, vertical: true ), heightFloors, inner.Height - (gapY * (rows - 1)) );

		foreach ( var cell in placed )
		{
			var left = inner.Left + (gapX * cell.X);
			for ( var i = 0; i < cell.X; i++ ) left += widths[i];

			var top = inner.Top + (gapY * cell.Y);
			for ( var i = 0; i < cell.Y; i++ ) top += heights[i];

			var across = gapX * (cell.SpanX - 1);
			for ( var i = cell.X; i < cell.X + cell.SpanX && i < columns; i++ ) across += widths[i];

			var down = gapY * (cell.SpanY - 1);
			for ( var i = cell.Y; i < cell.Y + cell.SpanY && i < rows; i++ ) down += heights[i];

			var rect = new Rect( left, top, across, down );

			if ( cell.Layout is not null )
			{
				cell.Layout.Arrange( rect );
				continue;
			}

			if ( cell.Widget is null || !cell.Widget.IsValid ) continue;

			cell.Widget.Position = rect.Position;
			cell.Widget.Size = new Vector2(
				MathX.Clamp( rect.Width, cell.Widget.MinimumWidth, cell.Widget.MaximumWidth ),
				MathX.Clamp( rect.Height, cell.Widget.MinimumHeight, cell.Widget.MaximumHeight ) );

			cell.Widget.SizeFromLayout = true;
		}
	}
}
