using Rinten.Helpers;
using Rinten.MovieMaker;
using Rinten.UI;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using static Editor.MovieMaker.Session;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>The whole movie editor: a track list, a timeline, and the history beside them.</summary>
[StyleSheet( "MovieEditor.cs.scss" )]
public sealed partial class MovieEditor : EditorPanel, IHotloadManaged, IUndoSystemProvider
{
	public const string HelpUrl = "";

	private static readonly ConditionalWeakTable<MovieEditor, object?> _editors = new();

	public static IEnumerable<Session> ActiveSessions => _editors
		.Select( x => x.Key )
		.Where( x => x.IsValid() )
		.Where( x => x.Session is not null )
		.Select( x => x.Session! );

	/// <summary>The movie editor the keyboard is currently talking to, if any.</summary>
	public static MovieEditor? Focused
	{
		get
		{
			for ( Panel? panel = InputFocus.Current; panel is not null; panel = panel.Parent )
			{
				if ( panel is MovieEditor editor ) return editor;
			}

			return null;
		}
	}

	//Sessions survive entering play mode, so they are identified by MoviePlayer component ID and which resource they edit. A null resource means embedded.

	public readonly record struct SessionKey( Guid PlayerId, string? ResourcePath );

	private readonly Dictionary<SessionKey, Session> _sessions;

	public Session? Session { get; private set; }

	public TrackListSection? TrackListSection { get; private set; }
	public TimelineSection? TimelineSection { get; private set; }
	public HistorySection? HistorySection { get; private set; }

	/// <summary>The timeline itself, once there is a session to draw one for.</summary>
	public Timeline? Timeline => TimelineSection?.Timeline;

	/// <summary>The bar above the timeline, which edit modes hang their own tools on.</summary>
	public MovieToolBar? TimelineToolBar => TimelineSection?.ToolBar;

	private MovieSplitterHandle? _historySplitter;

	public bool ShowHistory
	{
		get => HistorySection is { } history && !history.HasClass( "hidden" );
		set
		{
			if ( HistorySection is not { } history ) return;

			EditorCookie.Set( "moviemaker.showhistory", value );

			history.SetClass( "hidden", !value );
			_historySplitter?.SetClass( "hidden", !value );
		}
	}

	public MovieEditor( IReadOnlyDictionary<SessionKey, Session>? sessions = null )
	{
		_editors.Add( this, null );
		_sessions = sessions?.ToDictionary() ?? new Dictionary<SessionKey, Session>();

		AddClass( "movie-editor" );

		//Every key here is gated on this panel having the keyboard, so a click anywhere in it has to give it that.
		AcceptsFocus = true;

		UpdateEditorContext();

		if ( Session is null )
		{
			ShowStartupHelper();
		}

		EditorUtility.OnInspect += EditorUtility_OnInspect;
	}

	internal IReadOnlyDictionary<SessionKey, Session> Sessions => _sessions.ToImmutableDictionary();

	public override void OnDeleted()
	{
		Session?.Deactivate();
		Session = null;

		_editors.Remove( this );

		EditorUtility.OnInspect -= EditorUtility_OnInspect;

		base.OnDeleted();
	}

	[EditorEvent.Hotload]
	private void OnHotload()
	{
		Session?.TrackList.Update();
	}

	private void EditorUtility_OnInspect( EditorUtility.OnInspectArgs ev )
	{
		if ( GetMoviePlayer( ev.Object ) is { } player && Session?.Player != player )
		{
			Switch( player );
		}
	}

	private static MoviePlayer? GetMoviePlayer( object? obj )
	{
		if ( obj is Array { Length: > 0 } array )
		{
			obj = array.GetValue( 0 );
		}

		return (obj as GameObject)?.GetComponent<MoviePlayer>();
	}

	private void Initialize( MoviePlayer player, IMovieResource? resource, SessionContext? context )
	{
		Session?.Deactivate();

		var key = new SessionKey( player.Id, (resource as MovieResource)?.ResourcePath );

		if ( _sessions.TryGetValue( key, out var session ) )
		{
			Session = session;
		}
		else
		{
			Session = _sessions[key] = new Session( resource );
		}

		Session.Initialize( this, player, context );

		BuildSections( Session );

		Session.Activate();
	}

	//Lays out the three sections and the handles between them. The timeline is made first: the track list
	//scrolls with it and the edit modes hang their tools on its toolbar, so both want it to already exist.
	private void BuildSections( Session session )
	{
		DeleteChildren( true );

		TimelineSection = new TimelineSection( this, session );
		TrackListSection = new TrackListSection( this, session );
		HistorySection = new HistorySection( this, session );

		TrackListSection.Parent = this;

		AddChild<MovieSplitterHandle>()
			.Init( TrackListSection, SplitterSide.Left, TimelineSection, "moviemaker.tracklistwidth", 300f, 256f );

		TimelineSection.Parent = this;

		_historySplitter = AddChild<MovieSplitterHandle>()
			.Init( HistorySection, SplitterSide.Right, TimelineSection, "moviemaker.historywidth", 300f, 256f );

		HistorySection.Parent = this;

		ShowHistory = EditorCookie.Get( "moviemaker.showhistory", false );
	}

