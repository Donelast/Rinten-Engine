namespace Rinten;

//What a hand can be asked for, whichever headset is asked. The actions the VR API reads - see VRSystem.Input: the grip and aim poses,
//the trigger and the grip, the stick and its click, two face buttons and a buzz. A controller without one of them simply never presses it.
internal enum XrInput
{
	GripPose,
	AimPose,
	Trigger,
	Grip,
	Joystick,
	JoystickPress,
	ButtonA,
	ButtonB,
	Haptic,
}

//The one seam that knows which headset backend runs: OpenXR (CoreXr) or WebXR in a page (CoreWebXr)
internal static class CoreHeadset
{
	//Constant on either build, so the side that is not here folds away.
	private static readonly bool web = OperatingSystem.IsBrowser();

	//Whether this run is to look for a headset at all - at launch for a VR game, a Settings/VR-triggered
	//editor restart, or a page the player asked to enter. -novr keeps the desktop probe off.
	internal static bool Wanted
	{
		get => web ? CoreWebXr.Wanted : CoreXr.Wanted;
		set
		{
			if ( web ) CoreWebXr.Wanted = value;
			else CoreXr.Wanted = value;
		}
	}

	//Looks for a headset. Quiet where there is none: that is the ordinary case.
	internal static void Begin()
	{
		if ( web ) CoreWebXr.Begin();
		else CoreXr.Begin();
	}

	//Ask for a session. Only a page has one to ask for: an OpenXR session is made as
	//the device comes up and is not the player's to open, so this is nothing there.
	internal static void Request()
	{
		if ( web ) CoreWebXr.Request();
	}

	//Leave the session, where leaving one is a thing that can be asked.
	internal static void Dismiss()
	{
		if ( web ) CoreWebXr.Dismiss();
	}

	//Whether a headset was found at all - the game may say so before a session exists.
	internal static bool Hooked => web ? CoreWebXr.Hooked : CoreXr.Hooked;

	//Whether there is a session: the headset is ours to draw to.
	internal static bool HasSession => web ? CoreWebXr.HasSession : CoreXr.HasSession;

	//Whether the headset is calling the engine's frame rather than the page's own loop. Only ever true in a page,
	//where the poses and the layer drawn into are good only inside the session's animation frame - see CoreWebXr.Frame.
	internal static bool Driving => web && CoreWebXr.HasSession;

	//What the session is doing, in the engine's own words.
	internal static XR.SessionState Session => web ? CoreWebXr.Session : CoreXr.Session;

	//What the headset calls itself.
	internal static string SystemName => web ? CoreWebXr.SystemName : CoreXr.SystemName;

	//Why there is no headset, or how the last session ended. Empty where there is nothing to say.
	internal static string Message => web ? CoreWebXr.Message : CoreXr.Message;

	//The headset's refresh, as far as the frames it asks for say.
	internal static float RefreshRate => web ? CoreWebXr.RefreshRate : CoreXr.RefreshRate;

	//The size to draw each eye at.
	internal static (int Width, int Height) EyeSize => web ? CoreWebXr.EyeSize : CoreXr.EyeSize;

	//Between the eyes, in metres.
	internal static float EyeDistance => web ? CoreWebXr.EyeDistance : CoreXr.EyeDistance;

	//Where the head is, in the play area.
	internal static Transform Head => web ? CoreWebXr.Head : CoreXr.Head;

	//Whether that is where the head is or only where it is guessed to be.
	internal static bool HeadTracked => web ? CoreWebXr.HeadTracked : CoreXr.HeadTracked;

	//The headset's next frame waited for, and the head and the hands read for the
	//tick that follows - so everything the tick reads is where it will be seen.
	internal static void FrameStart()
	{
		if ( web ) CoreWebXr.FrameStart();
		else CoreXr.FrameStart();
	}

	//Each eye drawn from the scene's camera and the frame handed back.
	internal static void FrameEnd( Scene scene )
	{
		if ( web ) CoreWebXr.FrameEnd( scene );
		else CoreXr.FrameEnd( scene );
	}

	//What the controllers are doing.
	internal static class Input
	{
		//What is in this hand, for the info command.
		internal static string Holding( int hand )
			=> web ? CoreWebXr.Input.Holding( hand ) : CoreXr.Input.Holding( hand );

		//What is in this hand, by the profile its model is named for - see XrControllerModels.
		internal static string Model( int hand )
			=> web ? CoreWebXr.Input.Model( hand ) : CoreXr.Input.Model( hand );

		internal static (bool Active, bool Down, bool Changed) Boolean( XrInput kind, int hand )
			=> web ? CoreWebXr.Input.Boolean( kind, hand ) : CoreXr.Input.Boolean( kind, hand );

		internal static (bool Active, float Value, bool Changed) Float( XrInput kind, int hand )
			=> web ? CoreWebXr.Input.Float( kind, hand ) : CoreXr.Input.Float( kind, hand );

		internal static (bool Active, Vector2 Value, bool Changed) Vector( XrInput kind, int hand )
			=> web ? CoreWebXr.Input.Vector( kind, hand ) : CoreXr.Input.Vector( kind, hand );

		internal static (bool Active, Transform Pose) Pose( XrInput kind, int hand )
			=> web ? CoreWebXr.Input.Pose( kind, hand ) : CoreXr.Input.Pose( kind, hand );

		internal static void Buzz( int hand, float seconds, float frequency, float amplitude )
		{
			if ( web ) CoreWebXr.Input.Buzz( hand, seconds, amplitude );
			else CoreXr.Input.Buzz( hand, seconds, frequency, amplitude );
		}
	}

	//Where the eyes are looking, where the headset follows them. A page is never told - WebXR has no eye tracking - so there it is never tracked.
	internal static class Gaze
	{
		//Whether the headset follows the eyes at all.
		internal static bool Available => !web && CoreXr.Gaze.Available;

		//Whether Pose is this frame's.
		internal static bool Tracked => !web && CoreXr.Gaze.Tracked;

		//In the play area: from between the eyes, looking along Forward.
		internal static Transform Pose => web ? Transform.Zero : CoreXr.Gaze.Pose;
	}

	//The fingers, where the headset follows them.
	internal static class Hands
	{
		internal static bool Available => web ? CoreWebXr.Hands.Available : CoreXr.Hands.Available;

		internal static bool Seen( int hand, bool roundController )
			=> web ? CoreWebXr.Hands.Seen( hand, roundController ) : CoreXr.Hands.Seen( hand, roundController );

		internal static bool Joint( int hand, bool roundController, int joint, out Transform pose )
			=> web
				? CoreWebXr.Hands.Joint( hand, roundController, joint, out pose )
				: CoreXr.Hands.Joint( hand, roundController, joint, out pose );
	}
}
