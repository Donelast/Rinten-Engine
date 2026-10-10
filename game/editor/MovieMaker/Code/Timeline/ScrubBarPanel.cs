using Rinten;
using Rinten.MovieMaker;
using Rinten.UI;
using System.Collections.Generic;

namespace Editor.MovieMaker;

#nullable enable

//Dragging it moves the playhead; holding alt draws the preview loop instead. Dragging past either end keeps going, panning the timeline under the pointer so a long clip can be scrubbed without letting go.
/// <summary>A bar with times and notches on it, above and below the tracks.</summary>
public sealed class ScrubBarPanel : Panel, ISnapSource, IMovieItem
{
	[FromTheme( Key = "ScrubBarTimeLabel" )]
	public static Color TimeLabelColor { get; set; } = Theme.Green.WithAlpha( 0.25f );

	[FromTheme( Key = "ScrubBarHeight" )]
	public static float BarHeight { get; set; } = 24f;

	private readonly Timeline _timeline;
	private readonly Panel _outer;
	private readonly Panel _inner;
	private readonly ScrubRangePanel _pasteRange;
	private readonly ScrubRangePanel _loopRange;
	private readonly List<Rinten.UI.Label> _labels = new();

	private MovieTime _dragStartTime;
	private float _panSpeed;
	private int _lastState;

	private (KeyboardModifiers? Modifiers, Vector2 ScenePos) _lastScrub;

	public Session Session => _timeline.Session;

	public bool IsTop { get; }

	MovieTimeRange IMovieItem.TimeRange => (MovieTime.Zero, MovieTime.MaxValue);

	bool IMovieItem.OverridesMouseEvents => true;

	public ScrubBarPanel( Timeline timeline, bool isTop )
	{
		_timeline = timeline;

		IsTop = isTop;

		AddClass( "scrub-bar" );
		AddClass( isTop ? "top" : "bottom" );

		_outer = AddChild<Panel>( "range outer" );
		_inner = AddChild<Panel>( "range inner" );

		_pasteRange = AddChild( new ScrubRangePanel( "paste", isTop ) );
		_loopRange = AddChild( new ScrubRangePanel( "loop", isTop ) );

		Style.Height = Length.Pixels( BarHeight );

		MarkDirty();
	}

	public void MarkDirty() => _lastState = 0;

	public void Frame()
	{
		Style.Cursor = (Application.KeyboardModifiers & KeyboardModifiers.Alt) != 0
			? MovieCursor.Text
			: MovieCursor.Grab;

		if ( !_panSpeed.AlmostEqual( 0f ) )
		{
			var delta = -_panSpeed * RealTime.Delta;

			_timeline.PanImmediate( delta );

			_lastScrub.ScenePos -= new Vector2( delta, 0f );

			UpdateScrubSpeed( _lastScrub.ScenePos );

			if ( _lastScrub.Modifiers is { } modifiers )
			{
				PostScrub( modifiers, _lastScrub.ScenePos );
			}
		}

		var state = HashCode.Combine( _timeline.PixelsPerSecond, _timeline.TimeOffset, Session.Duration,
			Session.SequenceTimeRange, Session.LoopTimeRange, Session.EditMode?.SourceTimeRange,
			Session.EditMode?.ScrubBarOverrideColor, Session.FrameRate );

		if ( state == _lastState ) return;

		_lastState = state;

		Rebuild();
	}

