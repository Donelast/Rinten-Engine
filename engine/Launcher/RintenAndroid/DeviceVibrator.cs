using System;
using System.Linq;
using Android.Content;
using Android.OS;

namespace Rinten.Android;

//The phone's motor, for Input.Vibrate and haptics with no controller - see CoreInput.HostVibrate.
internal static class DeviceVibrator
{
	//What plays a pattern on this phone, or null where it has no motor.
	public static Action<long[], int[]> For( Context context )
	{
		Vibrator motor = null;

		try
		{
			motor = OperatingSystem.IsAndroidVersionAtLeast( 31 )
				? ( context.GetSystemService( Context.VibratorManagerService ) as VibratorManager )?.DefaultVibrator
#pragma warning disable CA1422 //the old way, for the phones that only have the old way
				: context.GetSystemService( Context.VibratorService ) as Vibrator;
#pragma warning restore CA1422
		}
		catch ( Exception e )
		{
			global::Android.Util.Log.Warn( "rinten", $"no vibrator: {e.Message}" );
		}

		if ( motor is null || !motor.HasVibrator ) return null;

		return ( timings, amplitudes ) => Play( motor, timings, amplitudes );
	}

	//Plays each step for its time at its strength, 0 to 255 - replacing whatever was playing. All of nothing stops it.
	private static void Play( Vibrator motor, long[] timings, int[] amplitudes )
	{
		try
		{
			if ( timings.Sum() <= 0 || amplitudes.All( x => x <= 0 ) )
			{
				motor.Cancel();
				return;
			}

			//A motor that is only on or off is on for anything above nothing.
			var graded = motor.HasAmplitudeControl;

			for ( var i = 0; i < amplitudes.Length; i++ )
			{
				amplitudes[i] = graded ? Math.Clamp( amplitudes[i], 0, 255 ) : ( amplitudes[i] > 0 ? 255 : 0 );
			}

			//The Java object is the system's once it has it; ours is let go after.
			using var effect = timings.Length == 1
				? VibrationEffect.CreateOneShot( timings[0], graded ? amplitudes[0] : VibrationEffect.DefaultAmplitude )
				: VibrationEffect.CreateWaveform( timings, amplitudes, -1 );

			motor.Vibrate( effect );
		}
		catch ( Exception e )
		{
			//A buzz that did not happen is not worth a game that stopped.
			global::Android.Util.Log.Warn( "rinten", $"could not vibrate: {e.Message}" );
		}
	}
}
