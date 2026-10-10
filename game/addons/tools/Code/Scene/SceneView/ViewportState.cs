using System.Text.Json.Serialization;

namespace Editor;

/// <summary>What a single viewport is looking at the scene with.</summary>
public enum ViewportViewMode
{
	[Title( "3D" ), Icon( "view_in_ar" )]
	Perspective,
	[Title( "Top 2D" ), Icon( "roofing" )]
	Top2d,
	[Title( "Front 2D" ), Icon( "cottage" )]
	Front2d,
	[Title( "Side 2D" ), Icon( "gite" )]
	Side2d,
	[Title( "Flat 2D (Screen)" ), Icon( "grid_on" )]
	Flat2d
}

/// <summary>How many viewports a scene view is split into, and where.</summary>
public enum ViewportLayoutMode
{
	[Title( "One" ), Icon( "check_box_outline_blank" )]
	One,
	[Title( "Two (Horizontal)" ), Icon( "view_column" )]
	TwoHorizontal,
	[Title( "Two (Vertical)" ), Icon( "view_agenda" )]
	TwoVertical,
	[Title( "Three (Left)" ), Icon( "space_dashboard" )]
	ThreeLeft,
	[Title( "Three (Right)" ), Icon( "space_dashboard" )]
	ThreeRight,
	[Title( "Three (Top)" ), Icon( "space_dashboard" )]
	ThreeTop,
	[Title( "Three (Bottom)" ), Icon( "space_dashboard" )]
	ThreeBottom,
	[Title( "Four" ), Icon( "grid_view" )]
	Four
}

/// <summary>Whether a scene view is showing its own scene or a game being played.</summary>
public enum SceneViewMode
{
	Scene,
	Game
}

//Written to a cookie as it stands, so what is in here is what comes back when the editor is opened again.
/// <summary>Everything one viewport remembers about how it is looking at the scene.</summary>
public class ViewportState
{
	public Vector3 CameraPosition { get; set; }
	public Rotation CameraRotation { get; set; }

	/// <summary>View mode of this viewport</summary>
	[EnumDropdown]
	public ViewportViewMode View
	{
		get => _mode;
		set => SetViewmode( value );
	}
	private ViewportViewMode _mode;

	[JsonIgnore] public bool Is2D => View != ViewportViewMode.Perspective;

	/// <summary>Render mode to use for this viewport</summary>
	public SceneCameraDebugMode RenderMode { get; set; } = SceneCameraDebugMode.Normal;

	/// <summary>Should the scene render in wireframe mode</summary>
	public bool WireframeMode { get; set; } = false;

	/// <summary>Should we render post processing effects?</summary>
	public bool EnablePostProcessing { get; set; } = true;

	/// <summary>Enable the default directional light when editing prefabs.</summary>
	public bool EnablePrefabLighting { get; set; } = true;

	/// <summary>Show the grid</summary>
	public bool ShowGrid { get; set; } = true;

	/// <summary>Should we fade the grid</summary>
	[Range( 0.0f, 1.0f )]
	public float GridOpacity { get; set; } = 0.2f;

	/// <summary>The plane the grid is shown on</summary>
	public Gizmo.GridAxis GridAxis { get; set; } = Gizmo.GridAxis.ZX;

	/// <summary>The orthographic size for the camera</summary>
	[Title( "Orthographic Height" )]
	public float CameraOrthoHeight { get; set; } = 25.0f;

	private void SetViewmode( ViewportViewMode viewmode )
	{
		_mode = viewmode;

		switch ( viewmode )
		{
			//Looking down, with X to the right and Z down the screen: the ground plane, seen from above.
			case ViewportViewMode.Top2d:
				CameraRotation = Rotation.LookAt( Vector3.Down, Vector3.Forward );
				GridAxis = Gizmo.GridAxis.ZX;
				break;

			//X+ Right, Y+ Up. The camera stands on +Z and looks back along -Z, which is where a Vector2
			//taken into the world as (x, y, 0) faces - so this and Flat2d are the same view now.
			case ViewportViewMode.Front2d:
			case ViewportViewMode.Flat2d:
				CameraRotation = Rotation.LookAt( Vector3.Forward, Vector3.Up );
				GridAxis = Gizmo.GridAxis.XY;
				break;

			case ViewportViewMode.Side2d:
				CameraRotation = Rotation.LookAt( Vector3.Left, Vector3.Up );
				GridAxis = Gizmo.GridAxis.YZ;
				break;

			default:
				GridAxis = Gizmo.GridAxis.ZX;
				break;
		}
	}

	internal static Gizmo.GridAxis GridAxisForDirection( Vector3 axisDir )
	{
		float[] a = [MathF.Abs( axisDir.x ), MathF.Abs( axisDir.y ), MathF.Abs( axisDir.z )];
		return Array.IndexOf( a, a.Max() ) switch
		{
			0 => Gizmo.GridAxis.YZ,
			1 => Gizmo.GridAxis.ZX,
			_ => Gizmo.GridAxis.XY
		};
	}
}
