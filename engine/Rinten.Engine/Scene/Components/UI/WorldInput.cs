using Rinten.UI;
using Rinten.VR;

namespace Rinten;

/// <summary>A router for world input, the best place to put this is on your player's camera. Uses cursor ray when mouse is active, otherwise the direction of this gameobject. You could also put this on a VR controller to interact with world panels.</summary>
[Title( "World Input" ), Category( "UI" ), Icon( "flip_camera_android" )]
public sealed class WorldInput : Component
{
	//Holds our input state in the UI system.
	internal WorldPanelInput WorldPanelInput = new();

	/// <summary>Which action is our left clicking button?</summary>
	[Property, InputAction] public string LeftMouseAction { get; set; } = "Attack1";

	/// <summary>Which action is our right clicking button?</summary>
	[Property, InputAction] public string RightMouseAction { get; set; } = "Attack2";

	/// <summary>If using VR this will be the hand source for input.</summary>
	[Property] public VRHand.HandSources VRHandSource { get; set; } = VRHand.HandSources.Left;

	/// <summary>The <see cref="Panel"/> that is currently hovered by this input.</summary>
	public Panel Hovered => WorldPanelInput.Hovered;

	protected override void OnUpdate()
	{
		WorldPanelInput.Ray = new Ray( WorldPosition, WorldRotation.Forward );
		WorldPanelInput.MouseLeftPressed = Input.Down( LeftMouseAction );
		WorldPanelInput.MouseRightPressed = Input.Down( RightMouseAction );
		//The screen's input hands the wheel over negated - a turn towards you scrolls the panel down - and a world panel is the same panel.
		WorldPanelInput.MouseWheel = -Input.MouseWheel;

		//If the mouse is active, we want to use the mouse cursor ray instead, and use MOUSE1/2 as backup also. Not in a headset:
		//the mouse is on a desk nobody is looking at, and its ray would come from the camera rather than the hand this sits on.
		if ( Mouse.Active && Scene?.Camera is not null && !Game.IsRunningInVR )
		{
			WorldPanelInput.Ray = Scene.Camera.ScreenPixelToRay( Mouse.Position );
			WorldPanelInput.MouseLeftPressed |= Input.Keyboard.Down( "MOUSE1" );
			WorldPanelInput.MouseRightPressed |= Input.Keyboard.Down( "MOUSE2" );
		}

		//If we're in VR, triggers are mouse presses, joystick scrolls
		if ( Game.IsRunningInVR )
		{
			var hand = (VRHandSource == VRHand.HandSources.Left) ? Input.VR.LeftHand : Input.VR.RightHand;
			WorldPanelInput.MouseLeftPressed = hand.Trigger.Value > 0.75f;
			WorldPanelInput.MouseRightPressed = false;
			WorldPanelInput.MouseWheel = -hand.Joystick.Value;
		}
	}
}
