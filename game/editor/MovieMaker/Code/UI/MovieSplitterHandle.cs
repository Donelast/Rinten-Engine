using Rinten.UI;
using System;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>Which side of a handle the panel it resizes is on.</summary>
public enum SplitterSide
{
	Left,
	Right
}

//Only one of the two panels is given a width; the other grows into whatever is left. So there is never a second number to keep in step with the first, and a section that is hidden takes its handle with it without the rest re-measuring.
/// <summary>The line between two sections of the editor, which you can drag.</summary>
[StyleSheet( "MovieSplitterHandle.cs.scss" )]
public sealed class MovieSplitterHandle : Panel
{
	private Panel? _sized;
	private Panel? _flexible;
	private SplitterSide _side;
	private string? _cookie;
	private float _minimum;
	private float _flexibleMinimum;

	private float _startPointerX;
	private float _startWidth;

	public MovieSplitterHandle()
	{
		AddClass( "movie-splitter" );
	}

	/// <summary>Says what this handle moves.</summary>
	public MovieSplitterHandle Init( Panel sized, SplitterSide side, Panel flexible, string cookie,
		float minimum = 200f, float flexibleMinimum = 200f )
	{
		_sized = sized;
		_flexible = flexible;
		_side = side;
		_cookie = cookie;
		_minimum = minimum;
		_flexibleMinimum = flexibleMinimum;

		sized.AddClass( "splitter-sized" );
		flexible.AddClass( "splitter-flexible" );

		var stored = EditorCookie.Get( cookie, 0f );

		Apply( stored > 0f ? stored : minimum );

		return this;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton != MouseButtons.Left ) return;
		if ( _sized is not { } sized ) return;

		SetClass( "dragging", true );

		_startPointerX = e.PointerPosition.x;
		_startWidth = sized.Box.Rect.Width * sized.ScaleFromScreen;

		e.StopPropagation();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( !HasClass( "dragging" ) ) return;
		if ( _flexible is null ) return;

		var scale = _flexible.ScaleFromScreen;
		var pointerDelta = (e.PointerPosition.x - _startPointerX) * scale;
		var delta = _side == SplitterSide.Left ? pointerDelta : -pointerDelta;
		var maximum = Math.Max( _minimum, _startWidth + _flexible.Box.Rect.Width * scale - _flexibleMinimum );

		Apply( Math.Clamp( _startWidth + delta, _minimum, maximum ) );
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		if ( !HasClass( "dragging" ) ) return;

		SetClass( "dragging", false );

		if ( _cookie is not null && _sized is { } sized )
		{
			EditorCookie.Set( _cookie, sized.Box.Rect.Width * sized.ScaleFromScreen );
		}
	}

	//Widths are kept in panel units, the same ones Style.Width is written in;
	//Box.Rect is screen pixels, so anything measured is scaled on the way in.
	private void Apply( float width )
	{
		if ( _sized is not { } sized ) return;

		sized.Style.Width = Length.Pixels( width );
	}
}
