using Rinten.UI;
using System;

namespace Editor;

/// <summary>A pooled list of rows, all one height.</summary>
public abstract class PooledRowList<TCell> : PooledCellList<TCell> where TCell : Panel
{
	protected PooledRowList()
	{
		AddClass( "pooled-row-list" );
	}

	//How tall a row is, in layout units.
	protected virtual float RowHeight => 20.0f;

	protected override void MeasureCells( float width ) { }

	protected override float TotalHeight( int count ) => count * MathF.Max( 1.0f, RowHeight );

	protected override void GetWindow( float scrollTop, float viewHeight, int count, out int first, out int pastEnd )
	{
		var step = MathF.Max( 1.0f, RowHeight );

		first = (int)( scrollTop / step ) - Overscan;
		pastEnd = (int)( ( scrollTop + viewHeight ) / step ) + 1 + Overscan;
	}

	protected override void PositionCell( TCell cell, int index )
	{
		cell.Style.Top = Length.Pixels( index * MathF.Max( 1.0f, RowHeight ) );
	}

	protected override void CellExtent( int index, out float top, out float height )
	{
		height = MathF.Max( 1.0f, RowHeight );
		top = index * height;
	}
}
