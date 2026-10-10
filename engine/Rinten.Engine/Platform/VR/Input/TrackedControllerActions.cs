using Rinten.XR;

namespace Rinten.VR;

internal record struct TrackedControllerActions
{
	public InputFloatActionState Trigger;
	public InputFloatActionState Grip;
	public InputVector2ActionState Joystick;
	public InputBooleanActionState JoystickPress;
	public InputBooleanActionState ButtonA;
	public InputBooleanActionState ButtonB;
}
