namespace Rinten;

//WebXR headset (engine half; webxr.js is the page half); one array of doubles handed over per frame
internal static partial class CoreWebXr
{
	private static readonly Diagnostics.Logger log = new( "WebXR" );

	//Whether this run is to look for a headset at all.
	internal static bool Wanted { get; set; }

	//The word each thing lives at. These are webxr.js's; change one and change the other, or a headset tracks nothing and says nothing about why.
	private const int WordActive = 0;
	private const int WordRefresh = 1;
	private const int WordHeadFlags = 2;
	private const int WordHeadPose = 3;
	private const int WordEyes = 10;
	private const int EyeWords = 11;
	private const int WordSizes = 32;
	private const int WordHands = 36;
	private const int HandWords = 21;
	private const int WordJoints = 78;
	private const int JointsAHand = 26;

	private const int WordCount = WordJoints + 2 * JointsAHand * 7;

	//What a hand's flags word says that hand has.
	private const int HasGrip = 1;
	private const int HasAim = 2;
	private const int HasPad = 4;
	private const int HasHand = 8;

	//Filled by the page once a frame, read by everything here.
	private static readonly double[] words = new double[WordCount];

	//Whether the last poll came back with a headset in it.
	private static bool live;

	//Said once, so a page with no headset does not say it every frame.
	private static bool complained;

	//What the headset calls itself. WebXR never says, so it is what it is.
	internal static string SystemName => "WebXR";

	//What the page has to say about the headset - why there is none, or how a session that opened came to close. A
	//browser's console is not reachable with a headset on, so this is the only way the reason gets to the player.
	internal static string Message => XrBind.Attached ? XrBind.Message?.Invoke() ?? "" : "";

	//Whether there is a headset to enter at all. A page answers this through a promise,
	//so it is "ask again" for the first frames - which is why nothing here caches a no.
	internal static bool Hooked => Wanted && XrBind.Attached && XrBind.Supported() == 1;

	//Whether there is a session with a layer, drawn into by this frame.
	internal static bool HasSession => live;

	//What the session is doing, in the engine's words. WebXR has no state machine of OpenXR's kind - a
	//session is being drawn or it is not - so it answers with the two that mean those and Idle for the rest.
	internal static XR.SessionState Session
	{
		get
		{
			if ( live ) return XR.SessionState.Focused;
			if ( !XrBind.Attached ) return XR.SessionState.Unknown;
			return XrBind.Supported() == 1 ? XR.SessionState.Ready : XR.SessionState.Idle;
		}
	}

	//The headset's refresh, where the browser says; nought where it does not.
	internal static float RefreshRate { get; private set; }

	//The size the browser gave each eye's part of its framebuffer.
	internal static (int Width, int Height) EyeSize { get; private set; }

	//Between the eyes, in metres, out of where the browser puts them.
	internal static float EyeDistance { get; private set; }

	//Where the head is, in the play area.
	internal static Transform Head { get; private set; } = Transform.Zero;

	//Whether that is where it is, or only where it is guessed to be.
	internal static bool HeadTracked { get; private set; }

	//This frame's eyes, as the page gave them: where each stands and how wide it sees.
	private static readonly (Transform Pose, float Left, float Right, float Up, float Down)[] eyes = new (Transform, float, float, float, float)[2];

	//Looks for a headset. Nothing is opened here - only a gesture may open a session - so this says what there is and lets the page offer the way in.
	internal static void Begin()
	{
		if ( !Wanted ) return;

		if ( !XrBind.Attached )
		{
			log.Warning( "no headset: the page did not bind WebXR" );
			return;
		}

		//The page draws its own way in for a game that has none: a build asking for VR wants a button, whatever else it has.
		XrBind.Offer?.Invoke( true );
	}

	//Ask for a session. Taken on the next click where this was not called during one.
	internal static void Request()
	{
		if ( !Wanted || !XrBind.Attached ) return;
		XrBind.Enter?.Invoke();
	}

	//End the session and go back to the page.
	internal static void Dismiss()
	{
		if ( !XrBind.Attached ) return;
		XrBind.Leave?.Invoke();
	}