	private void Rebuild()
	{
		var baseColor = Session.EditMode?.ScrubBarOverrideColor ?? Timeline.BackgroundColor;

		Style.BackgroundColor = baseColor;

		if ( Session.SequenceTimeRange is { } sequenceRange )
		{
			Place( _outer, (MovieTime.Zero, Session.Duration), baseColor.TintRelative( Timeline.BackgroundColor, Timeline.OuterColor ) );
			Place( _inner, sequenceRange, baseColor.TintRelative( Timeline.BackgroundColor, Timeline.InnerColor ) );
		}
		else
		{
			Place( _outer, (MovieTime.Zero, Session.Duration), baseColor.TintRelative( Timeline.BackgroundColor, Timeline.InnerColor ) );

			_inner.SetClass( "hidden", true );
		}

		_pasteRange.Show( _timeline, Session.EditMode?.SourceTimeRange );
		_loopRange.Show( _timeline, Session.LoopTimeRange );

		RebuildLabels();
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

	//Puts a time under every major tick that is on screen, reusing the labels.
	private void RebuildLabels()
	{
		var view = _timeline.View;
		var range = _timeline.VisibleTimeRange;
		var interval = _timeline.MajorTick.Interval;

		if ( interval.IsZero ) return;

		var t0 = MovieTime.Max( (range.Start - interval).Round( interval ), MovieTime.Zero );
		var t1 = t0 + range.Duration + interval * 2;

		var used = 0;

		for ( var t = t0; t <= t1; t += interval, used++ )
		{
			var label = LabelAt( used );
			var x = view.FromScene( new Vector2( view.TimeToPixels( t ), 0f ) ).x;

			label.SetClass( "hidden", false );
			label.Style.Left = Length.Pixels( x + 6f );

			label.Text = IsTop
				? t.ToString()
				: t.GetFrameIndex( Session.FrameRate ).ToString( "0000" );
		}

		for ( var i = used; i < _labels.Count; i++ )
		{
			_labels[i].SetClass( "hidden", true );
		}
	}

	private Rinten.UI.Label LabelAt( int index )
	{
		while ( _labels.Count <= index )
		{
			var created = AddChild<Rinten.UI.Label>( "tick-label" );

			created.Style.FontColor = TimeLabelColor;

			_labels.Add( created );
		}

		return _labels[index];
	}

	//Scrubbing

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		var scenePos = _timeline.ScreenToScene( e.PointerPosition );
		var time = _timeline.ScenePositionToTime( scenePos, new SnapOptions( source => source is not TimeCursorPanel ) );

		if ( e.MouseButton == MouseButtons.Middle )
		{
			//Panning is the timeline's job, over the bar as much as anywhere else.
			return;
		}

		e.StopPropagation();

		if ( e.MouseButton == MouseButtons.Right )
		{
			ShowContextMenu( time );
			return;
		}

		StartScrubbing( time, Application.KeyboardModifiers );
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( !Application.MouseButtons.HasFlag( MouseButtons.Left ) ) return;

		Scrub( _timeline.ScreenToScene( e.PointerPosition ), Application.KeyboardModifiers );
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		StopScrubbing();
	}

	public void StartScrubbing( MovieTime time, KeyboardModifiers modifiers )
	{
		if ( (modifiers & KeyboardModifiers.Alt) != 0 )
		{
			//Alt+Click: set loop time range

			_dragStartTime = time;
			Session.LoopTimeRange = null;
		}
		else
		{
			Session.PlayheadTime = time;
		}

		_panSpeed = 0f;
	}

	public void StopScrubbing() => _panSpeed = 0f;

	/// <summary>Update scrub speed from <paramref name="scenePos"/>, and optionally move the playhead.</summary>
	public void Scrub( Vector2 scenePos, bool movePlayhead ) =>
		Scrub( scenePos, movePlayhead ? KeyboardModifiers.None : null );

	private void Scrub( Vector2 scenePos, KeyboardModifiers? modifiers )
	{
		_lastScrub = (modifiers, scenePos);

		UpdateScrubSpeed( scenePos );

		if ( modifiers is not null )
		{
			PostScrub( modifiers.Value, scenePos );
		}
	}

	private void UpdateScrubSpeed( Vector2 scenePos )
	{
		var sceneView = _timeline.VisibleRect;

		if ( scenePos.x > sceneView.Right )
		{
			_panSpeed = (scenePos.x - sceneView.Right) * 5f;
		}
		else if ( scenePos.x < sceneView.Left )
		{
			_panSpeed = (scenePos.x - sceneView.Left) * 5f;
		}
		else
		{
			_panSpeed = 0f;
		}
	}

	private void PostScrub( KeyboardModifiers modifiers, Vector2 scenePos )
	{
		var sceneView = _timeline.VisibleRect;

		scenePos.x = Math.Clamp( scenePos.x, sceneView.Left, sceneView.Right );

		var time = _timeline.ScenePositionToTime( scenePos, new SnapOptions( source => source is not TimeCursorPanel ) );

		if ( (modifiers & KeyboardModifiers.Alt) != 0 )
		{
			if ( time != _dragStartTime )
			{
				Session.LoopTimeRange = new MovieTimeRange(
					MovieTime.Min( time, _dragStartTime ),
					MovieTime.Max( time, _dragStartTime ) );
			}
			else
			{
				Session.LoopTimeRange = null;
			}
		}
		else
		{
			Session.PlayheadTime = time;
		}
	}

