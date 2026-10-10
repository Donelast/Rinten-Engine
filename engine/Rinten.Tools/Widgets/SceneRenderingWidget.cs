using Rinten.Engine.Settings;
using System;

namespace Editor;

/// <summary>Render a scene to a native widget. This replaces NativeRenderingWidget.</summary>
public partial class SceneRenderingWidget : Frame
{
	/// <summary>The active scene that we're rendering</summary>
	public Scene Scene { get; set; }

	/// <summary>The camera to render from. We will fallback to Scene.Camera if this is null</summary>
	public CameraComponent Camera { get; set; }

	/// <summary>This widget manages it's own gizmo instance.</summary>
	public Gizmo.Instance GizmoInstance { get; private set; } = new();

	/// <summary>True when a handle was under the cursor last frame, or is held now.</summary>
	public bool GizmoHasPointer =>
		!string.IsNullOrEmpty( GizmoInstance?.builder.HoveredPath )
		|| !string.IsNullOrEmpty( GizmoInstance?.current.HoveredPath )
		|| !string.IsNullOrEmpty( GizmoInstance?.current.PressedPath );

	public bool EnableEngineOverlays { get; set; } = false;

	/// <summary>Editor grid, light/camera icons, and selection. The scene view turns this on.</summary>
	public bool ShowEditorOverlays { get; set; }

	/// <summary>Whether the grid on the ground is drawn. A 2D view turns it off and draws its own.</summary>
	public bool ShowGroundGrid { get; set; } = true;

	/// <summary>Whether the grid is cut into the faces of the editable meshes.</summary>
	public bool ShowSurfaceGrid { get; set; } = true;


	public SceneRenderingWidget( Widget parent = null ) : base( parent )
	{
		//Ours has no window of its own to hand a renderer: the scene is drawn into a picture and this widget wears it.
		FocusMode = FocusMode.Click;
		MouseTracking = true;

		RenderSettings.Instance.OnVideoSettingsChanged += HandleVideoChanged;

		//Now, not on first use: making it is what enters this widget in the list of scenes the frame draws. See CreateRenderCore.
		CreateRenderCore();
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		//The view the game had the mouse in is going away, and the mouse with it - a
		//cursor left hidden in a widget that no longer exists is one nothing gives back.
		if ( GameMode.IsPlaySurface( this ) ) GameMode.ClearPlayMode();

		RenderSettings.Instance.OnVideoSettingsChanged -= HandleVideoChanged;

		ReleaseManaged();

		GizmoInstance?.Dispose();
		GizmoInstance = default;
	}

	/// <summary>Create a hidden scene editor camera, post processing will be copied from a main camera in the scene.</summary>
	public CameraComponent CreateSceneEditorCamera() => SceneSurface.CreateEditorCamera( Scene );

	public event Action OnPreFrame;

	//Called just before rendering.
	protected virtual void PreFrame()
	{
		OnPreFrame?.Invoke();
	}

	/// <summary>Update common inputs for gizmo.</summary>
	public void UpdateGizmoInputs( bool hasMouseFocus = true )
	{
		var camera = GetSceneCamera();
		if ( camera is null ) return;

		UpdateGizmoInputs( ref GizmoInstance.Input, camera, hasMouseFocus );
	}

	private void UpdateGizmoInputs( ref Gizmo.Inputs input, SceneCamera camera, bool hasMouseFocus )
		=> SceneSurface.UpdateGizmoInputs( ref input, camera, hasMouseFocus, ScreenPosition, Size );

	private SceneCamera GetSceneCamera() => SceneSurface.SceneCameraFor( Scene, Camera );

	/// <summary>Return a ray for the current cursor position</summary>
	public Ray CursorRay
	{
		get => GetRay( Application.CursorPosition - ScreenPosition );
	}

	/// <summary>Given a local widget position, return a Ray</summary>
	public Ray GetRay( Vector2 localPosition )
	{
		var camera = GetSceneCamera();
		if ( camera is null )
			return default;

		return camera.GetRay( localPosition, Size );
	}

	internal void HandleVideoChanged()
	{
		//Nothing here is a swapchain of theirs where the editor is ours.
		return;
	}

}
