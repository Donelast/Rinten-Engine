using Rinten.UI;
using System;

namespace Editor;

/// <summary>A pooled grid of cells, all one size, wrapped to whatever the width fits.</summary>
public abstract class PooledGridList<TCell> : PooledCellList<TCell> where TCell : Panel
{
	protected PooledGridList()
	{
		AddClass( "pooled-grid-list" );
	}

	//How big a cell is, in layout units.
	protected virtual Vector2 CellSize => new( 80.0f, 80.0f );

	//The gap between cells, in layout units.
	protected virtual Vector2 CellGap => Vector2.Zero;

	//How many cells fit across, from the width the list was last measured at.
	protected int Columns { get; private set; } = 1;

	private float ColumnStep => MathF.Max( 1.0f, CellSize.x + CellGap.x );
	private float RowStep => MathF.Max( 1.0f, CellSize.y + CellGap.y );

	protected override void MeasureCells( float width )
	{
		Columns = Math.Max( 1, (int)( ( width + CellGap.x ) / ColumnStep ) );
	}

	protected override float TotalHeight( int count )
	{
		if ( count <= 0 ) return 0.0f;

		var rows = ( count + Columns - 1 ) / Columns;

		return rows * RowStep - CellGap.y;
	}

	protected override void GetWindow( float scrollTop, float viewHeight, int count, out int first, out int pastEnd )
	{
		var step = RowStep;

		var firstRow = (int)( scrollTop / step ) - Overscan;
		var pastRow = (int)( ( scrollTop + viewHeight ) / step ) + 1 + Overscan;

		first = firstRow * Columns;
		pastEnd = pastRow * Columns;
	}

	protected override void PositionCell( TCell cell, int index )
	{
		cell.Style.Left = Length.Pixels( index % Columns * ColumnStep );
		cell.Style.Top = Length.Pixels( index / Columns * RowStep );
	}

	protected override void CellExtent( int index, out float top, out float height )
	{
		top = index / Columns * RowStep;
		height = CellSize.y;
	}
}
