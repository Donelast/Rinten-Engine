using Rinten;
using Rinten.MovieMaker;
using Rinten.UI;
using System.Collections.Generic;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>The playhead, or the lighter line that follows the pointer.</summary>
public sealed class TimeCursorPanel : Panel, ISnapSource
{

	private readonly Timeline _timeline;
	private readonly bool _snap;

	private readonly Panel _line;
	private readonly TimeCursorLabel _timeLabel;
	private readonly TimeCursorLabel _frameLabel;

	private MovieTime _value;

	public Color Color { get; }

	/// <summary>How long the label stays up after the cursor moves. Zero keeps it up.</summary>
	public float LabelFadeTime { get; init; }

	public RealTimeSince ShowLabelTime { get; private set; }

	public MovieTime Value
	{
		get => _value;
		set
		{
			if ( _value != value )
			{
				ShowLabelTime = 0f;
			}

			_value = value;

			UpdatePosition();
		}
	}

	public TimeCursorPanel( Timeline timeline, Color color, bool snap )
	{
		_timeline = timeline;
		_snap = snap;

		Color = color;

		AddClass( "time-cursor" );

		_line = AddChild<Panel>( "line" );
		_line.Style.BackgroundColor = color.WithAlpha( 0.5f );

		AddChild( new BookmarkPanel( color, pointsDown: true ) ).AddClass( "marker" );
		AddChild( new BookmarkPanel( color, pointsDown: false ) ).AddClass( "marker" );

		_timeLabel = AddChild( new TimeCursorLabel( color ) );
		_timeLabel.AddClass( "top" );

		_frameLabel = AddChild( new TimeCursorLabel( color ) );
		_frameLabel.AddClass( "bottom" );

		UpdatePosition();
	}

	public void UpdatePosition()
	{
		var view = _timeline.View;
		var x = view.FromScene( new Vector2( view.TimeToPixels( _value ), 0f ) ).x;

		Style.Left = Length.Pixels( x );
		Style.Top = Length.Pixels( TimelineView.VerticalMargin * 0.25f );
		Style.Height = Length.Pixels( Math.Max( 0f, view.Size.y - TimelineView.VerticalMargin * 0.5f ) );

		_timeLabel.Text = _value.ToString();
		_frameLabel.Text = _value.GetFrameIndex( _timeline.Session.FrameRate ).ToString( "0000" );
	}

	public override void Tick()
	{
		base.Tick();

		if ( LabelFadeTime <= 0f ) return;

		var alpha = 1f - Math.Clamp( ShowLabelTime / LabelFadeTime * 4f - 3f, 0f, 1f );

		_timeLabel.Style.Opacity = alpha;
		_frameLabel.Style.Opacity = alpha;
	}

	Rect ISnapSource.SceneSnapBounds => _timeline.View.VisibleRect;

	IEnumerable<SnapTarget> ISnapSource.GetSnapTargets( MovieTime sourceTime, bool isPrimary ) =>
		_snap ? [_value] : [];

	//The time written beside the cursor.
	private sealed class TimeCursorLabel : Panel
	{
		private readonly Rinten.UI.Label _label;

		public TimeCursorLabel( Color color )
		{
			AddClass( "time-cursor-label" );

			_label = AddChild<Rinten.UI.Label>();
			_label.Style.FontColor = color;
		}

		public string Text
		{
			set => _label.Text = value;
		}
	}
}