	public void CloseSession()
	{
		Session?.Deactivate();
		Session = null;

		TrackListSection = null;
		TimelineSection = null;
		HistorySection = null;

		ShowStartupHelper();
	}

	private void ShowStartupHelper()
	{
		DeleteChildren( true );

		AddChild( new StartupHelperPanel( CreateNewPlayer ) );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		UpdateEditorContext();

		OpenPending();

		Session?.Frame();
	}

	[Event( "scene.beforesave" )]
	private static void OnSceneBeforeSave( Scene scene )
	{
		//Save all active sessions involving the saved scene

		foreach ( var moviePlayer in scene.Components.GetAll<MoviePlayer>( FindMode.EverythingInDescendants ) )
		{
			if ( ActiveSessions.FirstOrDefault( x => x.Player == moviePlayer ) is not { } session ) continue;

			try
			{
				if ( session.Resource is MovieResource )
				{
					session.Save();
				}
			}
			catch ( Exception ex )
			{
				Log.Warning( ex );
			}
		}
	}

	//Look for any clips we can edit. If the clip we are editing has gone, stop editing it.
	private void UpdateEditorContext()
	{
		var activeEditorSession = SceneEditorSession.Active;
		var activeScene = activeEditorSession?.Scene;

		if ( activeScene?.IsLoading is true )
		{
			//Don't try to create a session while the scene is loading
			activeScene = null;
		}

		if ( Session is { } session )
		{
			//Whatever was being edited is gone
			if ( !session.Player.IsValid || session.Player.Scene != activeScene || session.Player.Resource != session.Root.Resource )
			{
				CloseSession();
			}
		}

		//No active scene, can't open a player
		if ( activeScene is null ) return;

		if ( Session is null )
		{
			//The first player in the scene, walked to: FirstOrDefault boxed the scene's walk twice, every frame the editor had nothing open.
			foreach ( var player in activeScene.GetAllComponents<MoviePlayer>() )
			{
				Switch( player );
				break;
			}
		}
	}

	public void PlayToggle()
	{
		if ( Session is not { } session ) return;

		session.IsPlaying = !session.IsPlaying;
	}

	public void RecordToggle()
	{
		if ( Session is not { } session ) return;

		session.IsRecording = !session.IsRecording;
	}

	public void NavigateToStart()
	{
		if ( Session is not { } session ) return;

		session.PlayheadTime = 0d;

		Timeline?.PanToPlayheadTime();
	}

	public void NavigateToEnd()
	{
		if ( Session is not { } session ) return;

		session.PlayheadTime = session.Duration;

		Timeline?.PanToPlayheadTime();
	}

	public void Undo() => Session?.Undo();

	public void Redo() => Session?.Redo();

	public void Switch( MoviePlayer player )
	{
		Initialize( player, player.Resource, null );
	}

	public void EnterSequence( MovieResource resource, MovieTransform transform, MovieTimeRange timeRange )
	{
		var oldTimeline = Timeline!;

		var timeOffset = transform.Inverse * oldTimeline.TimeOffset;
		var pixelsPerSecond = (float)(transform.Inverse.Scale.FrequencyScale * oldTimeline.PixelsPerSecond);

		Initialize( Session!.Player, resource, new SessionContext( Session, transform, timeRange ) );

		Timeline!.SetView( timeOffset, pixelsPerSecond );
	}

	public void ExitSequence()
	{
		if ( Session?.Context is not { } context ) return;

		var oldTimeline = Timeline!;
		var timeOffset = Session.SequenceTransform * oldTimeline.TimeOffset;
		var pixelsPerSecond = (float)(Session.SequenceTransform.Scale.FrequencyScale * oldTimeline.PixelsPerSecond);

		var resource = Session.Resource;

		Session.Save();
		Initialize( Session.Player, context.Parent.Resource, context.Parent.Context );

		Timeline!.SetView( timeOffset, pixelsPerSecond );

		if ( resource is MovieResource movieResource )
		{
			Session.Project.RefreshSequenceTracks( movieResource );
		}
	}

	public void CreateNewPlayer()
	{
		using ( SceneEditorSession.Scope() )
		{
			var go = new GameObject( true, "New Movie Player" );
			go.Components.Create<MoviePlayer>();

			SceneEditorSession.Active.Selection.Set( go );
		}
	}

	/// <summary>Returns true if the given movie <paramref name="resource"/> has an open session. This includes parent sessions of the current session, for nested movies.</summary>
	public bool IsMovieOpen( IMovieResource resource ) => FindSession( resource ) is not null;

	/// <summary>Returns an open movie session for the given <paramref name="resource"/>, or null if it isn't currently being edited. This includes parent sessions for nested movies.</summary>
	public Session? FindSession( IMovieResource resource )
	{
		var session = Session;

		while ( session is not null )
		{
			if ( session.Resource == resource ) return session;

			session = session.Parent;
		}

		return null;
	}

	IUndoSystem? IUndoSystemProvider.UndoSystem => Session?.History;
}
