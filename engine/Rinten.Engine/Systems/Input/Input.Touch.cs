namespace Rinten;

/// <summary>Where a finger is in its time on the screen.</summary>
public enum TouchPhase
{
	/// <summary>Put down this frame.</summary>
	Began,

	/// <summary>Down, and moved since the last frame.</summary>
	Moved,

	/// <summary>Down, and where it was last frame.</summary>
	Stationary,

	/// <summary>Lifted this frame. Gone from <see cref="Input.Touches"/> the frame after.</summary>
	Ended,

	/// <summary>Taken away by the system this frame rather than lifted - a gesture it kept for itself, the application going into the background. Not a tap.</summary>
	Cancelled,
}

/// <summary>One finger on the screen, as this frame sees it.</summary>
public readonly record struct Touch( int Id, Vector2 Position, Vector2 Delta, Vector2 StartPosition, TouchPhase Phase )
{
	/// <summary>Whether the finger is still on the screen.</summary>
	public bool IsDown => Phase is TouchPhase.Began or TouchPhase.Moved or TouchPhase.Stationary;
}

public static partial class Input
{
	/// <summary>Current and just-lifted touches in press order; the first finger also drives the mouse</summary>
	public static IReadOnlyList<Touch> Touches => CoreInput.Touches;

	/// <summary>Whether this frame's input has come from a touch screen - a finger has been put down since the application started.</summary>
	public static bool UsingTouch => CoreInput.TouchSeen;

	//Nothing where the device has no motor, or the platform no way to reach it.
	/// <summary>Vibrates the device itself - a phone in the player's hand - rather than a controller.</summary>
	public static void Vibrate( int milliseconds, float strength = 1.0f )
	{
		if ( milliseconds <= 0 || strength <= 0 ) return;

		CoreInput.VibrateDevice( [milliseconds], [strength] );
	}
}
