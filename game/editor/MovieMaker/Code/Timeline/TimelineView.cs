using Rinten.MovieMaker;
using System;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>What about the view changed.</summary>
[Flags]
public enum TimelineViewChange
{
	None = 0,

	/// <summary>Where the window is looking.</summary>
	Offset = 1,

	/// <summary>How wide a second is drawn.</summary>
	Scale = 2,

	/// <summary>How big the window is.</summary>
	Size = 4,

	/// <summary>How tall everything in the timeline is.</summary>
	ContentHeight = 8,

	/// <summary>What moves things about, rather than only moving the window over them.</summary>
	Layout = Scale | Size
}

//Only the arithmetic: nothing here knows what a panel is. Time is scaled and the vertical is not, so this is not a zoom in both directions the way a graph has - a second gets wider, a track never gets taller.
/// <summary>Where the timeline is looking, and how wide a second is drawn.</summary>
public sealed class TimelineView
{
	public const float MinPixelsPerSecond = 1f / 4f;
	public const float MaxPixelsPerSecond = 1024f;

	/// <summary>Room left to the left of time zero, so the first frame is not against the edge.</summary>
	public const float LeftMargin = 8f;

	/// <summary>Room above and below the tracks, where the scrub bars sit.</summary>
	public const float VerticalMargin = 48f;

	/// <summary>Raised whenever the view has moved or a second has changed width.</summary>
	public event Action<TimelineViewChange>? Changed;

	private float _pixelsPerSecond = 100f;
	private Vector2 _offset;
	private Vector2 _size;
	private float _contentHeight;

	/// <summary>How many pixels one second of the movie is drawn as.</summary>
	public float PixelsPerSecond
	{
		get => _pixelsPerSecond;
		set
		{
			value = Math.Clamp( value, MinPixelsPerSecond, MaxPixelsPerSecond );

			if ( _pixelsPerSecond.AlmostEqual( value ) ) return;

			_pixelsPerSecond = value;

			Raise( TimelineViewChange.Scale );
		}
	}

	/// <summary>Which point of the timeline the top left of the view is on, in pixels.</summary>
	public Vector2 Offset
	{
		get => _offset;
		set
		{
			value = Clamp( value );

			if ( _offset == value ) return;

			_offset = value;

			Raise( TimelineViewChange.Offset );
		}
	}

	/// <summary>How big the window onto the timeline is.</summary>
	public Vector2 Size
	{
		get => _size;
		set
		{
			if ( _size == value ) return;

			_size = value;

			Raise( TimelineViewChange.Size | ClampOffset() );
		}
	}

	/// <summary>How tall everything in the timeline is, including the margins.</summary>
	public float ContentHeight
	{
		get => _contentHeight;
		set
		{
			if ( _contentHeight.AlmostEqual( value ) ) return;

			_contentHeight = value;

			Raise( TimelineViewChange.ContentHeight | ClampOffset() );
		}
	}

	//Holds the view inside what there is to look at, and says whether that moved it.
	private TimelineViewChange ClampOffset()
	{
		var clamped = Clamp( _offset );

		if ( _offset == clamped ) return TimelineViewChange.None;

		_offset = clamped;

		return TimelineViewChange.Offset;
	}

	private void Raise( TimelineViewChange change ) => Changed?.Invoke( change );

	public Rect VisibleRect => new( _offset, _size );

	public MovieTimeRange VisibleTimeRange => (PixelsToTime( _offset.x ), PixelsToTime( _offset.x + _size.x ));

	public MovieTime TimeOffset => VisibleTimeRange.Start;

	public MovieTime PixelsToTime( float pixels ) => MovieTime.FromSeconds( PixelsToSeconds( pixels ) );

	public double PixelsToSeconds( float pixels ) => pixels / _pixelsPerSecond;

	public float TimeToPixels( MovieTime time ) => TimeToPixels( time.TotalSeconds );

	public float TimeToPixels( double seconds ) => (float)(seconds * _pixelsPerSecond);

	/// <summary>Where a point of the view is in the timeline.</summary>
	public Vector2 ToScene( Vector2 view ) => view + _offset;

	/// <summary>And the other way.</summary>
	public Vector2 FromScene( Vector2 scene ) => scene - _offset;

	/// <summary>Slides the view, in pixels of the screen.</summary>
	public void Pan( Vector2 delta ) => Offset = _offset - delta;

	/// <summary>A second gets wider or narrower, about a point of the view that stays put.</summary>
	public void ZoomTo( float wanted, float viewAnchorX )
	{
		var before = PixelsToSeconds( _offset.x + viewAnchorX );

		PixelsPerSecond = wanted;

		Offset = _offset with { x = TimeToPixels( before ) - viewAnchorX };
	}

	private Vector2 Clamp( Vector2 offset )
	{
		var minY = -VerticalMargin;
		var maxY = Math.Max( minY, _contentHeight - _size.y );

		return new Vector2(
			Math.Max( offset.x, -LeftMargin ),
			Math.Clamp( offset.y, minY, maxY ) );
	}
}
