using Rinten.MovieMaker;
using Rinten.UI;
using System;

namespace Editor.MovieMaker;

#nullable enable

//Three things you can drag along it - where the export starts, where it ends, and which frame the preview shows - and the closest one to the pointer is the one that moves.
/// <summary>Progress bar and time range picker under the preview in the export window.</summary>
public sealed class VideoExportTimelinePanel : Panel
{
	private const float HoverMargin = 4f;

	private enum Element
	{
		Start,
		End,
		Playhead
	}

	private readonly VideoExportWindow _window;
	private readonly Panel _track;
	private readonly Panel _range;
	private readonly Panel _rendered;
	private readonly Panel _playhead;

	private Element? _dragged;

	public VideoExportTimelinePanel( VideoExportWindow window )
	{
		_window = window;

		AddClass( "export-timeline" );

		_track = AddChild<Panel>( "track" );
		_range = _track.AddChild<Panel>( "range" );
		_rendered = _track.AddChild<Panel>( "rendered" );

		_playhead = _track.AddChild<Panel>( "playhead" );
		_playhead.AddChild( new BookmarkPanel( Theme.Yellow, pointsDown: true, 7, 6, 3f ) ).AddClass( "top" );
		_playhead.AddChild( new BookmarkPanel( Theme.Yellow, pointsDown: false, 7, 6, 3f ) ).AddClass( "bottom" );
	}

	public override void Tick()
	{
		base.Tick();

		Refresh();
	}

	/// <summary>Moves the marks to where the times now are.</summary>
	public void Refresh()
	{
		Place( _range, _window.TimeRange );
		Place( _rendered, (_window.RenderStartTime, _window.LastRenderedTime) );

		_playhead.Style.Left = Length.Percent( Fraction( _window.PreviewTime ) * 100f );
	}

	private void Place( Panel panel, MovieTimeRange timeRange )
	{
		var left = Fraction( timeRange.Start );
		var right = Fraction( timeRange.End );

		panel.Style.Left = Length.Percent( left * 100f );
		panel.Style.Width = Length.Percent( Math.Max( 0f, right - left ) * 100f );
	}

	private float Fraction( MovieTime time ) => (float)_window.Session.Duration.GetFraction( time );

	private MovieTime TimeAt( Vector2 screenPosition )
	{
		var rect = _track.Box.Rect;

		if ( rect.Width <= 0f ) return MovieTime.Zero;

		var local = _track.ScreenPositionToPanelPosition( screenPosition );

		return MovieTime.Lerp( 0d, _window.Session.Duration, Math.Clamp( local.x / rect.Width, 0f, 1f ) );
	}

	private Element? ElementAt( Vector2 screenPosition )
	{
		var rect = _track.Box.Rect;

		if ( rect.Width <= 0f ) return null;

		var local = _track.ScreenPositionToPanelPosition( screenPosition );

		Element? best = null;
		var bestDist = HoverMargin * ScaleToScreen;

		foreach ( var element in Enum.GetValues<Element>() )
		{
			var x = Fraction( TimeOf( element ) ) * rect.Width;
			var dist = Math.Abs( x - local.x );

			if ( dist >= bestDist ) continue;

			bestDist = dist;
			best = element;
		}

		return best;
	}

	private MovieTime TimeOf( Element element ) => element switch
	{
		Element.Start => _window.TimeRange.Start,
		Element.End => _window.TimeRange.End,
		_ => _window.PreviewTime
	};

	private void SetTimeOf( Element element, MovieTime time )
	{
		switch ( element )
		{
			case Element.Start:
				_window.StartTime = time.TotalSeconds;
				break;

			case Element.End:
				_window.EndTime = time.TotalSeconds;
				break;
		}

		_window.PreviewTime = time;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		_dragged = ElementAt( e.PointerPosition ) ?? Element.Playhead;

		e.StopPropagation();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( _dragged is not { } element )
		{
			Style.Cursor = ElementAt( e.PointerPosition ) is not null
				? MovieCursor.ResizeHorizontal
				: MovieCursor.Default;

			return;
		}

		var snap = (Application.KeyboardModifiers & KeyboardModifiers.Ctrl) != 0
			? MovieTime.FromSeconds( 0.1 )
			: MovieTime.FromFrames( 1, _window.Config.FrameRate );

		SetTimeOf( element, TimeAt( e.PointerPosition ).Round( snap ) );

		_ = _window.UpdatePreviewAsync( true );
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		_dragged = null;
	}
}
