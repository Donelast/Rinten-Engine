using Rinten.MovieMaker;
using Rinten.UI;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>The timeline: tracks laid out down the page and time running across it.</summary>
[StyleSheet( "Timeline.cs.scss" )]
public sealed partial class Timeline : Panel, ISnapSource
{
	[FromTheme]
	public static Color PlayheadColor { get; set; } = Theme.Yellow;

	[FromTheme]
	public static Color PreviewColor { get; set; } = Theme.Blue;

	/// <summary>Color outside the current movie's time range.</summary>
	[FromTheme]
	public static Color BackgroundColor { get; set; } = Theme.ControlBackground;

	/// <summary>Color outside the current sequence block's time range.</summary>
	[FromTheme]
	public static Color OuterColor { get; set; } = Theme.ControlBackground.LerpTo( Theme.WidgetBackground, 0.5f );

	/// <summary>Color inside the current sequence block's time range.</summary>
	[FromTheme]
	public static Color InnerColor { get; set; } = Theme.WidgetBackground;

	/// <summary>How much space to leave around the playhead when auto-scrolling.</summary>
	[FromTheme]
	public static float PlayheadMarginPixels { get; set; } = 128f;

	public Session Session { get; }

	/// <summary>Where the timeline is looking, and how wide a second is drawn.</summary>
	public TimelineView View { get; } = new();

	/// <summary>Invoked when the view pans, scrolls, or changes scale.</summary>
	public event Action<Rect>? ViewChanged;

	/// <summary>Invoked when what is picked out changes.</summary>
	public Action? OnSelectionChanged { get; set; }

	private readonly TimelineBackgroundPanel _background;
	private readonly TimelineGridPanel _grid;

	//The part that moves: one transform for everything on the timeline.
	private readonly Panel _pan;

	private readonly TimeCursorPanel _playhead;
	private readonly TimeCursorPanel _preview;
	private readonly TimelineScrollBarPanel _scrollBar;

	private readonly SynchronizedSet<TrackView, TimelineTrack> _tracks;
	private readonly SynchronizedSet<(MovieTime Time, Rect SceneRect), SnapTargetPanel> _snapTargets;

	public ScrubBarPanel ScrubBarTop { get; }
	public ScrubBarPanel ScrubBarBottom { get; }

	public IEnumerable<TimelineTrack> Tracks => _tracks;

	/// <summary>How much room the project navigation rows take at the top of the list.</summary>
	public float HeaderHeight => (Session.Depth + 1) * MovieLayout.TrackHeight + MovieLayout.RootTrackSpacing;

	public Timeline( Session session )
	{
		Session = session;

		AddClass( "timeline" );

		AcceptsFocus = true;

		_background = AddChild( new TimelineBackgroundPanel( this ) );
		_grid = AddChild( new TimelineGridPanel( this ) );

		_pan = AddChild<Panel>( "pan" );

		ScrubBarTop = AddChild( new ScrubBarPanel( this, true ) );
		ScrubBarBottom = AddChild( new ScrubBarPanel( this, false ) );

		_playhead = AddChild( new TimeCursorPanel( this, PlayheadColor, true ) { LabelFadeTime = 1f } );
		_preview = AddChild( new TimeCursorPanel( this, PreviewColor, false ) );

		_scrollBar = AddChild( new TimelineScrollBarPanel( this ) );

		_tracks = new SynchronizedSet<TrackView, TimelineTrack>(
			AddTrack, RemoveTrack, UpdateTrack );

		_snapTargets = new SynchronizedSet<(MovieTime Time, Rect SceneRect), SnapTargetPanel>(
			_ => _pan.AddChild( new SnapTargetPanel( this ) ),
			item => item.Delete(),
			( target, item ) =>
			{
				item.SetSceneRect( new Rect(
					View.TimeToPixels( target.Time ), target.SceneRect.Top,
					1f, target.SceneRect.Height ) );

				return true;
			} );

		View.PixelsPerSecond = Session.Cookies.PixelsPerSecond;
		View.Changed += OnViewChanged;

		Session.PlayheadChanged += UpdatePlayheadTime;
		Session.PreviewChanged += UpdatePreviewTime;
		Session.TrackList.Changed += UpdateTracks;

		EditorEvent.Register( this );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		View.Changed -= OnViewChanged;

		Session.PlayheadChanged -= UpdatePlayheadTime;
		Session.PreviewChanged -= UpdatePreviewTime;
		Session.TrackList.Changed -= UpdateTracks;

		EditorEvent.Unregister( this );
	}

	private int _lastState;

	private int CalculateStateHash() => HashCode.Combine(
		View.PixelsPerSecond, View.VisibleTimeRange, Session.FrameRate, Session.TrackList.StateHash );

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( !IsVisible ) return;

		MeasureView();
		UpdateZoom();

		_background.Frame();

		ScrubBarTop.Frame();
		ScrubBarBottom.Frame();

		if ( Session.PreviewTime is not null
			&& (Application.KeyboardModifiers & KeyboardModifiers.Shift) == 0
			&& (Application.MouseButtons & MouseButtons.Left) == 0 )
		{
			Session.PreviewTime = null;
		}

		var state = CalculateStateHash();

		if ( state != _lastState )
		{
			_lastState = state;

			UpdateTracks( Session.TrackList );
		}

		if ( IsDragging )
		{
			DragTo( _lastMouseScenePos );
		}

		CullItems();
	}

	//Tells the view how big its window is, in the units everything is placed in.
	private void MeasureView()
	{
		var scale = ScaleFromScreen;

		View.Size = Box.Rect.Size * scale;
		View.ContentHeight = HeaderHeight + Session.TrackList.Height + TimelineView.VerticalMargin;
	}

	private void UpdatePlayheadTime( MovieTime time ) => _playhead.Value = time;

	private void UpdatePreviewTime( MovieTime? time )
	{
		_preview.SetClass( "hidden", time is null );

		if ( time is { } value )
		{
			_preview.Value = value;
		}
	}

	public void UpdateTracks( TrackListView trackList )
	{
		_tracks.Update( trackList.VisibleTracks );
	}

	private TimelineTrack AddTrack( TrackView source ) => _pan.AddChild( new TimelineTrack( this, source ) );

	private void RemoveTrack( TimelineTrack item ) => item.Delete();

	private bool UpdateTrack( TrackView source, TimelineTrack item )
	{
		item.UpdateLayout();

		return true;
	}

	Rect ISnapSource.SceneSnapBounds => View.VisibleRect;

	IEnumerable<SnapTarget> ISnapSource.GetSnapTargets( MovieTime sourceTime, bool isPrimary )
	{
		yield return MovieTime.Zero;

		if ( !isPrimary || !Session.FrameSnap ) yield break;

		yield return new SnapTarget( sourceTime.Round( MovieTime.FromFrames( 1, Session.FrameRate ) ), -3, false );
	}
}
