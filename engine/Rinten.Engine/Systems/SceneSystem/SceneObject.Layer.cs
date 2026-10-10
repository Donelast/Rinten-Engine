using NativeEngine;

namespace Rinten;

//Kept separate from SceneLayerType, which tells a manually rendered scene object which pass it is in (e.g. shadows).

/// <summary>SceneObjects can be rendered on layers other than the main game layer. This is useful if, for example, you want to render on top of everything without applying post processing.</summary>
public enum SceneRenderLayer
{
	/// <summary>Draw wherever makes sense based on the flags, default behaviour</summary>
	Default,

	/// <summary>Layer drawn on top of everything else - with altered depth</summary>
	ViewModel = 10,

	/// <summary>Overlay - after post processing - but still with the scene's depth</summary>
	OverlayWithDepth = 20,

	/// <summary>Overlay - after post processing - without depth (draw over)</summary>
	OverlayWithoutDepth = 30
}


internal static class SceneRenderLayerHelper
{
	/// <summary>Internally we don't pass these enums, they're just string tokens. No need to expose the actual string tokens to people unless we expose the render pipeline fully to them.</summary>
	public static Dictionary<SceneRenderLayer, string> Names = new Dictionary<SceneRenderLayer, string>()
	{
		{ SceneRenderLayer.ViewModel, "viewmodel" },
		{ SceneRenderLayer.OverlayWithDepth, "OverlayWithDepth" },
		{ SceneRenderLayer.OverlayWithoutDepth, "OverlayWithoutDepth" }
	};
}


public partial class SceneObject
{

	SceneRenderLayer _renderLayer;

	/// <summary>For a layer to draw this object, the target layer must match (or be unset) and the flags must match</summary>
	public SceneRenderLayer RenderLayer
	{
		get => _renderLayer;
		set
		{
			if ( _renderLayer == value ) return;

			_renderLayer = value;
			Touched();
		}
	}


}
