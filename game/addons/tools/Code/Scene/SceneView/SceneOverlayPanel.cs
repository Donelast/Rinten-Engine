using Rinten.UI;

namespace Editor;

//It lies over the scene, so it must not take the mouse where it has nothing to click: the layer itself is deaf to the pointer and whatever is put on it says for itself that it is not.
/// <summary>The layer over one viewport's picture, where anything that is only read goes.</summary>
[StyleSheet( "SceneOverlayPanel.cs.scss" )]
public sealed class SceneOverlayPanel : EditorPanel
{
	/// <summary>Down the right, clear of the orientation gizmo above it. Where something that only wants reading goes: the left is the tool strip's, and the top right corner is the gizmo's.</summary>
	public Panel Side { get; }

	public SceneOverlayPanel()
	{
		AddClass( "scene-overlay" );

		Side = new Panel();
		Side.AddClass( "side" );
		AddChild( Side );
	}
}
