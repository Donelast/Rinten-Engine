namespace Editor;

public partial class SceneViewportPanel
{
	//Bumped when a saved viewport stops meaning what it said. Camera positions and rotations, and the grid plane, were all written in
	//the old Z-up basis; restoring one now would put a person somewhere they never left the camera. A new key simply leaves those behind.
	private const string CookieVersion = "v2";

	private class SceneCookie
	{
		public Vector3 CameraPosition { get; set; }
		public Rotation CameraRotation { get; set; }
		public float? CameraOrthoHeight { get; set; }
		public bool CameraOrtho { get; set; }
	}

	public ViewportState State { get; init; }

	private void InitializeCamera()
	{
		SceneView.TabSession.MakeActive();

		using var scope = SceneEditorSession.Scope();

		Scene scene = Session.Scene;
		if ( !scene.IsValid() )
			return;

		//1. load last camera position from cookies if possible
		if ( scene.Source is not null &&
			ProjectCookie.Get<SceneCookie>( $"{scene.Source.ResourcePath}.Viewport{ViewportIndex}", null ) is SceneCookie cookie )
		{
			State.CameraPosition = cookie.CameraPosition;
			if ( cookie.CameraOrthoHeight.HasValue )
				State.CameraOrthoHeight = cookie.CameraOrthoHeight.Value;
			if ( !State.Is2D )
				State.CameraRotation = cookie.CameraRotation;

			//Restore an axis-aligned ortho view
			if ( cookie.CameraOrtho && !State.Is2D )
			{
				camera.RestoreOrtho();
			}
			return;
		}

		//2. Place camera where a Camera component is
		var cc = scene.Camera;
		if ( cc.IsValid() )
		{
			State.CameraPosition = cc.WorldPosition;
			if ( !State.Is2D )
				State.CameraRotation = cc.WorldRotation;
			return;
		}

		//3. BBox frame the scene
		if ( !State.Is2D )
			State.CameraRotation = Rotation.From( 45, 45, 0 );

		var fieldOfView = 80.0f;
		var bounds = scene.GetBounds();
		var distance = MathX.SphereCameraDistance( bounds.Size.Length * 0.5f, fieldOfView );
		distance = MathF.Max( distance, 5.08f );
		State.CameraPosition = bounds.Center + distance * State.CameraRotation.Backward;
	}

	[Event( "scene.session.save" )]
	public void SaveState()
	{
		if ( ProjectCookie is null ) return;

		//Measured runs don't save the camera, so they leave no trace for the user
		if ( Editor.PerfScenario.Measuring ) return;

		Scene scene = Session.Scene;
		if ( !scene.IsValid() )
			return;

		if ( scene.Source is not null )
		{
			//TODO: this should still store something for non-resource scenes?
			ProjectCookie.Set( $"{scene.Source.ResourcePath}.Viewport{ViewportIndex}", new SceneCookie()
			{
				CameraPosition = State.CameraPosition,
				CameraRotation = State.CameraRotation,
				CameraOrthoHeight = State.CameraOrthoHeight,
				CameraOrtho = camera.GizmoOrthoActive,
			} );
		}

		ProjectCookie.Set( $"SceneView.Viewport{ViewportIndex}.Settings.{CookieVersion}", State );
	}

	[Shortcut( "scene.cycle-viewmode", "CTRL+SPACE" )]
	public void CycleViewmode()
	{
		ViewportViewMode newMode = (ViewportViewMode)(((int)State.View + 1) % ((int)ViewportViewMode.Flat2d + 1));
		State.View = newMode == ViewportViewMode.Perspective ? ViewportViewMode.Top2d : newMode; //skip 3d

		Vector3 center = Vector3.Zero;
		int count = 0;

		//TODO: support other (mesh tool?) selections here
		if ( SceneEditorSession.Active is not null )
		{
			foreach ( var selected in SceneEditorSession.Active.Selection.OfType<GameObject>() )
			{
				center += selected.WorldPosition;
				count++;
			}
		}

		if ( count > 0 )
		{
			State.CameraPosition = (center / count) - State.CameraRotation.Forward * (10.16f);
		}
	}
}
