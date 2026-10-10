namespace Editor.MdlEditor;

//Gizmo snapping, handle size and grid for the model viewport, kept separate from the scene view's
internal static class PreviewGizmoSettings
{
	//The name carries the unit, the way the scene view's does: these lengths are metres, and
	//a blob written when they were something else would come back as a grid nobody asked for.
	private const string CookieKey = "animgraph.preview.gizmo.metric";

	private static Gizmo.SceneSettings _settings;

	//What the model viewport's gizmos read, as the last session left it.
	internal static Gizmo.SceneSettings Current => _settings ??= Restore();

	//Told when something has changed one, so the next session opens the same way.
	internal static void Save() => EditorCookie.Set( CookieKey, Current );

	private static Gizmo.SceneSettings Restore()
		=> EditorCookie.Get<Gizmo.SceneSettings>( CookieKey, null ) ?? Defaults();

	//10cm grid with snapping off by default; model data rarely sits on a grid
	private static Gizmo.SceneSettings Defaults() => new()
	{
		GridSpacing = 0.1f,
		AngleSpacing = 15.0f,
		SnapToGrid = false,
		SnapToAngles = false,
		GlobalSpace = false,
	};

	//The finest and coarsest the grid goes here, in metres. A millimetre to ten metres. Finer than the scene view's fine end, because a
	//model is worked at a size a level never is, and it stops well short of its coarse one - a grid wider than any model is not a grid.
	internal const float Finest = 0.001f;
	internal const float Coarsest = 10.0f;

	//Half the grid step, or twice it, within those ends. Doubles rather than a ladder of chosen sizes, for the reason the scene view's does
	//it: every coarser grid is then a subset of the finer one, so stepping up thins the lines instead of sliding a different grid over them.
	internal static float Step( float metres, int direction )
	{
		if ( metres <= 0.0f ) return Finest;

		return Math.Clamp( direction < 0 ? metres * 0.5f : metres * 2.0f, Finest, Coarsest );
	}
}

//The few gizmo settings the viewport exposes, in model-friendly units, for a control sheet
internal sealed class PreviewGizmoOptions
{
	private static Gizmo.SceneSettings Settings => PreviewGizmoSettings.Current;

	[Title( "World Space" ), Order( 0 )]
	[Description( "Line the handles up with the world rather than with the thing they are on. X toggles it." )]
	public bool GlobalSpace
	{
		get => Settings.GlobalSpace;
		set => Write( () => Settings.GlobalSpace = value );
	}

	[Title( "Snap To Grid" ), Order( 1 )]
	[Description( "Hold Ctrl while dragging to invert this. G toggles it." )]
	public bool SnapToGrid
	{
		get => Settings.SnapToGrid;
		set => Write( () => Settings.SnapToGrid = value );
	}

	//Shown in centimetres and stored in metres, the way the scene view's box is: a metre is the unit, and nobody placing an eyeball wants to type 0.005.
	[Title( "Grid Step" ), Order( 2 )]
	[Range( 0.1f, 1000.0f, slider: false ), Step( 0.1f )]
	[Description( "In centimetres. What a drag lands on, and what the grid under the model is drawn at.  ·  [ and ]" )]
	public float GridStep
	{
		get => Settings.GridSpacing * 100.0f;
		set => Write( () => Settings.GridSpacing =
			Math.Clamp( value * 0.01f, PreviewGizmoSettings.Finest, PreviewGizmoSettings.Coarsest ) );
	}

	[Title( "Snap Angles" ), Order( 3 )]
	[Description( "Hold Ctrl while dragging to invert this." )]
	public bool SnapToAngles
	{
		get => Settings.SnapToAngles;
		set => Write( () => Settings.SnapToAngles = value );
	}

	[Title( "Angle Step" ), Order( 4 )]
	[Range( 0.25f, 180.0f, slider: false ), Step( 0.25f )]
	[Description( "In degrees. How far one notch of the rotate handle turns something." )]
	public float AngleStep
	{
		get => Settings.AngleSpacing;
		set => Write( () => Settings.AngleSpacing = Math.Clamp( value, 0.25f, 180.0f ) );
	}

	[Title( "Handle Size" ), Order( 5 )]
	[Range( 0.25f, 2.0f ), Step( 0.05f )]
	[Description( "How big the move, rotate and scale handles are drawn." )]
	public float HandleSize
	{
		get => Settings.GizmoScale;
		set => Write( () => Settings.GizmoScale = Math.Clamp( value, 0.25f, 2.0f ) );
	}

	//Change one and keep it, so the next session opens the same way.
	private static void Write( Action change )
	{
		change();
		PreviewGizmoSettings.Save();
	}
}
