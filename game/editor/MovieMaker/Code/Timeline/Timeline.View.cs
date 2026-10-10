using Rinten.MovieMaker;
using Rinten.UI;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

//Where the timeline is looking, and everything that has to move with it.
partial class Timeline
{
	private readonly SmoothDeltaFloat _smoothZoom = new( 0.3f, 100f );
	private double _zoomOriginTime;

	public float PixelsPerSecond => View.PixelsPerSecond;

	public Rect VisibleRect => View.VisibleRect;

	public MovieTimeRange VisibleTimeRange => View.VisibleTimeRange;

	public MovieTime TimeOffset => View.TimeOffset;

	public MovieTime PixelsToTime( float pixels ) => View.PixelsToTime( pixels );

	public double PixelsToSeconds( float pixels ) => View.PixelsToSeconds( pixels );

	public float TimeToPixels( MovieTime time ) => View.TimeToPixels( time );

	public float TimeToPixels( double seconds ) => View.TimeToPixels( seconds );

	/// <summary>Where a point of the panel is on the timeline.</summary>
	public Vector2 ToScene( Vector2 local ) => View.ToScene( local );

	/// <summary>And the other way.</summary>
	public Vector2 FromScene( Vector2 scene ) => View.FromScene( scene );

	/// <summary>Where a point on screen is on the timeline.</summary>
	public Vector2 ScreenToScene( Vector2 screen ) => View.ToScene( ScreenToLocal( screen ) );

	/// <summary>Where a point on screen is in this panel, in the units everything is placed in.</summary>
	public Vector2 ScreenToLocal( Vector2 screen ) => ScreenPositionToPanelPosition( screen ) * ScaleFromScreen;

	/// <summary>A box across a time range, at the height of an item that is already placed.</summary>
	public Rect GetSceneRect( TimelineItem item, MovieTimeRange timeRange )
	{
		timeRange = timeRange.ClampStart( 0d );

		var min = TimeToPixels( timeRange.Start );
		var max = TimeToPixels( timeRange.End );

		return item.SceneRect with { Left = min, Right = max };
	}

	/// <summary>Jump the view to a place and a scale, cancelling any easing.</summary>
	public void SetView( MovieTime timeOffset, float pixelsPerSecond )
	{
		_smoothZoom.Value = pixelsPerSecond;

		View.PixelsPerSecond = pixelsPerSecond;
		View.Offset = View.Offset with { x = TimeToPixels( timeOffset ) };
	}

	public void ZoomSmooth( float v, float x )
	{
		_zoomOriginTime = PixelsToSeconds( x );

		_smoothZoom.Target = (_smoothZoom.Target * MathF.Pow( 2f, v ))
			.Clamp( TimelineView.MinPixelsPerSecond, TimelineView.MaxPixelsPerSecond );
	}

	private void UpdateZoom()
	{
		if ( !_smoothZoom.Update( RealTime.Delta ) ) return;

		var before = TimeToPixels( _zoomOriginTime );

		View.PixelsPerSecond = _smoothZoom.Value;

		var after = TimeToPixels( _zoomOriginTime );

		PanImmediate( before - after );
	}

	public void PanImmediate( float dx )
	{
		if ( dx == 0f ) return;

		View.Pan( new Vector2( dx, 0f ) );
	}

	public void ScrollImmediate( float dy )
	{
		if ( dy == 0f ) return;

		View.Pan( new Vector2( 0f, dy ) );
	}

	public void PanToPlayheadTime()
	{
		var margin = PixelsToTime( PlayheadMarginPixels );
		var range = new MovieTimeRange( Session.PlayheadTime - margin, Session.PlayheadTime + margin );

		if ( range.Start < VisibleTimeRange.Start )
		{
			PanImmediate( TimeToPixels( VisibleTimeRange.Start - range.Start ) );
		}
		else if ( range.End > VisibleTimeRange.End )
		{
			PanImmediate( TimeToPixels( VisibleTimeRange.End - range.End ) );
		}
	}

	public void ScrollToTrack( TrackView trackView ) => ScrollToTrack( trackView.Track );

	public void ScrollToTrack( IProjectTrack track )
	{
		if ( _tracks.FirstOrDefault( x => x.View.Track.Id == track.Id ) is not { } timelineTrack ) return;

		var trackRect = timelineTrack.SceneRect;
		var visibleRect = VisibleRect.Shrink( 0f, TimelineView.VerticalMargin );

		if ( trackRect.Top < visibleRect.Top )
		{
			ScrollImmediate( visibleRect.Top - trackRect.Top );
		}
		else if ( trackRect.Bottom > visibleRect.Bottom )
		{
			ScrollImmediate( visibleRect.Bottom - trackRect.Bottom );
		}
	}

	//Applies the view: pans via the layer transform; only re-lays out items when the scale changes
	private void OnViewChanged( TimelineViewChange change )
	{
		if ( change.HasFlag( TimelineViewChange.Scale ) )
		{
			Session.Cookies.PixelsPerSecond = View.PixelsPerSecond;
		}

		if ( change.HasFlag( TimelineViewChange.Offset ) )
		{
			var transform = new PanelTransform();

			transform.AddTranslate( Length.Pixels( -View.Offset.x ), Length.Pixels( -View.Offset.y ) );

			_pan.Style.Transform = transform;
		}

		_background.MarkDirty();
		_grid.MarkDirty();

		ScrubBarTop.MarkDirty();
		ScrubBarBottom.MarkDirty();

		_playhead.UpdatePosition();
		_preview.UpdatePosition();

		_scrollBar.UpdatePosition();

		if ( (change & TimelineViewChange.Layout) != 0 )
		{
			foreach ( var track in _tracks )
			{
				track.UpdateLayout();
			}
		}

		if ( IsDragging )
		{
			DragTo( _lastMouseScenePos );
		}

		ViewChanged?.Invoke( View.VisibleRect );
		Session.EditMode?.ViewChanged( View.VisibleRect );
	}
}
