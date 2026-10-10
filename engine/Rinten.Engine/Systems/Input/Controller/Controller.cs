using NativeEngine;

namespace Rinten;

internal sealed partial class Controller
{
	private Color[] ControllerColors = new[]
	{
		Color.Red,
		Color.Green,
		Color.Blue,
		Color.White
	};


	[ConCmd( "controller_debug", ConVarFlags.Protected )]
	public static void ControllerDebug()
	{
		Log.Info( "---------------------------------------------------------------------------" );
		Log.Info( $"Detected {Controller.All.Count()} controllers" );
		foreach ( var controller in Controller.All )
		{
			Log.Info( $"\t> {controller.Name}, GlyphSet: {controller.GlyphSet}, Handle: {controller.SDLHandle}, Device ID: {controller.DeviceId}" );
			Log.Info( $"\t\t> Color: {controller.LEDColor}, GlyphVendor: {controller.GlyphVendor}" );
			Log.Info( $"\t\t> Accel: {controller.Accelerometer}, Gyro: {controller.Gyroscope}" );
		}
		Log.Info( "---------------------------------------------------------------------------" );
	}

	public int SDLHandle { get; init; }
	public int DeviceId { get; init; }

	/// <summary>The glyph set for this controller, used for icon/prompt selection.</summary>
	public GameControllerGlyphSet GlyphSet { get; init; }

	//The open SDL_Gamepad this is, which is what rumble and the light go through. Zero for a controller nothing opened.
	internal IntPtr Handle { get; init; }

	internal Controller( int joystickHandle, int deviceHandle )
	{
		SDLHandle = joystickHandle;
		DeviceId = deviceHandle;
		InputContext = Input.Context.Create( $"GameController:{SDLHandle}" );
	}

	public override string ToString()
	{
		return $"{Name}";
	}

	/// <summary>Gets a sensor reading from the device's gyroscope (if it has one)</summary>
	public Angles Gyroscope
	{
		get
		{
			//No-op: nothing to do here
			return default;
		}
	}

	/// <summary>Gets a sensor reading from the device's accelerometer (if it has one)</summary>
	public Vector3 Accelerometer  => default;

	private Color32 ledColor = Color.White;
	/// <summary>Sets the color of the gamepad if supported</summary>
	public Color32 LEDColor
	{
		get => ledColor;
		set
		{
			//Kept whether or not the pad has a light: a DualSense takes it, an Xbox pad says no, and neither is a reason to forget what was asked for.
			ledColor = value;

			CoreInput.SetLed( Handle, value );
		}
	}

	/// <summary>The name of this controller (e.g. "Xbox Wireless Controller", "Steam Controller")</summary>
	public string Name { get; init; }

	/// <summary>Which glyph folder to use for this controller. Derived from the controller's glyph set.</summary>
	public string GlyphVendor => GlyphSet switch
	{
		GameControllerGlyphSet.Xbox => "xbox",
		GameControllerGlyphSet.PlayStation => "playstation",
		GameControllerGlyphSet.Switch => "switch",
		GameControllerGlyphSet.Steam => "steam",
		_ => "xbox"
	};

	/// <summary>Rumbles the controller.</summary>
	public void Rumble( int leftMotor, int rightMotor, int duration )
	{
		CoreInput.Rumble( Handle, leftMotor, rightMotor, duration );
	}

	/// <summary>Rumbles the controller's triggers (if supported)</summary>
	public void RumbleTriggers( int leftTrigger, int rightTrigger, int duration )
	{
		CoreInput.RumbleTriggers( Handle, leftTrigger, rightTrigger, duration );
	}

	/// <summary>Stops all rumble and haptic events on this controller.</summary>
	public void StopAllHaptics()
	{
		//Nought for as long as nothing, which is how SDL is told to stop.
		CoreInput.Rumble( Handle, 0, 0, 0 );
		CoreInput.RumbleTriggers( Handle, 0, 0, 0 );
	}
}
