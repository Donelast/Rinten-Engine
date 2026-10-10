using Rinten.MovieMaker;
using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>What is behind the tracks: the movie's own length, and the part of it the open sequence covers.</summary>
public sealed class TimelineBackgroundPanel : Panel
{
	private readonly Timeline _timeline;
	private readonly Panel _outer;
	private readonly Panel _inner;

	private int _lastState;

	public TimelineBackgroundPanel( Timeline timeline )
	{
		_timeline = timeline;

		AddClass( "timeline-background" );

		_outer = AddChild<Panel>( "range" );
		_inner = AddChild<Panel>( "range" );

		MarkDirty();
	}

	public void MarkDirty() => _lastState = 0;

	public void Frame()
	{
		var session = _timeline.Session;
		var state = HashCode.Combine( _timeline.PixelsPerSecond, _timeline.TimeOffset, session.Duration, session.SequenceTimeRange );

		if ( state == _lastState ) return;

		_lastState = state;

		if ( session.SequenceTimeRange is { } sequenceRange )
		{
			Place( _outer, (MovieTime.Zero, session.Duration), Timeline.OuterColor );
			Place( _inner, sequenceRange, Timeline.InnerColor );
		}
		else
		{
			Place( _outer, (MovieTime.Zero, session.Duration), Timeline.InnerColor );

			_inner.SetClass( "hidden", true );
		}
	}

	private void Place( Panel panel, MovieTimeRange timeRange, Color color )
	{
		var view = _timeline.View;

		var left = view.FromScene( new Vector2( view.TimeToPixels( timeRange.Start ), 0f ) ).x;
		var right = view.FromScene( new Vector2( view.TimeToPixels( timeRange.End ), 0f ) ).x;

		panel.SetClass( "hidden", false );
		panel.Style.Left = Length.Pixels( left );
		panel.Style.Width = Length.Pixels( Math.Max( 0f, right - left ) );
		panel.Style.BackgroundColor = color;
	}
}
