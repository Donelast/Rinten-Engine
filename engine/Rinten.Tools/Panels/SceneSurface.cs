using System;

namespace Editor;

/// <summary>The parts of showing a scene that are the same for a widget and for a panel.</summary>
public static class SceneSurface
{
	/// <summary>Create a hidden scene editor camera, post processing will be copied from a main camera in the scene.</summary>
	public static CameraComponent CreateEditorCamera( Scene scene )
	{
		if ( scene is null ) return null;

		using ( scene.Push() )
		{
			var go = new GameObject( true, "editor_camera" );
			go.Flags = GameObjectFlags.Hidden | GameObjectFlags.NotSaved | GameObjectFlags.EditorOnly | GameObjectFlags.Absolute;
			var camera = go.AddComponent<CameraComponent>();
			camera.RenderExcludeTags.Add( "hidden" );
			camera.IsMainCamera = false;
			camera.IsSceneEditorCamera = true;
			return camera;
		}
	}

	/// <summary>The scene camera to draw from, falling back to the scene's own.</summary>
	public static SceneCamera SceneCameraFor( Scene scene, CameraComponent camera )
	{
		if ( camera.IsValid() )
			return camera.SceneCamera;

		if ( !scene.IsValid() )
			return null;

		if ( !scene.Camera.IsValid() )
			return null;

		return scene.Camera.SceneCamera;
	}

	/// <summary>Fills in what the gizmos read from the pointer, in the surface's own pixels.</summary>
	public static void UpdateGizmoInputs( ref Gizmo.Inputs input, SceneCamera camera,
		bool hasMouseFocus, Vector2 screenPosition, Vector2 size )
	{
		ArgumentNullException.ThrowIfNull( camera );

		input.Camera = camera;
		input.Modifiers = Application.KeyboardModifiers;

		//While the game has the mouse there is no pointer for a tool to use: the cursor is frozen wherever the game
		//took it, and the buttons being held are somebody shooting. A gizmo under that point would be dragged by it.
		if ( !hasMouseFocus || GameMode.HasInput )
		{
			input.CursorRay = new Ray();
			return;
		}

		input.CursorPosition = Application.CursorPosition;
		input.LeftMouse = Application.MouseButtons.HasFlag( MouseButtons.Left );
		input.RightMouse = Application.MouseButtons.HasFlag( MouseButtons.Right );

		input.CursorPosition -= screenPosition;
		input.CursorRay = camera.GetRay( input.CursorPosition, size );

		if ( !input.IsHovered )
		{
			input.LeftMouse = false;
			input.RightMouse = false;
		}
	}
}
