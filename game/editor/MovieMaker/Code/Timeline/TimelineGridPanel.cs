using Rinten;
using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>The ticks running down the timeline, major over minor.</summary>
public sealed class TimelineGridPanel : Panel
{
	private const float MajorMargin = 8f;
	private const float MinorMargin = 16f;

	private readonly Timeline _timeline;
	private readonly GridLinesPanel _major;
	private readonly GridLinesPanel _minor;

	public TimelineGridPanel( Timeline timeline )
	{
		_timeline = timeline;

		AddClass( "timeline-grid" );

		_major = AddChild( new GridLinesPanel( 2f, MajorMargin ) );
		_minor = AddChild( new GridLinesPanel( 1f, MinorMargin ) );

		MarkDirty();
	}

	public void MarkDirty()
	{
		_major.Interval = _timeline.TimeToPixels( _timeline.MajorTick.Interval );
		_minor.Interval = _timeline.TimeToPixels( _timeline.MinorTick.Interval );

		_major.Offset = _timeline.View.Offset.x;
		_minor.Offset = _timeline.View.Offset.x;
	}
}

//One line is painted into a tile as wide as the gap between two of them, and the GPU repeats it across the panel. Panning moves the tile rather than repainting it, so the only thing a scroll costs is a background position.
/// <summary>One run of evenly spaced lines.</summary>
public sealed class GridLinesPanel : BitmapPanel
{
	private const int TileHeight = 1;

	private readonly float _thickness;

	private float _interval = 16f;
	private float _offset;

	public Color Color { get; set; } = Theme.TextControl.WithAlpha( 0.02f );

	/// <summary>How far apart the lines are, in scene pixels.</summary>
	public float Interval
	{
		get => _interval;
		set
		{
			value = Math.Max( 1f, value );

			if ( _interval.AlmostEqual( value ) ) return;

			_interval = value;

			MarkDirty();
			Apply();
		}
	}

	/// <summary>Where the timeline is looking, so the lines stay on their times.</summary>
	public float Offset
	{
		get => _offset;
		set
		{
			if ( _offset.AlmostEqual( value ) ) return;

			_offset = value;

			Apply();
		}
	}

	public GridLinesPanel( float thickness, float margin )
	{
		_thickness = thickness;

		AddClass( "grid-lines" );

		Style.Top = Length.Pixels( margin );
		Style.Bottom = Length.Pixels( margin );

		Apply();
	}

	protected override bool StretchToFit => false;

	protected override Vector2Int BitmapSize => new( Math.Max( 1, _interval.CeilToInt() ), TileHeight );

	protected override void OnDrawBitmap( Bitmap bitmap )
	{
		bitmap.SetPen( Color, _thickness );
		bitmap.DrawLine( new Vector2( 0f, -1f ), new Vector2( 0f, TileHeight + 1f ) );
	}

	private void Apply()
	{
		Style.BackgroundSizeX = Length.Pixels( _interval );
		Style.BackgroundPositionX = Length.Pixels( -_offset % _interval );
	}
}