	private void ShowContextMenu( MovieTime time )
	{
		var menu = new Menu();

		menu.AddHeading( "Preview Loop" );

		menu.AddOption( "Set Start", "start", () =>
		{
			Session.LoopTimeRange = Session.LoopTimeRange is not { } range || range.End <= time
				? (time, Session.Duration)
				: (time, range.End);
		} ).Enabled = Session.LoopTimeRange is null || Session.LoopTimeRange.Value.End > time;

		menu.AddOption( "Set End", "last_page", () =>
		{
			Session.LoopTimeRange = Session.LoopTimeRange is not { } range || range.Start >= time
				? (0d, time)
				: (range.Start, time);
		} ).Enabled = Session.LoopTimeRange is null || Session.LoopTimeRange.Value.Start < time;

		menu.AddOption( "Clear", "clear", () => Session.LoopTimeRange = null )
			.Enabled = Session.LoopTimeRange is not null;

		menu.OpenAtCursor( this );
	}

	Rect ISnapSource.SceneSnapBounds => _timeline.View.VisibleRect;

	public IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary )
	{
		if ( Session.EditMode?.SourceTimeRange is { } sourceRange )
		{
			yield return sourceRange.Start;
			yield return sourceRange.End;
		}

		if ( Session.LoopTimeRange is { } loopRange )
		{
			yield return loopRange.Start;
			yield return loopRange.End;
		}

		if ( !isPrimary ) yield break;

		yield return new SnapTarget( sourceTime.Round( _timeline.MinorTick.Interval ), -2 );
		yield return new SnapTarget( sourceTime.Round( _timeline.MajorTick.Interval ), -1 );
	}
}

/// <summary>A marked out stretch of the scrub bar: the loop, or what is on the clipboard.</summary>
public sealed class ScrubRangePanel : Panel
{
	private readonly bool _isTop;

	public ScrubRangePanel( string kind, bool isTop )
	{
		_isTop = isTop;

		AddClass( "scrub-range" );
		AddClass( kind );

		AddChild<Panel>( "edge start" );
		AddChild<Panel>( "edge end" );

		if ( kind == "loop" )
		{
			AddChild( new CornerPanel( isTop, start: true ) );
			AddChild( new CornerPanel( isTop, start: false ) );
		}
		else
		{
			AddChild<IconPanel>( "icon" ).Text = "content_paste";
		}
	}

	public void Show( Timeline timeline, MovieTimeRange? timeRange )
	{
		if ( timeRange is not { } range )
		{
			SetClass( "hidden", true );
			return;
		}

		var view = timeline.View;
		var left = view.FromScene( new Vector2( view.TimeToPixels( range.Start ), 0f ) ).x;
		var right = view.FromScene( new Vector2( view.TimeToPixels( range.End ), 0f ) ).x;

		SetClass( "hidden", false );
		Style.Left = Length.Pixels( left );
		Style.Width = Length.Pixels( Math.Max( 0f, right - left ) );
	}

	//The little wedge that says which way the loop bracket is facing.
	private sealed class CornerPanel : BitmapPanel
	{
		private const int Size = 6;

		private readonly bool _pointsUp;

		public CornerPanel( bool isTop, bool start )
		{
			_pointsUp = isTop;

			AddClass( "corner" );
			AddClass( start ? "start" : "end" );
		}

		protected override Vector2Int BitmapSize => new( Size, Size );

		protected override void OnDrawBitmap( Bitmap bitmap )
		{
			var y0 = _pointsUp ? Size : 0f;
			var y1 = _pointsUp ? 0f : Size;

			bitmap.SetAntialias( true );
			bitmap.SetFill( Color.White.WithAlpha( 0.5f ) );
			bitmap.DrawPolygon( [new Vector2( 0f, y0 ), new Vector2( 0f, y1 ), new Vector2( Size, y0 )] );
		}
	}
}
