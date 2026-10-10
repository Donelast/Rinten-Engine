using Native;
using Rinten;
using System;

namespace Editor;

/// <summary>A widget layout. You can think of it as an invisible box of rows or columns, each one containing a widget, useful for automatic positioning and scaling.</summary>
public partial class GridLayout : Layout
{

	public sealed override T Add<T>( T widget )
	{
		if ( !widget.IsValid() ) throw new ArgumentNullException( nameof( widget ) );
		AddCell( 0, 0, widget );
		return widget;
	}

	public T AddCell<T>( int x, int y, T widget, int xSpan = 1, int ySpan = 1, TextFlag alignment = 0 ) where T : Widget
	{
		if ( !widget.IsValid() ) throw new ArgumentNullException( nameof( widget ) );

		AddPlaced( new Placed { X = x, Y = y, SpanX = xSpan, SpanY = ySpan, Widget = widget } );
		return widget;
	}

	public sealed override Layout Add( Layout layout )
	{
		if ( !layout.IsValid() ) throw new ArgumentNullException( nameof( layout ) );
		AddCell( 0, 0, layout );

		return layout;
	}

	public sealed override Layout Add( Layout layout, int stretch )
	{
		if ( !layout.IsValid() ) throw new ArgumentNullException( nameof( layout ) );
		AddCell( 0, 0, layout );
		return layout;
	}

	public Layout AddCell( int x, int y, Layout layout, int xSpan = 1, int ySpan = 1, TextFlag alignment = 0 )
	{
		if ( !layout.IsValid() ) throw new ArgumentNullException( nameof( layout ) );

		AddPlaced( new Placed { X = x, Y = y, SpanX = xSpan, SpanY = ySpan, Layout = layout } );
		return layout;
	}

	public void SetRowStretch( params float[] values )
	{
		rowStretch = values;
		OnGeometryChanged();
	}

	public void SetColumnStretch( params float[] values )
	{
		columnStretch = values;
		OnGeometryChanged();
	}

	/// <summary>The gap between columns. Set on its own it is the gap both ways, which is what the layout keeps when nothing says otherwise.</summary>
	public float HorizontalSpacing
	{
		get => horizontalSpacing ?? Spacing;
		set
		{
			horizontalSpacing = value;

			OnGeometryChanged();
		}
	}

	/// <summary>The gap between rows.</summary>
	public float VerticalSpacing
	{
		get => verticalSpacing ?? Spacing;
		set
		{
			verticalSpacing = value;

			OnGeometryChanged();
		}
	}

	private float? horizontalSpacing;
	private float? verticalSpacing;

		/// <summary>The smallest a row may be, whatever is in it. An inspector lines its labels up with this: every property is a grid of its own, and they agree on where the controls start only because they agree on this.</summary>
		public void SetMinimumRowHeight( int row, float height )
		{
			SetMinimumRowHeightManaged( row, height );
		}

		public void SetMinimumColumnWidth( int column, float width )
		{
			SetMinimumColumnWidthManaged( column, width );
		}

	/// <summary>Where a cell of the grid ended up.</summary>
	public Rect GetCellRect( int x, int y )
	{
		return CellRectManaged( x, y );
	}
}
