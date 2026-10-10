using Editor.MovieMaker.BlockDisplays;
using Rinten.MovieMaker;
using Rinten.UI;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>One keyframe on the timeline: a grip you can drag, and a shape that says how the values either side of it are joined.</summary>
public sealed class KeyframeHandle : TimelineItem, IComparable<KeyframeHandle>, ITrackItem, IMovieDraggable, IMovieContextMenu, ISnapSource
{
	private const float FullWidth = 16f;
	private const float HalfWidth = 8f;

	private readonly KeyframeGlyphPanel _glyph;

	public TimelineTrack Track { get; }
	public Session Session { get; }
	public TrackView View { get; }

	public KeyframeEditMode? EditMode => Session.EditMode as KeyframeEditMode;

	public Keyframe Keyframe
	{
		get;
		set
		{
			var maxInterpolation = View.Target.TargetType.MaxSupportedKeyframeInterpolation;

			if ( value.Interpolation > maxInterpolation )
			{
				value = value with { Interpolation = maxInterpolation };
			}

			if ( field == value ) return;

			if ( !Equals( value.Value, field.Value ) )
			{
				Tooltip = $"{View.Track.Name} = {value.Value?.ToString() ?? "null"}";
			}

			field = value;

			UpdatePosition();
		}
	}

	public MovieTime Time
	{
		get => Keyframe.Time;
		set
		{
			if ( value == Keyframe.Time ) return;
			Keyframe = Keyframe with { Time = value };
		}
	}

	public KeyframeConnection Connection
	{
		get => Keyframe.Connection;
		set
		{
			if ( value == Keyframe.Connection ) return;
			Keyframe = Keyframe with { Connection = value };
		}
	}

	public bool IsDragging { get; private set; }

	//True when this keyframe is the end of one block, and overlaps another keyframe that starts a new block.
	internal bool IsOverlappingNextBlock { get; set; }

	bool IMovieItem.MultiSelectable => true;

	MovieTime? IMovieItem.SelectionTime => IsOverlappingNextBlock ? Time - MovieTime.Epsilon : Time;

	TimelineTrack ITrackItem.Track => Track;

	public KeyframeHandle( TimelineTrack track, Keyframe keyframe )
		: base( track.Timeline )
	{
		Track = track;
		Session = track.Session;
		View = track.View;

		AddClass( "keyframe-handle" );

		ZIndex = 100;
		Selectable = true;

		_glyph = AddChild( new KeyframeGlyphPanel( this ) );

		Keyframe = keyframe;

		Timeline.AddItem( this );
	}

	public override void OnDeleted()
	{
		Timeline.Forget( this );

		base.OnDeleted();
	}

	/// <summary>Where along its own width the handle sits on its time.</summary>
	public float HandleBias => Keyframe.Connection switch
	{
		KeyframeConnection.EndBlock => 1f,
		KeyframeConnection.StartBlock => 0f,
		_ => 0.5f
	};

	/// <summary>Half a handle only answers for its own half.</summary>
	public override bool ContainsScenePoint( Vector2 scenePos )
	{
		if ( !SceneRect.IsInside( scenePos ) ) return false;

		var offset = scenePos.x - (SceneRect.Left + SceneRect.Width * HandleBias);

		return Keyframe.Connection switch
		{
			KeyframeConnection.EndBlock => offset <= 0f,
			KeyframeConnection.StartBlock => offset >= 0f,
			_ => true
		};
	}

	public void UpdatePosition()
	{
		var width = Keyframe.Connection switch
		{
			KeyframeConnection.EndBlock or KeyframeConnection.StartBlock => HalfWidth,
			_ => FullWidth
		};

		SetSceneRect( new Rect(
			Timeline.TimeToPixels( Time ) - width * HandleBias,
			Track.SceneRect.Top,
			width,
			MovieLayout.TrackHeight ) );

		SetClass( "hidden", View.IsLocked );

		_glyph.MarkDirty();
	}

	void IMovieItem.SingleSelected()
	{
		if ( EditMode is { } editMode )
		{
			editMode.DefaultInterpolation = Keyframe.Interpolation;
		}
	}

	protected override void OnSelectionChanged()
	{
		ZIndex = Selected ? 101 : 100;

		_glyph.MarkDirty();
	}

	public override void Tick()
	{
		base.Tick();

		_glyph.SyncState();
	}