	//The whole headset read into words, and what the rest of the engine asks for taken out of it. False where there was nothing to read.
	private static bool Read()
	{
		live = false;

		if ( !Wanted || !XrBind.Attached ) return false;

		Tune();

		//Before the page writes this frame's words over last frame's - what a button was is the only way to say whether it changed.
		Input.Remember();

		if ( XrBind.Poll( words ) == 0 || words[WordActive] == 0 )
		{
			Complain();
			return false;
		}

		RefreshRate = (float)words[WordRefresh];

		var flags = (int)words[WordHeadFlags];
		HeadTracked = ( flags & 2 ) != 0;
		if ( ( flags & 1 ) != 0 ) Head = Pose( WordHeadPose );

		for ( var i = 0; i < 2; i++ )
		{
			var at = WordEyes + i * EyeWords;

			eyes[i] = (Pose( at ),
				(float)words[at + 7], (float)words[at + 8],
				(float)words[at + 9], (float)words[at + 10]);
		}

		EyeDistance = ( eyes[0].Pose.Position - eyes[1].Pose.Position ).Length;

		//Both eyes are the same size in every headset's framebuffer; the wider of the two is taken so a browser that disagrees still draws whole eyes.
		EyeSize = (
			Math.Max( (int)words[WordSizes], (int)words[WordSizes + 2] ),
			Math.Max( (int)words[WordSizes + 1], (int)words[WordSizes + 3] ));

		live = EyeSize.Width > 0 && EyeSize.Height > 0;

		if ( live ) Said();

		return live;
	}

	//The eye size this session was last heard to have, so it is said once.
	private static (int Width, int Height) saidSize;

	//Logs the eye size and the RenderScale needed for one-to-one with the headset panel
	private static void Said()
	{
		if ( saidSize == EyeSize ) return;

		saidSize = EyeSize;

		var native = BrowserNative.XrNative();

		//RenderScale *is* the framebuffer scale factor the page asks the browser for, and getNativeFramebufferScaleFactor
		//is the one that draws one-to-one - so the number to set is that one, as it stands.
		var one = native > 0.0 ? $", one-to-one at RenderScale {native:F2}" : "";

		log.Info( $"headset eye {EyeSize.Width}x{EyeSize.Height} at RenderScale " +
			$"{Rinten.VR.VRSystem.RenderScale:F2}, foveation {Rinten.VR.VRSystem.Foveation:F2}{one}" );
	}

	//What was last asked of the page, so nothing is asked of it twice.
	private static float tunedScale = -1.0f;
	private static float tunedFoveation = -1.0f;
	private static bool? tunedFingers;

	//How big an eye is drawn and how far its edge is foveated, handed to the page when the game changes its mind. Pushed rather than read once a
	//frame because the size is fixed when a session's layer is made, and a game may set it long before the player enters - see VRSystem.RenderScale.
	private static void Tune()
	{
		//Whether the fingers are worth following, which is a thing the engine learns rather
		//than is told: nothing read a joint lately, so nothing wants one - see Hands.Wanted.
		var fingers = Hands.Wanted;

		if ( fingers != tunedFingers )
		{
			tunedFingers = fingers;
			XrBind.Fingers?.Invoke( fingers );
		}

		var scale = Rinten.VR.VRSystem.RenderScale;
		var foveation = Rinten.VR.VRSystem.Foveation;

		if ( scale == tunedScale && foveation == tunedFoveation ) return;

		tunedScale = scale;
		tunedFoveation = foveation;

		BrowserNative.XrTune( scale, foveation );
	}

	//Why there is no headset, once.
	private static void Complain()
	{
		if ( complained || !Wanted ) return;

		var said = XrBind.Message?.Invoke();
		if ( string.IsNullOrWhiteSpace( said ) ) return;

		complained = true;
		log.Warning( said );
	}

	//Seven words - a place and a turn - as the engine holds them.
	private static Transform Pose( int at ) => new(
		new Vector3( (float)words[at], (float)words[at + 1], (float)words[at + 2] ),
		new Rotation( (float)words[at + 3], (float)words[at + 4], (float)words[at + 5], (float)words[at + 6] ) );
}
