namespace Editor;

public partial class SceneViewPanel
{
	private class State
	{
		public ViewportLayoutMode Layout { get; set; } = ViewportLayoutMode.One;
		public List<string> SplitterState { get; set; } = new List<string>();
	}

	private const string CookieName = "SceneView";

	//Layout the user chose; mapping's forced four-way split is not saved
	private ViewportLayoutMode chosenLayout = ViewportLayoutMode.One;

	public void SaveState( bool saveLayout = true )
	{
		if ( saveLayout ) //we don't want to do this when we're in a temporary state (eg. fullscreen)
		{
			ProjectCookie.Set( $"{CookieName}.Layout", new State()
			{
				Layout = chosenLayout,
				SplitterState = splitters.ConvertAll( splitter => splitter.SaveState() )
			} );
		}

		foreach ( var viewport in viewports.Values )
		{
			viewport.SaveState();
		}
	}

	public void RestoreState()
	{
		State state = ProjectCookie.Get( $"{CookieName}.Layout", new State() );

		chosenLayout = state.Layout;

		//A game is one view. Whatever the scene tabs were left showing, the tab that runs the game is a picture
		//of the game and nothing else - there is nothing to line up in a top-down view of a game being played.
		viewportLayout = TabSession is GameEditorSession ? ViewportLayoutMode.One : state.Layout;

		RebuildLayout();
		RestoreSplitterState();
	}

	public void RestoreSplitterState()
	{
		State state = ProjectCookie.Get( $"{CookieName}.Layout", new State() );

		for ( int i = 0; i < state.SplitterState.Count && i < splitters.Count; i++ )
		{
			splitters[i].RestoreState( state.SplitterState[i] );
		}
	}
}