	public void ShowContextMenu( EditMode.ContextMenuEvent ev )
	{
		if ( EditMode is not { } editMode ) return;

		ev.Accepted = true;

		editMode.Session.PlayheadTime = Keyframe.Time;

		var selection = Timeline.SelectedItems
			.OfType<KeyframeHandle>()
			.OrderBy( x => x.View )
			.ThenBy( x => x.Keyframe )
			.ToImmutableArray();

		ev.Title = $"Selected Keyframe{(selection.Length > 1 ? "s" : "")}";

		CreateValueEditMenu( selection, ev.Menu );
		CreateInterpolationMenu( selection, ev.Menu );
		CreateConnectionMenu( selection, ev.Menu );

		ev.Menu.AddHeading( "Clipboard" );

		ev.Menu.AddOption( "Copy", "content_copy", () => editMode.Copy() );
		ev.Menu.AddOption( "Cut", "content_cut", () => editMode.Cut() );

		if ( GetOverlappingClipboard( selection ) is { } clipboard )
		{
			ev.Menu.AddOption( "Paste", "content_paste", () => editMode.Paste( clipboard, Time - clipboard.Time ) );
		}

		ev.Menu.AddOption( "Delete", "delete", () => editMode.Delete() );
	}

	private KeyframeEditMode.ClipboardData? GetOverlappingClipboard( IReadOnlyList<KeyframeHandle> selection )
	{
		if ( EditMode?.Clipboard is not { } clipboard ) return null;

		if ( !selection.Any( x => clipboard.Keyframes.Any( y => y.Guid == x.View.Track.Id ) ) )
		{
			return null;
		}

		return clipboard;
	}

	private void CreateValueEditMenu( IReadOnlyList<KeyframeHandle> selection, Menu parent )
	{
		var type = selection.First().View.Target.TargetType;

		if ( selection.Any( x => x.View.Target.TargetType != type ) ) return;

		var method = GetType()
			.GetMethod( nameof( TryCreateValueEdit ), BindingFlags.NonPublic | BindingFlags.Instance )!
			.MakeGenericMethod( type );

		if ( method.Invoke( this, [selection] ) is not Widget widget ) return;

		parent.AddWidget( widget );
	}

	//A row for typing the keyframe's value straight into the menu. A menu is still a window of
	//widgets, so the panel sheet goes into it through the same host every other migrated control uses.
	private Widget? TryCreateValueEdit<T>( IReadOnlyList<KeyframeHandle> selection )
	{
		var selectedValue = (T)Keyframe.Value!;

		var property = TypeLibrary.CreateProperty( "Value", () => selectedValue, value =>
		{
			selectedValue = value;

			foreach ( var handle in selection )
			{
				handle.Keyframe = handle.Keyframe with { Value = value };
			}

			EditMode?.UpdateTracksFromHandles( selection );
		}, [] );

		try
		{
			var host = new Editor.Inspectors.PanelControlSheetHost { MaximumWidth = 320f };

			host.AddRow( property );

			return host;
		}
		catch ( Exception ex )
		{
			Log.Warning( ex );
			return null;
		}
	}

	private void CreateInterpolationMenu( IReadOnlyList<KeyframeHandle> selection, Menu parent )
	{
		var maxInterpolation = selection
			.Select( x => x.View.Target.TargetType )
			.Distinct()
			.Min( x => x.MaxSupportedKeyframeInterpolation );

		var menu = parent.AddMenu( "Interpolation Mode", "gradient" );
		var currentMode = selection.All( x => x.Keyframe.Interpolation == selection[0].Keyframe.Interpolation )
			? selection[0].Keyframe.Interpolation
			: KeyframeInterpolation.Unknown;

		foreach ( var value in Enum.GetValues<KeyframeInterpolation>() )
		{
			if ( value < 0 ) continue;
			if ( value > maxInterpolation ) continue;

			var option = menu.AddOption( value.ToString().ToTitleCase(), action: () =>
			{
				using var _ = Session.History.Push( "Change Keyframe Interpolation" );

				foreach ( var handle in selection )
				{
					handle.Keyframe = handle.Keyframe with { Interpolation = value };
				}

				EditMode?.UpdateTracksFromHandles( selection );
			} );

			option.Checkable = true;
			option.Checked = value == currentMode;
		}
	}

