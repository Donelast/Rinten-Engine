namespace Editor;

public partial class SceneViewportPanel
{
	/// <summary>Is this viewport the game view?</summary>
	public bool IsGameView { get; private set; }

	/// <summary>Called when the scene view's view mode changes.</summary>
	public void OnViewModeChanged( SceneViewMode viewMode )
	{
		Renderer.Scene = Session.Scene;
		GizmoInstance.Selection = Session.Selection;

		if ( editorCamera.IsValid() && editorCamera.Scene != Session.Scene )
		{
			//make sure the editor camera exists in the correct scene
			editorCamera.DestroyGameObject();
			editorCamera = Renderer.CreateSceneEditorCamera();
		}

		camera.Active = viewMode == SceneViewMode.Game ? null : editorCamera;

		Renderer.Camera = camera.Active;
		Renderer.EnableEngineOverlays = IsGameView;
	}

	//What this viewport was told to show - full bright, the buffers - on the cameras the game is drawn
	//with. The game's own cameras are the ones drawing it, so the view settings have to be said to them.
	private void ApplyViewToGame()
	{
		if ( Game.ActiveScene is not { } scene ) return;

		foreach ( var gameCamera in scene.GetAllComponents<CameraComponent>() )
		{
			gameCamera.DebugMode = State.RenderMode;
			gameCamera.WireframeMode = State.WireframeMode;
		}
	}

	/// <summary>Set this viewport as the game view.</summary>
	public void SetGameView()
	{
		GameMode.SetPlaySurface( Renderer );
		IsGameView = true;

		Tools.DisposeAll();
	}

	/// <summary>Clear this viewport as the game view.</summary>
	public void ClearGameView()
	{
		GameMode.ClearPlayMode();
		IsGameView = false;

		SetDefaultSize();
	}
}
