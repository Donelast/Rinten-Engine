namespace Rinten;

public static partial class Input
{
	//SDL will translate these commands into haptic pulses that should work on all controller types.
	/// <summary>Trigger a haptic event on supported controllers including Xbox trigger impulse rumble.</summary>
	public static void TriggerHaptics( float leftMotor, float rightMotor, float leftTrigger = 0.0f, float rightTrigger = 0.0f, int duration = 500 )
	{
		var controller = Input.CurrentController;

		//No controller: the device in the player's hand, where it has a motor - a phone. One motor, so the strongest of the four is what it gets.
		if ( controller is null )
		{
			Vibrate( duration, Math.Max( Math.Max( leftMotor, rightMotor ), Math.Max( leftTrigger, rightTrigger ) ) );
			return;
		}

		if ( controller.ActiveHapticEffect != null )
		{
			controller.StopAllVibrations();
		}

		if ( leftMotor > 0f || rightMotor > 0f )
		{
			controller.Rumble( leftMotor.Remap( 0, 1, 0, 0xFFFF, true ).CeilToInt(), rightMotor.Remap( 0, 1, 0, 0xFFFF, true ).CeilToInt(), duration );
		}

		if ( leftTrigger > 0f || rightTrigger > 0f )
		{
			controller.RumbleTriggers( leftTrigger.Remap( 0, 1, 0, 0xFFFF, true ).CeilToInt(), rightTrigger.Remap( 0, 1, 0, 0xFFFF, true ).CeilToInt(), duration );
		}
	}

	/// <summary>Trigger haptics based on a predefined <see cref="HapticEffect"/>. All <see cref="HapticEffect"/>s are normalized (start at 0, peak at 1).</summary>
	public static void TriggerHaptics( HapticEffect pattern, float lengthScale = 1.0f, float frequencyScale = 1.0f, float amplitudeScale = 1.0f )
	{
		var controller = Input.CurrentController;

		if ( controller is null )
		{
			CoreInput.VibrateDevice( pattern?.ControllerPattern, lengthScale, amplitudeScale );
			return;
		}

		controller.TriggerHapticEffect( pattern, lengthScale, frequencyScale, amplitudeScale );
	}

	/// <summary>Trigger haptics based on a predefined <see cref="HapticEffect"/>. All <see cref="HapticEffect"/>s are normalized (start at 0, peak at 1).</summary>
	public static void TriggerHaptics( HapticEffect pattern, float frequencyScale, float amplitudeScale )
	{
		var controller = Input.CurrentController;

		if ( controller is null )
		{
			CoreInput.VibrateDevice( pattern?.ControllerPattern, 1.0f, amplitudeScale );
			return;
		}

		controller.TriggerHapticEffect( pattern, 1.0f, frequencyScale, amplitudeScale );
	}

	/// <summary>Stop all vibration events on the current controller.</summary>
	public static void StopAllHaptics()
	{
		var controller = Input.CurrentController;

		if ( controller is null )
		{
			//A step of nothing replaces whatever the device was playing.
			CoreInput.VibrateDevice( [0], [0f] );
			return;
		}

		controller.StopAllHaptics();
	}
}