	private void CreateConnectionMenu( IReadOnlyList<KeyframeHandle> selection, Menu parent )
	{
		//General menu for changing connection mode of all selected keyframes

		var menu = parent.AddMenu( "Connection Mode", "linear_scale" );
		var currentMode = selection.All( x => x.Keyframe.Connection == selection[0].Keyframe.Connection )
			? selection[0].Keyframe.Connection
			: KeyframeConnection.Unknown;

		foreach ( var value in Enum.GetValues<KeyframeConnection>() )
		{
			if ( value < 0 ) continue;

			var icon = TypeLibrary.GetEnumDescription( value ).Icon;

			var option = menu.AddOption( value.ToString().ToTitleCase(), icon, action: () =>
			{
				using var _ = Session.History.Push( "Change Keyframe Connections" );

				foreach ( var handle in selection )
				{
					handle.Keyframe = handle.Keyframe with { Connection = value };
				}

				EditMode?.UpdateTracksFromHandles( selection );
			} );

			option.Checkable = true;
			option.Checked = value == currentMode;
		}

		var trackGroups = selection
			.GroupBy( x => x.View )
			.Select( x => (
				All: EditMode?.GetTrackHandles( x.First().Track ) ?? ImmutableArray<KeyframeHandle>.Empty,
				Selected: x.ToImmutableHashSet()) )
			.ToArray();

		var canSplitRanges = trackGroups
			.SelectMany( x => x.All.GetSelectedRanges( x.Selected ) )
			.Where( CanSplit )
			.ToArray();

		var canJoinPairs = trackGroups
			.SelectMany( x => x.All.GetSelectedPairs( x.Selected ) )
			.Where( x => CanJoin( x.Prev, x.Next ) )
			.ToArray();

		if ( canSplitRanges.Length > 0 )
		{
			parent.AddOption( "Split", "join_inner", () =>
			{
				using var _ = Session.History.Push( "Split Keyframes" );

				foreach ( var range in canSplitRanges )
				{
					Split( range );
				}

				EditMode?.UpdateTracksFromHandles( selection );
			} );
		}

		if ( canJoinPairs.Length > 0 )
		{
			parent.AddOption( "Join", "join_full", () =>
			{
				using var _ = Session.History.Push( "Join Keyframes" );

				foreach ( var pair in canJoinPairs )
				{
					Join( pair.Prev, pair.Next );
				}

				EditMode?.UpdateTracksFromHandles( selection );
			} );
		}
	}

	//Split a selected range of keyframes.
	private void Split( IReadOnlyList<KeyframeHandle> keyframes )
	{
		var editMode = EditMode!;

		if ( keyframes.Count == 1 )
		{
			editMode.SplitKeyframe( keyframes[0] );
			return;
		}

		keyframes[0].Keyframe = keyframes[0].Keyframe with { Connection = KeyframeConnection.EndBlock };
		keyframes[^1].Keyframe = keyframes[^1].Keyframe with { Connection = KeyframeConnection.StartBlock };

		editMode.RemoveKeyframes( keyframes.Skip( 1 ).Take( keyframes.Count - 2 ) );
	}

	//Join a disconnected pair of keyframes.
	private static void Join( KeyframeHandle prev, KeyframeHandle next )
	{
		if ( !CanJoin( prev, next ) ) return;

		if ( prev.Keyframe.Connection is KeyframeConnection.EndBlock )
		{
			prev.Keyframe = prev.Keyframe with { Connection = KeyframeConnection.Connect };
		}

		if ( next.Keyframe.Connection is KeyframeConnection.StartBlock )
		{
			next.Keyframe = next.Keyframe with { Connection = KeyframeConnection.Connect };
		}
	}

	private static bool CanSplit( IReadOnlyList<KeyframeHandle> handles ) =>
		handles.Any( x => x.Keyframe.Connection is KeyframeConnection.Connect )
		|| handles.Count > 2
		|| handles is [{ Keyframe.Connection: not KeyframeConnection.EndBlock }, { Keyframe.Connection: not KeyframeConnection.StartBlock }];

	private static bool CanJoin( KeyframeHandle prev, KeyframeHandle next ) =>
		prev.Keyframe.Connection is KeyframeConnection.EndBlock ||
		next.Keyframe.Connection is KeyframeConnection.StartBlock;

	public int CompareTo( KeyframeHandle? other )
	{
		if ( ReferenceEquals( this, other ) )
		{
			return 0;
		}

		if ( other is null )
		{
			return 1;
		}

		var keyframeCompare = Keyframe.CompareTo( other.Keyframe );

		if ( keyframeCompare != 0 )
		{
			return keyframeCompare;
		}

		//When overlapping, put selected first

		return -Selected.CompareTo( other.Selected );
	}

	MovieTimeRange IMovieItem.TimeRange => Keyframe.Time;

	void IMovieDraggable.StartDrag() => IsDragging = true;

	void IMovieDraggable.Drag( MovieTime delta ) => Time += delta;

	void IMovieDraggable.EndDrag() => IsDragging = false;

	public IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary ) => IsDragging ? [] : [Time];

	public bool SnapFilter( ISnapSource source )
	{
		if ( source == this ) return false;
		if ( source is not BlockItem block ) return true;

		var view = View;

		while ( view is not null )
		{
			if ( view == block.Track.View ) return false;

			view = view.Parent;
		}

		return true;
	}
}
