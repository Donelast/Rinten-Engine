using Rinten.MovieMaker;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

partial class Timeline
{
	private readonly List<ISnapSource> _snapSourceBuffer = new();

	private static MovieTime ClampTime( MovieTime time, MovieTime? min, MovieTime? max )
	{
		if ( min is { } minTime )
		{
			time = MovieTime.Max( time, minTime );
		}

		if ( max is { } maxTime )
		{
			time = MovieTime.Min( time, maxTime );
		}

		return time;
	}

	public MovieTime ScenePositionToTime( Vector2 scenePos, SnapOptions? options = null, bool showSnap = true )
	{
		options ??= new SnapOptions();

		var time = ClampTime( PixelsToTime( scenePos.x ), options.Min, options.Max );

		_snapSourceBuffer.Clear();
		_snapSourceBuffer.Add( this );

		if ( Session.ObjectSnap )
		{
			_snapSourceBuffer.AddRange( GetSnapSources( scenePos, options ) );
		}

		var primaryOffset = options.SnapOffsets.DefaultIfEmpty().MinBy( x => x.Absolute );
		var ctx = new SnapContext(
			targetTime: time,
			targetRange: (options.Min ?? default, options.Max ?? MovieTime.MaxValue),
			maxDistance: PixelsToTime( 16f ),
			sources: _snapSourceBuffer );

		foreach ( var offset in options.SnapOffsets.DefaultIfEmpty() )
		{
			ctx.Update( offset, offset == primaryOffset );
		}

		if ( showSnap )
		{
			UpdateSnapTargets( ctx.BestSources.Select( x => (x.Key, x.Value) ) );
		}

		return MovieTime.Max( ctx.BestTime, MovieTime.Zero );
	}

	//Only things the pointer is level with, and only those the caller allows.
	private IEnumerable<ISnapSource> GetSnapSources( Vector2 scenePos, SnapOptions options )
	{
		foreach ( var source in SnapSources )
		{
			var bounds = source.SceneSnapBounds;

			if ( bounds.Top > scenePos.y || bounds.Bottom < scenePos.y ) continue;

			if ( options.Filter?.Invoke( source ) is false ) continue;

			yield return source;
		}
	}

	private sealed class SnapContext
	{
		public MovieTime TargetTime { get; }
		public MovieTimeRange TargetRange { get; }
		public MovieTime MaxDistance { get; }
		public IReadOnlyList<ISnapSource> Sources { get; }

		public SnapContext( MovieTime targetTime, MovieTimeRange targetRange, MovieTime maxDistance, IEnumerable<ISnapSource> sources )
		{
			TargetTime = targetTime;
			TargetRange = targetRange;
			MaxDistance = maxDistance;
			Sources = [.. sources];

			BestTime = targetTime;
		}

		public MovieTime BestTime { get; private set; }
		public float BestScore { get; private set; } = float.PositiveInfinity;
		public Dictionary<MovieTime, Rect> BestSources { get; } = new();

		public bool Update( MovieTime offset, bool isPrimary )
		{
			var changed = false;

			foreach ( var source in Sources )
			{
				foreach ( var (snapTime, priority, show) in source.GetSnapTargets( TargetTime + offset, isPrimary ) )
				{
					var snappedTime = snapTime - offset;

					if ( !TargetRange.Contains( snappedTime ) ) continue;

					var timeDiff = (snappedTime - TargetTime).Absolute;
					var force = priority < 0;

					if ( !force && timeDiff * Math.Max( 4 - priority, 1 ) > MaxDistance * 4 ) continue;

					var score = (float)(timeDiff.TotalSeconds / MaxDistance.TotalSeconds) - priority;

					if ( score < BestScore )
					{
						if ( snappedTime != BestTime )
						{
							BestSources.Clear();
						}

						BestScore = score;
						BestTime = snappedTime;

						changed = true;
					}

					if ( !show || snappedTime != BestTime ) continue;

					var rect = source.SceneSnapBounds;

					if ( BestSources.TryGetValue( snapTime, out var existing ) )
					{
						rect.Add( existing );
					}

					BestSources[snapTime] = rect;
				}
			}

			return changed;
		}
	}
}
