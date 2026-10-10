namespace Editor;

//Fullscreens the game's dock on measured runs (-perffullscreen), after play starts if asked
internal static class PerfFullscreen
{
	private static readonly bool wanted = Named( "-perffullscreen" );
	private static readonly bool afterPlay = Named( "-perfplay" );

	private static bool Named( string name )
	{
		foreach ( var arg in System.Environment.GetCommandLineArgs() )
		{
			if ( string.Equals( arg, name, System.StringComparison.OrdinalIgnoreCase ) ) return true;
		}

		return false;
	}

	private static bool done;

	[EditorEvent.Frame]
	public static void Frame()
	{
		if ( !wanted || done ) return;

		if ( afterPlay && !Game.IsPlaying ) return;

		var dock = SceneDock.Active;

		if ( !dock.IsValid() ) return;

		//Wait for the game tab to come forward so that dock goes fullscreen, not an idle one
		if ( afterPlay && dock.SceneView?.CurrentView != SceneViewMode.Game ) return;

		if ( EditorWindow.IsFullscreen( dock ) ) { done = true; return; }

		done = true;

		EditorWindow.SetFullscreen( dock );

		Log.Info( "-perffullscreen: the scene view is fullscreen" );
	}
}
