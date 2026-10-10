namespace Editor;

public partial class SceneViewPanel
{
	/// <summary>What this view shows, fixed by its session: games have their own tab</summary>
	public SceneViewMode CurrentView => TabSession is GameEditorSession ? SceneViewMode.Game : SceneViewMode.Scene;

	private SceneViewMode lastView;

	private Scene lastScene;

	private SceneViewportPanel gameViewport;

	//Points the first viewport at the running game. Called once, when the view for a game session is built.
	private void TakeGameView()
	{
		if ( TabSession is not GameEditorSession ) return;
		if ( gameViewport is not null ) return;

		gameViewport = viewports.FirstOrDefault().Value;
		if ( gameViewport is null ) return;

		gameViewport.SetGameView();

		OnViewModeChanged();
		viewportTools.UpdateViewportFromCookie();
	}

	[Event( "scene.stop" )]
	public void OnSceneStop()
	{
		if ( gameViewport is null ) return;

		gameViewport.ClearGameView();
		gameViewport = null;

		OnViewModeChanged();
	}

	//Current view mode changed
	private void OnViewModeChanged()
	{
		viewportTools.Rebuild();

		toolSidebar?.SetClass( "hidden", CurrentView == SceneViewMode.Game );

		foreach ( var viewport in viewports.Values )
		{
			viewport.OnViewModeChanged( CurrentView );
		}
	}

	public SceneViewportPanel GetGameTarget()
	{
		return gameViewport;
	}

	/// <summary>Set the game viewport to free sizing mode</summary>
	public void SetFreeSize()
	{
		GetGameTarget()?.SetDefaultSize();
	}

	/// <summary>Set the game viewport to a specific aspect ratio</summary>
	public void SetForceAspect( float aspect )
	{
		GetGameTarget()?.SetAspectRatio( aspect );
	}

	/// <summary>Set the game viewport to a specific resolution</summary>
	public void SetForceResolution( Vector2 resolution )
	{
		GetGameTarget()?.SetResolution( resolution );
	}
}
