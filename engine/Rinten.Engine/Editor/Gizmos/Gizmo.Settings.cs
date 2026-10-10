using System.Text.Json.Serialization;

namespace Rinten;

public static partial class Gizmo
{
	public static SceneSettings Settings => Active?.Settings;

	//Bumped when gizmo type enable/disable settings change, so cached handles know to rebuild.
	internal static int GizmoTypeGeneration { get; private set; }

	[Expose]
	public enum GridAxis
	{
		XY,
		YZ,
		ZX,
	}

	[Expose]
	public class SceneSettings
	{
		/// <summary>How do we want to edit this? Usually something like "position", "rotation", "scale" etc</summary>
		public string EditMode { get; set; } = "position";

		/// <summary>Do we want to let the user select things in the current mode?</summary>
		public bool Selection { get; set; } = true;

		/// <summary>What is the current view mode? 3d, 2d, ui?</summary>
		public string ViewMode { get; set; } = "3d";

		/// <summary>Are gizmos enabled?</summary>
		public bool GizmosEnabled { get; set; } = true;

		/// <summary>How big to show the gizmos</summary>
		[Range( 0, 2 )]
		public float GizmoScale { get; set; } = 1.0f;

		/// <summary>When enabled, component gizmo handles are drawn at a fixed world size instead of maintaining a constant screen size regardless of distance.</summary>
		public bool WorldSpaceGizmos { get; set; } = false;

		/// <summary>When enabled, component gizmo handles are depth tested against scene geometry. When disabled, they render on top of everything.</summary>
		public bool GizmoDepthTest { get; set; } = false;

		/// <summary>Maximum distance in metres from the camera at which component gizmo handles are visible. Set to 0 for unlimited distance.</summary>
		[Range( 0, 1000, slider: false ), Step( 5 )]
		public float GizmoRenderDistance { get; set; } = 0;

		/// <summary>Grid line spacing in metres (the box shows cm); half a metre by default.</summary>
		[Range( 0.01f, 10 ), Step( 0.01f )]
		public float GridSpacing { get; set; } = 0.5f;

		//On, the same as angle snapping beside it. Everything the mesh tools measure against is the grid - what a block is dragged out to, what a face is aligned to, what a square of the dev material is worth - so off is the setting somebody has to find before any of that lines up.
		/// <summary>Snap positions to the grid</summary>
		public bool SnapToGrid { get; set; } = true;

		/// <summary>Snap angles</summary>
		public bool SnapToAngles { get; set; } = true;

		/// <summary>Grid spacing</summary>
		[Range( 0.25f, 180f ), Step( 5 )]
		public float AngleSpacing { get; set; } = 15;

		/// <summary>Editing in local space</summary>
		public bool GlobalSpace { get; set; } = false;

		/// <summary>Should we show lines representing GameObject references in action graphs?</summary>
		public bool DebugActionGraphs { get; set; } = false;

		//Which gizmos are disabled
		[JsonInclude]
		Dictionary<string, bool> DisabledGizmos { get; set; } = [];

		//Asked once per component per scene walk now that a switched-off gizmo stops being drawn at all, so the usual answer - nothing is switched off - is given without touching the dictionary or the type's name.
		/// <summary>Check if a gizmo type is enabled</summary>
		public bool IsGizmoEnabled( Type type )
			=> type is not null && (DisabledGizmos.Count == 0 || !DisabledGizmos.GetValueOrDefault( type.FullName ));

		/// <summary>Set the enabled state of a gizmo type</summary>
		public void SetGizmoEnabled( Type type, bool enabled )
		{
			if ( type is null ) return;

			DisabledGizmos[type.FullName] = !enabled;
			GizmoTypeGeneration++;
		}

		/// <summary>Clear all enabled gizmos</summary>
		public void ClearEnabledGizmos()
		{
			DisabledGizmos.Clear();
			GizmoTypeGeneration++;
		}
	}
}
