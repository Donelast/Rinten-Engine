using Rinten;
using Rinten.MovieMaker;
using Rinten.UI;
using System.Collections.Generic;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

/// <summary>A fixed width slice of a curve block, painted once and then left alone.</summary>
public sealed class CurveTilePanel : BitmapPanel
{
	public const int TileWidth = 256;

	/// <summary>Controls curve resolution based on zoom level.</summary>
	[FromTheme( Key = "CurveTilePixelsPerSample" )]
	public static float PixelsPerSample { get; set; } = 2f;

	[field: ThreadStatic]
	private static List<MovieTime>? SampleTimes { get; set; }

	private readonly List<Vector2>[] _lines;

	//One buffer for every element in the tile: they are sampled at the same times, so they are
	//the same length, and it is only made again when the zoom changes how many samples there are.
	private Vector2[] _points = [];
	private readonly (float Min, float Max)[] _ranges;

	private MovieTimeRange _paintedTimeRange;
	private MovieTimeRange _rangesTimeRange;

	/// <summary>The block this tile is a slice of.</summary>
	public ICurveBlockItem BlockItem { get; }

	/// <summary>Which tile this is in the block. The first is zero.</summary>
	public int Index { get; }

	/// <summary>What part of the movie this tile covers.</summary>
	public MovieTimeRange TimeRange { get; set; }

	/// <summary>Min and max value for each decomposed element within this tile's time range. Will always be up to date.</summary>
	public IReadOnlyList<(float Min, float Max)> Ranges
	{
		get
		{
			UpdateRanges();

			return _ranges;
		}
	}

	public CurveTilePanel( ICurveBlockItem blockItem, int index )
	{
		BlockItem = blockItem;
		Index = index;

		AddClass( "curve-tile" );

		var elementCount = blockItem.Elements.Count;

		_lines = new List<Vector2>[elementCount];
		_ranges = new (float Min, float Max)[elementCount];

		for ( var i = 0; i < elementCount; ++i )
		{
			_lines[i] = [];
		}
	}

	/// <summary>The values under part of this tile changed, so it has to be painted again.</summary>
	public void MarkDirty( MovieTimeRange timeRange )
	{
		if ( timeRange.Intersect( TimeRange ) is not { IsEmpty: false } )
		{
			return;
		}

		_paintedTimeRange = default;
		_rangesTimeRange = default;

		MarkDirty();
	}

	/// <summary>Puts this tile at its place along the block.</summary>
	public void Place( float left, float width )
	{
		Style.Left = Length.Pixels( left );
		Style.Width = Length.Pixels( width );
		Style.Height = Length.Pixels( MovieLayout.BlockHeight );
	}

	protected override Vector2Int BitmapSize => new( TileWidth, MovieLayout.BlockHeight );

	protected override void OnDrawBitmap( Bitmap bitmap )
	{
		_paintedTimeRange = TimeRange;

		var elementCount = BlockItem.Elements.Count;

		if ( elementCount <= 0 ) return;

		var times = SampleTimes ??= new List<MovieTime>();

		times.Clear();

		GetCurveTimes( times );

		if ( times.Count == 0 ) return;

		var ranges = BlockItem.Ranges;

		for ( var i = 0; i < elementCount; ++i )
		{
			_lines[i].Clear();
		}

		const float margin = 2f;

		var height = MovieLayout.BlockHeight - margin * 2f;

		Span<float> mids = stackalloc float[elementCount];

		var range = 0f;

		for ( var j = 0; j < elementCount; ++j )
		{
			range = Math.Max( range, ranges[j].Max - ranges[j].Min );

			mids[j] = (ranges[j].Min + ranges[j].Max) * 0.5f;
		}

		var scale = range <= 0f ? 0f : height / range;

		var t0 = TimeRange.Start;
		var t1 = TimeRange.End;

		var dxdt = TileWidth / (t1 - t0).TotalSeconds;

		Span<float> floats = stackalloc float[elementCount];

		foreach ( var t in times )
		{
			BlockItem.Read( t - BlockItem.Offset, floats );

			var x = (float)((t - t0).TotalSeconds * dxdt);

			for ( var j = 0; j < elementCount; ++j )
			{
				var y = (mids[j] - floats[j]) * scale + 0.5f * height + margin;

				_lines[j].Add( new Vector2( x, y ) );
			}
		}

		bitmap.SetAntialias( true );

		for ( var i = 0; i < elementCount; ++i )
		{
			if ( _lines[i].Count < 2 ) continue;

			bitmap.SetPen( BlockItem.Elements[i].Color.WithAlpha( 0.25f ), 2f );
			bitmap.DrawLines( Exact( _lines[i] ) );
		}
	}

	/// <summary>Paints, but only what the window can show.</summary>
	public override void Tick()
	{
		if ( TimeRange.Intersect( BlockItem.VisibleTimeRange ) is not { IsEmpty: false } ) return;

		if ( _paintedTimeRange != TimeRange )
		{
			MarkDirty();
		}

		base.Tick();
	}

	//The points as an array of exactly their own length, without making one each time.
	private Vector2[] Exact( List<Vector2> points )
	{
		if ( _points.Length != points.Count )
		{
			_points = new Vector2[points.Count];
		}

		points.CopyTo( _points );

		return _points;
	}

	private void UpdateRanges()
	{
		if ( _rangesTimeRange == TimeRange ) return;

		_rangesTimeRange = TimeRange;

		var elementCount = BlockItem.Elements.Count;

		if ( elementCount <= 0 ) return;

		for ( var i = 0; i < elementCount; ++i )
		{
			_ranges[i].Min = BlockItem.Elements[i].Min ?? float.PositiveInfinity;
			_ranges[i].Max = BlockItem.Elements[i].Max ?? float.NegativeInfinity;
		}

		var times = SampleTimes ??= new List<MovieTime>();

		times.Clear();

		GetCurveTimes( times );

		if ( times.Count == 0 ) return;

		Span<float> floats = stackalloc float[elementCount];

		foreach ( var t in times )
		{
			if ( t < _rangesTimeRange.Start ) continue;
			if ( t > _rangesTimeRange.End ) break;

			BlockItem.Read( t - BlockItem.Offset, floats );

			for ( var i = 0; i < elementCount; ++i )
			{
				_ranges[i].Min = Math.Min( _ranges[i].Min, floats[i] );
				_ranges[i].Max = Math.Max( _ranges[i].Max, floats[i] );
			}
		}
	}

	private void GetCurveTimes( List<MovieTime> times )
	{
		var timeRange = TimeRange;

		times.Add( timeRange.Start );

		var sampleRate = Math.Max( (int)Math.Round( TileWidth / (TimeRange.Duration.TotalSeconds * PixelsPerSample) ), 1 );

		foreach ( var hintRange in BlockItem.GetPaintHints( timeRange ) )
		{
			TryAddTime( hintRange.Start );

			//We subtract epsilon from the end so we can more accurately see sudden value changes going into the next paint hint time range

			var clamped = hintRange with { End = hintRange.End - MovieTime.Epsilon };

			foreach ( var time in clamped.GetSampleTimes( sampleRate ) )
			{
				TryAddTime( time );
			}

			TryAddTime( hintRange.End - MovieTime.Epsilon );
		}

		TryAddTime( timeRange.End );
		return;

		void TryAddTime( MovieTime time )
		{
			time += BlockItem.Offset;

			if ( time < timeRange.Start ) return;
			if ( time > timeRange.End ) return;

			if ( times[^1] < time )
			{
				times.Add( time );
			}
		}
	}
}
