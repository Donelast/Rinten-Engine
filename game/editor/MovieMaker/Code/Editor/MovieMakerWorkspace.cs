using System.Linq;
using Rinten.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>The movie maker: an editor of its own, a tab beside General and Mapping. The scene it records and plays moves into its middle the way it moves into mapping, the timeline lies under it, and the hierarchy and the inspector stand either side.</summary>
[EditorApp( AppTitle, AppIcon, "Movies: tracks and keyframes recorded from the open scene and played back in it" )]
public sealed class MovieMakerWorkspace : SceneWorkspace
{
	/// <summary>What the tab says, and the name the workspace is found by.</summary>
	public const string AppTitle = "Movie Maker";

	public const string AppIcon = "movie_creation";

	private static readonly List<MovieMakerWorkspace> _open = new();

	/// <summary>The movie maker that is open, if one is.</summary>
	public static MovieMakerWorkspace? Current
	{
		get
		{
			_open.RemoveAll( x => !x.IsValid() );

			return _open.FirstOrDefault();
		}
	}

	private readonly PanelDockHost _host;
	private MovieEditor _editor;

	/// <summary>The timeline, tracks and toolbar - the whole of the movie editor proper.</summary>
	public MovieEditor Editor => _editor;

	public MovieMakerWorkspace() : base( AppTitle )
	{
		_open.Add( this );

		FromThemeAttribute.Apply();

		_host = new PanelDockHost();
		_editor = new MovieEditor();
		_host.SetContent( _editor );

		var hierarchy = OpenPanel( "Hierarchy", "list", "SceneTreeWidget", DockArea.Left );
		var inspector = OpenPanel( "Inspector", "manage_search", "Editor.Inspector", DockArea.Right );
		var timeline = OpenPanel( AppTitle, AppIcon, _host, DockArea.Bottom );

		//The scene keeps most of the height; the tracks get enough for a dozen lanes.
		if ( timeline is not null )
			Docks.SetSplitterProportions( timeline, 0.6f, 0.4f );

		if ( hierarchy is not null )
			Docks.SetSplitterProportions( hierarchy, 0.18f, 0.82f );

		if ( inspector is not null )
			Docks.SetSplitterProportions( inspector, 0.78f, 0.22f );
	}

	public override void OnDestroyed()
	{
		_open.Remove( this );

		base.OnDestroyed();
	}

	/// <summary>Opens the movie maker's tab, or brings it up if it is open already.</summary>
	public static MovieMakerWorkspace? Open()
	{
		EditorWindow?.OpenSceneWorkspace( AppTitle, AppIcon, () => new MovieMakerWorkspace() );

		return Current;
	}

	/// <summary>Opens a movie from the asset browser, bringing the movie maker up to take it.</summary>
	[EditorForAssetType( "movie" )]
	public static void OpenResource( MovieResource resource )
	{
		Open()?.Editor.OpenResource( resource );
	}

	//Rebuilds the editor on a hotload, keeping its sessions.
	[EditorEvent.Hotload]
	private void OnHotload()
	{
		FromThemeAttribute.Apply();

		IReadOnlyDictionary<MovieEditor.SessionKey, Session>? sessions = null;

		if ( _editor.IsValid() )
		{
			sessions = _editor.Sessions;

			_editor.CloseSession();
		}

		_editor = new MovieEditor( sessions );

		_host.SetContent( _editor );
	}
}
