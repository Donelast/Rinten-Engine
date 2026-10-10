using Rinten;
using Rinten.UI;
using System;

namespace Editor;

/// <summary>A scroll area: a window onto a widget that is bigger than it.</summary>
public partial class ScrollArea
{
	private Vector2 scroll;

	//How far it can be scrolled before the canvas runs out.
	private Vector2 Range
	{
		get
		{
			if ( _canvas is null ) return 0;

			return new Vector2(
				MathF.Max( _canvas.Size.x - Size.x, 0.0f ),
				MathF.Max( _canvas.Size.y - Size.y, 0.0f ) );
		}
	}

	//Where the canvas has been scrolled to, in pixels.
	internal Vector2 ScrollOffset
	{
		get => scroll;
		set
		{
			var range = Range;

			var wanted = new Vector2(
				Math.Clamp( value.x, 0.0f, range.x ),
				Math.Clamp( value.y, 0.0f, range.y ) );

			if ( scroll == wanted ) return;

			scroll = wanted;

			PlaceCanvas();
			Update();
		}
	}

	//Puts the canvas where the scroll says, and makes it at least as big as the area - a canvas narrower than its window would leave a gap.
	private void PlaceCanvas()
	{
		if ( !OnPanels || _canvas is null ) return;

		//Measure the canvas like a layout cell; asking the widget returns its default size
		var cell = new Layout.Cell { Widget = _canvas };

		var wanted = new Vector2(
			Layout.WantsOf( cell, false ),
			Layout.WantsOf( cell, true ) );

		//An expanding canvas fits the viewport on that axis; otherwise it takes its wanted size and scrolls
		var fitWidth = horizontalMode == ScrollbarMode.Off
			|| _canvas.HorizontalSizeMode.HasFlag( SizeMode.Expand );

		var width = fitWidth
			? MathF.Max( Size.x, _canvas.MinimumWidth )
			: MathF.Max( wanted.x, Size.x );

		var height = _canvas.VerticalSizeMode.HasFlag( SizeMode.Expand )
			? MathF.Max( Size.y, _canvas.MinimumHeight )
			: MathF.Max( wanted.y, Size.y );

		_canvas.Size = new Vector2( width, height );

		_canvas.Position = -scroll;
	}

	protected override void OnResize()
	{
		base.OnResize();

		if ( !OnPanels ) return;

		//Whatever the area can no longer scroll to is scrolled back from.
		ScrollOffset = scroll;

		PlaceCanvas();
	}

	//Re-measure the canvas on every layout, since content can grow after it was set
	private protected override void OnArrange()
	{
		base.OnArrange();

		if ( !OnPanels ) return;

		PlaceCanvas();
	}

	//What the canvas last asked to be no smaller than.
	private Vector2 placedFor;

	private protected override void OnWidgetFrame()
	{
		base.OnWidgetFrame();

		if ( !OnPanels ) return;
		if ( _canvas is null || !_canvas.IsValid ) return;

		//A canvas that draws itself rather than holding widgets - a grid of icons, a list of rows - says how much room it needs through its
		//minimum size, and nothing in the layout is told when that changes. So it is asked, once a frame, and the answer costs two floats.
		var minimum = new Vector2( _canvas.MinimumWidth, _canvas.MinimumHeight );

		if ( minimum == placedFor ) return;

		placedFor = minimum;

		PlaceCanvas();
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		if ( !OnPanels ) { base.OnMouseWheel( e ); return; }

		var range = Range;

		if ( range.y <= 0.0f && range.x <= 0.0f ) return;

		var lines = e.Delta / 120.0f * Theme.RowHeight * 3.0f;

		var before = scroll;

		ScrollOffset = range.y > 0.0f
			? scroll + new Vector2( 0, -lines )
			: horizontalMode == ScrollbarMode.Off
				? scroll
				: scroll + new Vector2( -lines, 0 );

		//Only ours if it actually moved; a list at its end lets the thing underneath it scroll instead.
		if ( scroll != before ) e.Accept();
	}

	//The bar down the side: where there is something to scroll, or always where the area was told to keep one. ScrollArea.VerticalScrollbarMode
	//was remembered and read by nothing, so an area that asked for a bar of its own never got one and one that asked for none got it anyway.
	protected override void OnPaint()
	{
		if ( !OnPanels ) { base.OnPaint(); return; }

		if ( verticalMode == ScrollbarMode.Off ) return;
		if ( _canvas is null || !_canvas.IsValid() ) return;

		var range = Range;
		if ( range.y <= 0.0f && verticalMode != ScrollbarMode.On ) return;

		const float width = 6.0f;

		var height = Size.y * (Size.y / MathF.Max( _canvas.Size.y, 1.0f ));
		height = MathF.Min( MathF.Max( height, 20.0f ), Size.y );

		//Nothing to scroll and a bar asked for anyway: it fills the side and sits at the top, which is what "you are looking at all of it" reads as.
		var at = range.y > 0.0f ? (scroll.y / range.y) * (Size.y - height) : 0.0f;

		//No track behind it: the bar is the only part worth seeing, and it is a veil of white like everything else that lies on a surface.
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( Paint.HasMouseOver ? Theme.InkFaint : Theme.Edge );
		Paint.DrawRect( new Rect( Size.x - width - 1, at, width, height ), width * 0.5f );
	}
}
