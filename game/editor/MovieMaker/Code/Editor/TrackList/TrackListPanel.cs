using Rinten.MovieMaker;
using Rinten.UI;
using System.Linq;
using System.Reflection;

namespace Editor.MovieMaker;

#nullable enable

//The rows are placed at the heights the timeline puts them at, and the whole column is slid by however far the timeline is scrolled - so the two always line up without either of them asking the other where it is.
/// <summary>Lists tracks in the current movie, and allows you to add or remove them.</summary>
[StyleSheet( "TrackListPanel.cs.scss" )]
public sealed class TrackListPanel : Panel, IEditorDropTarget
{
	public Session Session { get; }
	public Timeline Timeline { get; }

	private SceneEditorSession SceneEditorSession { get; }

	public IEnumerable<TrackRowPanel> RootTracks => _rootTracks;

	public IEnumerable<TrackRowPanel> Tracks => RootTracks.SelectMany( EnumerateDescendants );

	private static IEnumerable<TrackRowPanel> EnumerateDescendants( TrackRowPanel track ) =>
		[track, .. track.ChildRows.SelectMany( EnumerateDescendants )];

	private readonly Panel _content;
	private readonly TrackDropTargetPanel _dragTarget;
	private readonly Rinten.UI.Label _placeholder;
	private readonly SynchronizedSet<TrackView, TrackRowPanel> _rootTracks;

	private TrackListView? _trackList;

	public TrackListPanel( Session session )
	{
		Session = session;
		Timeline = session.Editor.Timeline!;

		AddClass( "track-list" );

		SceneEditorSession = SceneEditorSession.Resolve( Session.Player.Scene );
		SceneEditorSession.Selection.OnItemAdded += OnSelectionAdded;

		_content = AddChild<Panel>( "content" );

		var navigation = new List<ProjectNavigationPanel>();
		var parentSession = session;

		while ( parentSession is not null )
		{
			navigation.Add( new ProjectNavigationPanel( parentSession, parentSession == session ) { Parent = _content } );

			parentSession = parentSession.Parent;
		}

		//Built from the deepest outwards, so put the root clip back on top.
		navigation.Reverse();

		for ( var i = 0; i < navigation.Count; i++ )
		{
			_content.SetChildIndex( navigation[i], i );
		}

		_placeholder = _content.AddChild<Rinten.UI.Label>( "placeholder" );
		_placeholder.Text = "Drag a GameObject, Component, MovieResource or inspector property here to create a track.";

		_dragTarget = AddChild( new TrackDropTargetPanel( this ) );

		_rootTracks = new SynchronizedSet<TrackView, TrackRowPanel>(
			AddRootTrack, RemoveRootTrack, UpdateChildTrack );

		Timeline.ViewChanged += OnTimelineViewChanged;

		EditorEvent.Register( this );

		Load( Session.TrackList );
	}

	public override void OnDeleted()
	{
		if ( _trackList is not null )
		{
			_trackList.Changed -= TrackList_Changed;
		}

		Timeline.ViewChanged -= OnTimelineViewChanged;
		SceneEditorSession.Selection.OnItemAdded -= OnSelectionAdded;

		EditorEvent.Unregister( this );

		base.OnDeleted();
	}

	private TrackRowPanel AddRootTrack( TrackView source ) => new( this, null, source ) { Parent = _content };

	private void RemoveRootTrack( TrackRowPanel item ) => item.Delete();

	private bool UpdateChildTrack( TrackView source, TrackRowPanel item ) => item.UpdateLayout();

	//Try to find a relevant track to select if we've just selected a GameObject in the hierarchy.
	private void OnSelectionAdded( object item )
	{
		if ( item is not GameObject go ) return;

		if ( Session.TrackList.Find( go ) is not { } trackView ) return;

		if ( trackView.IsSelected ) return;

		//Don't select this GameObject track if we've already selected a property track inside it

		if ( trackView.SameGameObjectDescendants.Any( x => x.IsSelected ) ) return;

		trackView.Select();

		Timeline.ScrollToTrack( trackView );
	}

	private void Load( TrackListView trackList )
	{
		if ( _trackList == trackList ) return;

		if ( _trackList is not null )
		{
			_trackList.Changed -= TrackList_Changed;
		}

		_trackList = trackList;
		_trackList.Changed += TrackList_Changed;

		TrackList_Changed( trackList );
	}

	private void TrackList_Changed( TrackListView trackList )
	{
		_rootTracks.Update( trackList.RootTracks );

		var header = Timeline.HeaderHeight;

		foreach ( var row in _rootTracks )
		{
			row.PlaceAt( header + row.View.Position );
		}

		_placeholder.SetClass( "hidden", _rootTracks.Count > 0 );

		OnTimelineViewChanged( Timeline.VisibleRect );
	}

