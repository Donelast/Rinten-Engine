namespace Rinten;

partial class CoreWebXr
{
	//Maps xr-standard gamepad words from webxr.js writeHand to engine controller inputs
	internal static class Input
	{
		//Where a hand's things sit inside its own words.
		private const int FlagsWord = 0;
		private const int GripPoseWord = 1;
		private const int AimPoseWord = 8;
		private const int TriggerWord = 15;
		private const int SqueezeWord = 16;
		private const int StickXWord = 17;
		private const int StickYWord = 18;
		private const int ButtonsWord = 19;
		private const int ProfileWord = 20;

		//Which bit of the buttons word is which - webxr.js reads the xr-standard gamepad,
		//where three and four and five are the stick's click and the two face buttons.
		private const int StickDown = 1;
		private const int ButtonADown = 2;
		private const int ButtonBDown = 4;

		//The hands as they were the frame before, for whether a button changed. Kept here because the words themselves are written over each frame.
		private static readonly double[] was = new double[2 * HandWords];

		//What was read last frame put aside, before the page writes this frame's over it.
		internal static void Remember() => Array.Copy( words, WordHands, was, 0, was.Length );

		private static double Word( int hand, int word ) => words[WordHands + hand * HandWords + word];
		private static double Before( int hand, int word ) => was[hand * HandWords + word];

		private static bool Has( int hand, int flag ) => ( (int)Word( hand, FlagsWord ) & flag ) != 0;

		//What is in this hand, by the name the profile list gave it.
		internal static string Holding( int hand )
		{
			if ( !live || hand is < 0 or > 1 || !Has( hand, HasPad | HasGrip ) ) return "nothing";

			var profile = Model( hand ) ?? "";

			if ( profile.StartsWith( "meta-" ) || profile.StartsWith( "oculus-" ) ) return "Meta Touch";
			if ( profile == "valve-index" ) return "Valve Index";
			if ( profile == "htc-vive-focus-3" ) return "Vive Focus 3";
			if ( profile.StartsWith( "htc-vive" ) ) return "HTC Vive";
			if ( profile is "microsoft-mixed-reality" or "samsung-odyssey" ) return "Windows Mixed Reality";
			if ( profile == "hp-mixed-reality" ) return "HP Reverb";
			if ( profile.StartsWith( "pico-" ) ) return "Pico";
			if ( profile.StartsWith( "generic-" ) ) return "a plain controller";

			return Has( hand, HasHand ) ? "a hand" : "a controller";
		}

		//What is in this hand, by the profile its model is named for: the first of the page's profile list that ProfileCodes knows - see XrControllerModels.
		internal static string Model( int hand )
		{
			if ( !live || hand is < 0 or > 1 ) return null;

			return XrControllerModels.ForWebCode( (int)Word( hand, ProfileWord ) );
		}

		internal static (bool Active, bool Down, bool Changed) Boolean( XrInput kind, int hand )
		{
			if ( !live || hand is < 0 or > 1 || !Has( hand, HasPad ) ) return default;

			var bit = kind switch
			{
				XrInput.JoystickPress => StickDown,
				XrInput.ButtonA => ButtonADown,
				XrInput.ButtonB => ButtonBDown,
				_ => 0,
			};

			if ( bit == 0 ) return default;

			var down = ( (int)Word( hand, ButtonsWord ) & bit ) != 0;
			var before = ( (int)Before( hand, ButtonsWord ) & bit ) != 0;

			return (true, down, down != before);
		}

		internal static (bool Active, float Value, bool Changed) Float( XrInput kind, int hand )
		{
			if ( !live || hand is < 0 or > 1 || !Has( hand, HasPad ) ) return default;

			var word = kind switch
			{
				XrInput.Trigger => TriggerWord,
				XrInput.Grip => SqueezeWord,
				_ => -1,
			};

			if ( word < 0 ) return default;

			var value = (float)Word( hand, word );

			return (true, value, value != (float)Before( hand, word ));
		}

		internal static (bool Active, Vector2 Value, bool Changed) Vector( XrInput kind, int hand )
		{
			if ( !live || hand is < 0 or > 1 || kind != XrInput.Joystick || !Has( hand, HasPad ) ) return default;

			var value = new Vector2( (float)Word( hand, StickXWord ), (float)Word( hand, StickYWord ) );
			var before = new Vector2( (float)Before( hand, StickXWord ), (float)Before( hand, StickYWord ) );

			return (true, value, value != before);
		}

		internal static (bool Active, Transform Pose) Pose( XrInput kind, int hand )
		{
			if ( !live || hand is < 0 or > 1 ) return (false, Transform.Zero);

			var (word, flag) = kind switch
			{
				XrInput.GripPose => (GripPoseWord, HasGrip),
				XrInput.AimPose => (AimPoseWord, HasAim),
				_ => (-1, 0),
			};

			if ( word < 0 || !Has( hand, flag ) ) return (false, Transform.Zero);

			return (true, CoreWebXr.Pose( WordHands + hand * HandWords + word ));
		}

		//A hand buzzed. A browser's actuator takes how hard and for how long and has no frequency to give, so the one OpenXR is told is dropped here.
		internal static void Buzz( int hand, float seconds, float amplitude )
		{
			if ( !live || hand is < 0 or > 1 ) return;

			XrBind.Buzz?.Invoke( hand, seconds, Math.Clamp( amplitude, 0.0f, 1.0f ) );
		}
	}

	//Hand joints in OpenXR order; WebXR has one answer, used for both motion ranges
	internal static class Hands
	{
		internal static bool Available => live && ( Seen( 0, false ) || Seen( 1, false ) );

		internal static bool Seen( int hand, bool roundController )
		{
			_ = roundController;

			return live && hand is >= 0 and <= 1 &&
				( (int)words[WordHands + hand * HandWords] & HasHand ) != 0;
		}

		//When a joint was last asked for. What the page does with it is in Wanted; what it is doing here is that this is the one place a joint is ever read.
		private static long asked;

		//Whether the page should keep following the fingers. A second's grace, so a game that reads them
		//every frame never sees a gap and one that stops reading them stops paying for them a second later.
		internal static bool Wanted => Environment.TickCount64 - asked < 1000;

		internal static bool Joint( int hand, bool roundController, int joint, out Transform pose )
		{
			asked = Environment.TickCount64;

			pose = Transform.Zero;

			if ( !Seen( hand, roundController ) || joint < 0 || joint >= JointsAHand ) return false;

			pose = CoreWebXr.Pose( WordJoints + ( hand * JointsAHand + joint ) * 7 );
			return true;
		}
	}
}
