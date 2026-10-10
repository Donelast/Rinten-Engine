using Rinten.Rendering;

namespace Rinten;

public sealed partial class CameraComponent : Component, Component.ExecuteInEditor
{
	CommandList _hudCommandList = new CommandList( "Hud" ) { Flags = CommandList.Flag.Hud };

	/// <summary>Allows drawing on the camera. This is drawn before the post processing.</summary>
	public HudPainter Hud => new HudPainter( _hudCommandList );

	CommandList _overlayCommandList = new CommandList( "Overlay" ) { Flags = CommandList.Flag.Hud };

	/// <summary>Used to draw to the screen. This is drawn on top of everything, so is good for debug overlays etc.</summary>
	public HudPainter Overlay => new HudPainter( _overlayCommandList );

	void Component.ISceneStage.Start()
	{
		//Clear the HUD at the start of every frame, so that subsequent Update()'s will give it fresh data.
		_hudCommandList.Reset();
		_overlayCommandList.Reset();
	}
}