	//Slides the column by however far the timeline is scrolled.
	private void OnTimelineViewChanged( Rect visibleRect )
	{
		var transform = new PanelTransform();

		transform.AddTranslate( Length.Pixels( 0f ), Length.Pixels( -visibleRect.Top ) );

		_content.Style.Transform = transform;
	}

	public override void OnMouseWheel( Vector2 value )
	{
		var turn = value.y != 0f ? value.y : value.x;

		if ( turn.AlmostEqual( 0f ) )
		{
			base.OnMouseWheel( value );
			return;
		}

		Timeline.ScrollImmediate( -turn * 48f );
	}

	private Vector2 _lastPointer;

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		_lastPointer = e.PointerPosition;

		if ( e.MouseButton == MouseButtons.Left && (Application.KeyboardModifiers & KeyboardModifiers.Ctrl) == 0 )
		{
			Session.TrackList.DeselectAll();
		}
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		var delta = (e.PointerPosition - _lastPointer) * ScaleFromScreen;

		_lastPointer = e.PointerPosition;

		if ( Application.MouseButtons.HasFlag( MouseButtons.Middle ) )
		{
			Timeline.ScrollImmediate( delta.y );
		}
	}

	//Dragging things in to make tracks

	public bool AcceptEditorDrops => true;

	public DropAction OnEditorDragHover( Widget.DragEvent ev ) =>
		HasDraggedTracks( ev.Data ) ? DropAction.Link : DropAction.Ignore;

	public void OnEditorDragLeave() => _dragTarget.SetClass( "hovered", false );

	public void OnEditorDragDrop( Widget.DragEvent ev ) => AddTracksFromDrag( ev.Data );

	internal static bool HasDraggedTracks( DragData data )
	{
		if ( data.OfType<GameObject>().Any() ) return true;
		if ( data.OfType<Component>().Any() ) return true;

		if ( data.OfType<SerializedProperty>().FirstOrDefault() is { } property )
		{
			return property.FindPathInScene() is not null;
		}

		if ( data.Assets.FirstOrDefault( x => x.AssetPath?.EndsWith( ".movie" ) ?? false ) is { } assetData )
		{
			var assetTask = assetData.GetAssetAsync();

			if ( !assetTask.IsCompleted ) return false;
			if ( assetTask.Result?.LoadResource<MovieResource>() is null ) return false;

			return true;
		}

		return false;
	}

	private IEnumerable<IProjectTrack> CreateDraggedTracks( DragData data )
	{
		using var scope = Session.History.Push( "Create New Track(s)" );

		if ( data.OfType<GameObject>().ToArray() is { Length: > 0 } gos )
		{
			foreach ( var go in gos )
			{
				yield return Session.GetOrCreateTrack( go );
			}
		}

		if ( data.OfType<Component>().ToArray() is { Length: > 0 } components )
		{
			foreach ( var component in components )
			{
				yield return Session.GetOrCreateTrack( component );
			}
		}

		if ( data.OfType<SerializedProperty>().ToArray() is { Length: > 0 } properties )
		{
			foreach ( var property in properties )
			{
				if ( property.FindPathInScene() is not { } path ) continue;

				yield return Session.GetOrCreateTrack( path );
			}
		}
	}

	private static PropertyInfo? DragData_Current { get; } = typeof( DragData )
		.GetProperty( "Current", BindingFlags.Static | BindingFlags.NonPublic );

	internal static DragData? CurrentDrag => (DragData?)DragData_Current?.GetValue( null );

	[EditorEvent.Frame]
	private void Frame()
	{
		_dragTarget.SetClass( "hidden", CurrentDrag is not { } data || !HasDraggedTracks( data ) );
	}

	internal void AddTracksFromDrag( DragData data )
	{
		var tracks = CreateDraggedTracks( data ).ToArray();

		_dragTarget.SetClass( "hovered", false );

		if ( tracks.Length == 0 ) return;

		Session.TrackList.Update();
		Session.ClipModified();

		Session.TrackList.ExpandAncestors( tracks );
		Session.TrackList.SelectAll( tracks );

		Timeline.ScrollToTrack( tracks[0] );

		var trackViews = tracks
			.Select( Session.TrackList.Find )
			.OfType<TrackView>()
			.ToArray();

		if ( trackViews.Length == 0 ) return;

		//Look for track presets that match all created tracks

		var matchingPresets = TrackPreset.BuiltInPresets
			.Concat( Session.Config.TrackPresets )
			.Where( x => trackViews.All( y => x.Root.Matches( y.Target ) ) )
			.ToArray();

		if ( matchingPresets.Length == 0 ) return;

		var menu = new Menu();

		menu.AddHeading( "Load Sub-Track Preset" );

		TrackPresetMenu.Populate( menu, matchingPresets, trackViews );

		menu.OpenAtCursor( this );
	}
}
