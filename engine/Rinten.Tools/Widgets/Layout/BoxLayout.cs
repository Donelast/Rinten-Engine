using Native;
using Rinten;
using System;

namespace Editor;

/// <summary>A widget layout. You can think of it as an invisible box of rows or columns, each one containing a widget, useful for automatic positioning and scaling.</summary>
public sealed class BoxLayout : Layout
{
	//Which way a box runs, and from which end.
	internal enum Direction
	{
		LeftToRight,
		RightToLeft,
		TopToBottom,
		BottomToTop
	}


	internal BoxLayout( Direction direction, Widget parent )
		: base()
	{
		Vertical = direction is Direction.TopToBottom or Direction.BottomToTop;
		Reversed = direction is Direction.RightToLeft or Direction.BottomToTop;

		if ( parent is not null ) SetOwner( parent );
	}

	/// <summary>Add a spacing item</summary>
	public override void AddSpacingCell( float size )
	{
		AddCell( new Cell { Spacing = size } );
	}

	/// <summary>Add a stretch item</summary>
	public override void AddStretchCell( int stretch = 0 )
	{
		if ( stretch < 0 )
			stretch = 0;

		AddCell( new Cell { Stretch = stretch } );
	}

	public int GetCellStretch( int index )
	{
		return index >= 0 && index < Cells.Count ? Cells[index].Stretch : 0;
	}

	public void SetCellStretch( int index, int stretch )
	{
		if ( index < 0 || index >= Cells.Count ) return;

		var cell = Cells[index];
		cell.Stretch = stretch;
		Cells[index] = cell;

		OnGeometryChanged();
	}

	public void SetCellStretch( Widget widget, int stretch )
	{
		if ( !widget.IsValid() ) throw new ArgumentNullException( nameof( widget ) );

		SetCellStretch( Cells.FindIndex( x => x.Widget == widget ), stretch );
	}
	public void SetCellStretch( Layout layout, int stretch )
	{
		if ( !layout.IsValid() ) throw new ArgumentNullException( nameof( layout ) );

		SetCellStretch( Cells.FindIndex( x => x.Layout == layout ), stretch );
	}

	public override T Add<T>( T widget, int stretch = 0 )
	{
		if ( !widget.IsValid() ) throw new ArgumentNullException( nameof( widget ) );

		AddCell( new Cell { Widget = widget, Stretch = stretch } );
		return widget;
	}

	public override Layout Add( Layout layout )
	{
		if ( !layout.IsValid() ) throw new ArgumentNullException( nameof( layout ) );

		AddCell( new Cell { Layout = layout } );
		return layout;
	}

	public override Layout Add( Layout layout, int stretch )
	{
		if ( !layout.IsValid() ) throw new ArgumentNullException( nameof( layout ) );

		AddCell( new Cell { Layout = layout, Stretch = stretch } );
		return layout;
	}

}
