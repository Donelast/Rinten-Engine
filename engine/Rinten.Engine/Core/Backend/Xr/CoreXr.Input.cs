using System.Runtime.InteropServices;
using Silk.NET.OpenXR;
using XrResult = Silk.NET.OpenXR.Result;
using XrAction = Silk.NET.OpenXR.Action;

namespace Rinten;

unsafe partial class CoreXr
{
	//Controller actions matching VRSystem.Input, bound per profile; the game reads actions, not buttons
	internal static class Input
	{
		private static readonly (XrInput Kind, string Name, ActionType Type)[] Actions =
		[
			(XrInput.GripPose, "grip_pose", ActionType.PoseInput),
			(XrInput.AimPose, "aim_pose", ActionType.PoseInput),
			(XrInput.Trigger, "trigger", ActionType.FloatInput),
			(XrInput.Grip, "grip", ActionType.FloatInput),
			(XrInput.Joystick, "joystick", ActionType.Vector2fInput),
			(XrInput.JoystickPress, "joystick_button", ActionType.BooleanInput),
			(XrInput.ButtonA, "button_a", ActionType.BooleanInput),
			(XrInput.ButtonB, "button_b", ActionType.BooleanInput),
			(XrInput.Haptic, "vibrate", ActionType.VibrationOutput),
		];

		//The controllers that are an extension of their own rather than a core profile - enabled where the runtime has them, see CoreXr.Begin.
		internal static readonly string[] OptionalExtensions =
		[
			"XR_BD_controller_interaction",
			"XR_HTC_vive_focus3_controller_interaction",
			"XR_EXT_hp_mixed_reality_controller",
			"XR_FB_touch_controller_pro",
			"XR_META_touch_controller_plus",
		];

		//What each input is called on a controller: one path for both hands, or one for each.
		private readonly record struct Binding( XrInput Kind, string Both, string Left = null, string Right = null );

		private readonly record struct Profile( string Path, string Name, string Extension, Binding[] Bindings );

		private static readonly Binding[] PosesAndBuzz =
		[
			new( XrInput.GripPose, "input/grip/pose" ),
			new( XrInput.AimPose, "input/aim/pose" ),
			new( XrInput.Haptic, "output/haptic" ),
		];

		//A controller shaped like a Touch: a stick, a trigger, a squeeze, and X Y on the left and A B on the right.
		private static Binding[] TouchLike() =>
		[
			.. PosesAndBuzz,
			new( XrInput.Trigger, "input/trigger/value" ),
			new( XrInput.Grip, "input/squeeze/value" ),
			new( XrInput.Joystick, "input/thumbstick" ),
			new( XrInput.JoystickPress, "input/thumbstick/click" ),
			new( XrInput.ButtonA, null, "input/x/click", "input/a/click" ),
			new( XrInput.ButtonB, null, "input/y/click", "input/b/click" ),
		];

		private static readonly Profile[] Profiles =
		[
			new( "/interaction_profiles/oculus/touch_controller", "oculus_touch", null, TouchLike() ),
			new( "/interaction_profiles/facebook/touch_controller_pro", "oculus_touch", "XR_FB_touch_controller_pro", TouchLike() ),
			new( "/interaction_profiles/meta/touch_controller_plus", "oculus_touch", "XR_META_touch_controller_plus", TouchLike() ),
			new( "/interaction_profiles/bytedance/pico4_controller", "pico4_controller", "XR_BD_controller_interaction", TouchLike() ),
			new( "/interaction_profiles/htc/vive_focus3_controller", "vive_focus3_controller", "XR_HTC_vive_focus3_controller_interaction", TouchLike() ),
			new( "/interaction_profiles/hp/mixed_reality_controller", "hpmotioncontroller", "XR_EXT_hp_mixed_reality_controller", TouchLike() ),
			new( "/interaction_profiles/valve/index_controller", "knuckles", null,
			[
				.. PosesAndBuzz,
				new( XrInput.Trigger, "input/trigger/value" ),
				new( XrInput.Grip, "input/squeeze/value" ),
				new( XrInput.Joystick, "input/thumbstick" ),
				new( XrInput.JoystickPress, "input/thumbstick/click" ),
				new( XrInput.ButtonA, "input/a/click" ),
				new( XrInput.ButtonB, "input/b/click" ),
			] ),
			new( "/interaction_profiles/htc/vive_controller", "vive_controller", null,
			[
				.. PosesAndBuzz,
				new( XrInput.Trigger, "input/trigger/value" ),
				new( XrInput.Grip, "input/squeeze/click" ),
				new( XrInput.Joystick, "input/trackpad" ),
				new( XrInput.JoystickPress, "input/trackpad/click" ),
				new( XrInput.ButtonB, "input/menu/click" ),
			] ),
			new( "/interaction_profiles/microsoft/motion_controller", "holographic_controller", null,
			[
				.. PosesAndBuzz,
				new( XrInput.Trigger, "input/trigger/value" ),
				new( XrInput.Grip, "input/squeeze/click" ),
				new( XrInput.Joystick, "input/thumbstick" ),
				new( XrInput.JoystickPress, "input/thumbstick/click" ),
				new( XrInput.ButtonB, "input/menu/click" ),
			] ),
			new( "/interaction_profiles/khr/simple_controller", "generic_tracked", null,
			[
				.. PosesAndBuzz,
				new( XrInput.Trigger, "input/select/click" ),
				new( XrInput.ButtonB, "input/menu/click" ),
			] ),
		];

		private static ulong actionSet;
		private static readonly ulong[] actions = new ulong[Actions.Length];

		// /user/hand/left and /user/hand/right.
		private static readonly ulong[] hands = new ulong[2];

		//Each hand's grip and aim, as spaces the runtime locates.
		private static readonly ulong[] gripSpaces = new ulong[2];
		private static readonly ulong[] aimSpaces = new ulong[2];

		//Where each hand's grip and aim will be when the frame is shown, and whether they are there at all.
		private static readonly Transform[] grips = new Transform[2];
		private static readonly Transform[] aims = new Transform[2];
		private static readonly bool[] tracked = new bool[2];

		//What each hand is holding, by the name VRSystem knows controllers by.
		private static readonly string[] holding = ["unknown", "unknown"];

		//What each hand is holding, by the name its model is under - see XrControllerModels.
		private static readonly string[] models = [null, null];

		private static bool synced;

		//Whether this frame's actions were read - the gaze has a state only then.
		internal static bool Synced => synced;

		internal static void Create()
		{
			hands[0] = Path( "/user/hand/left" );
			hands[1] = Path( "/user/hand/right" );

			var setInfo = new ActionSetCreateInfo { Type = StructureType.ActionSetCreateInfo };
			Write( setInfo.ActionSetName, 64, "gameplay" );
			Write( setInfo.LocalizedActionSetName, 128, "Gameplay" );

			ulong set;
			if ( xr.CreateActionSet( instance, &setInfo, &set ) != XrResult.Success )
			{
				log.Warning( "no controllers: the action set would not be made" );
				return;
			}

			actionSet = set;

			fixed ( ulong* subactions = hands )
			{
				for ( var i = 0; i < Actions.Length; i++ )
				{
					var (_, name, type) = Actions[i];

					var info = new ActionCreateInfo
					{
						Type = StructureType.ActionCreateInfo,
						ActionType = type,
						CountSubactionPaths = 2,
						SubactionPaths = subactions,
					};

					Write( info.ActionName, 64, name );
					Write( info.LocalizedActionName, 128, name.Replace( '_', ' ' ) );

					ulong made;
					if ( xr.CreateAction( actionSet, &info, &made ) == XrResult.Success ) actions[i] = made;
				}
			}

			foreach ( var profile in Profiles ) Suggest( profile );

			//The eyes, where the headset follows them: an action in this same set, since a session attaches one set and no more - see CoreXr.Gaze.
			Gaze.CreateAction( actionSet );

			for ( var hand = 0; hand < 2; hand++ )
			{
				gripSpaces[hand] = ActionSpace( XrInput.GripPose, hand );
				aimSpaces[hand] = ActionSpace( XrInput.AimPose, hand );
			}

			var attach = new SessionActionSetsAttachInfo { Type = StructureType.SessionActionSetsAttachInfo, CountActionSets = 1 };
			var sets = new ActionSet( actionSet );
			attach.ActionSets = &sets;

			var attached = xr.AttachSessionActionSets( session, &attach );
			if ( attached != XrResult.Success ) log.Warning( $"the controllers' actions would not attach ({attached})" );

			Gaze.CreateSpace();
		}

		//One controller's bindings - skipped where its extension is not on or the runtime refuses a path.
		private static void Suggest( Profile profile )
		{
			if ( profile.Extension is not null && !Enabled( profile.Extension ) ) return;

			var bindings = new List<ActionSuggestedBinding>();

			foreach ( var binding in profile.Bindings )
			{
				var action = actions[(int)binding.Kind];
				if ( action == 0 ) continue;

				for ( var hand = 0; hand < 2; hand++ )
				{
					var input = binding.Both ?? ( hand == 0 ? binding.Left : binding.Right );
					if ( input is null ) continue;

					var path = Path( ( hand == 0 ? "/user/hand/left/" : "/user/hand/right/" ) + input );
					if ( path == 0 ) continue;

					bindings.Add( new ActionSuggestedBinding { Action = new XrAction( action ), Binding = path } );
				}
			}

			var list = bindings.ToArray();

			fixed ( ActionSuggestedBinding* suggested = list )
			{
				var info = new InteractionProfileSuggestedBinding
				{
					Type = StructureType.InteractionProfileSuggestedBinding,
					InteractionProfile = Path( profile.Path ),
					CountSuggestedBindings = (uint)list.Length,
					SuggestedBindings = suggested,
				};

				var result = xr.SuggestInteractionProfileBindings( instance, &info );

				//Refused is a runtime that does not know the controller, which is the runtime's business; the others go on regardless.
				if ( result != XrResult.Success ) log.Trace( $"{profile.Path}: not bound ({result})" );
			}
		}

		private static ulong ActionSpace( XrInput kind, int hand )
		{
			if ( actions[(int)kind] == 0 ) return 0;

			var info = new ActionSpaceCreateInfo
			{
				Type = StructureType.ActionSpaceCreateInfo,
				Action = new XrAction( actions[(int)kind] ),
				SubactionPath = hands[hand],
				PoseInActionSpace = new Posef { Orientation = new Quaternionf { W = 1 } },
			};

			ulong made = 0;
			xr.CreateActionSpace( session, &info, &made );
			return made;
		}

		internal static void Destroy()
		{
			Gaze.Destroy();

			for ( var hand = 0; hand < 2; hand++ )
			{
				if ( gripSpaces[hand] != 0 ) xr.DestroySpace( gripSpaces[hand] );
				if ( aimSpaces[hand] != 0 ) xr.DestroySpace( aimSpaces[hand] );
				gripSpaces[hand] = aimSpaces[hand] = 0;
				tracked[hand] = false;
			}

			//The action set and its actions go with the instance.
			actionSet = 0;
			Array.Clear( actions );
			synced = false;
		}

		//The controllers read for the frame about to be made, and where the hands will be when it is shown.
		internal static void Sync( long displayTime )
		{
			synced = false;

			if ( actionSet == 0 ) return;

			//Only while the headset has the user's attention: an unfocused session gets no input, and asking is an error.
			if ( State != SessionState.Focused ) return;

			var active = new ActiveActionSet { ActionSet = new ActionSet( actionSet ) };
			var info = new ActionsSyncInfo { Type = StructureType.ActionsSyncInfo, CountActiveActionSets = 1, ActiveActionSets = &active };

			if ( xr.SyncActions( session, &info ) != XrResult.Success ) return;

			synced = true;

			for ( var hand = 0; hand < 2; hand++ )
			{
				tracked[hand] = Locate( gripSpaces[hand], displayTime, ref grips[hand] );
				Locate( aimSpaces[hand], displayTime, ref aims[hand] );
			}
		}

		private static bool Locate( ulong space, long time, ref Transform into )
		{
			if ( space == 0 ) return false;

			var location = new SpaceLocation { Type = StructureType.SpaceLocation };

			if ( xr.LocateSpace( space, stageSpace, time, &location ) != XrResult.Success ) return false;
			if ( ( location.LocationFlags & SpaceLocationFlags.OrientationValidBit ) == 0 ) return false;

			into = ToTransform( location.Pose );
			return true;
		}

		//The controllers changed: what each hand is holding, by name.
		internal static void ProfileChanged()
		{
			for ( var hand = 0; hand < 2; hand++ )
			{
				var state = new InteractionProfileState { Type = StructureType.InteractionProfileState };

				holding[hand] = "unknown";
				models[hand] = null;

				if ( xr.GetCurrentInteractionProfile( session, hands[hand], &state ) != XrResult.Success ) continue;
				if ( state.InteractionProfile == 0 ) continue;

				var path = PathString( state.InteractionProfile );

				foreach ( var profile in Profiles )
					if ( profile.Path == path ) holding[hand] = profile.Name;

				models[hand] = XrControllerModels.ForInteractionProfile( path );
			}
		}

		//What a hand is holding, by the profile its model is named for - null where nothing is known.
		internal static string Model( int hand ) => hand is 0 or 1 ? models[hand] : null;

		//What a hand is holding - see VRSystem.ControllerTypeFromString.
		internal static string Holding( int hand ) => holding[hand];

		private static ActionStateGetInfo Get( XrInput kind, int hand ) => new()
		{
			Type = StructureType.ActionStateGetInfo,
			Action = new XrAction( actions[(int)kind] ),
			SubactionPath = hands[hand],
		};

		internal static (bool Active, bool Down, bool Changed) Boolean( XrInput kind, int hand )
		{
			if ( !synced || actions[(int)kind] == 0 ) return default;

			var info = Get( kind, hand );
			var state = new ActionStateBoolean { Type = StructureType.ActionStateBoolean };

			if ( xr.GetActionStateBoolean( session, &info, &state ) != XrResult.Success ) return default;

			return (state.IsActive != 0, state.CurrentState != 0, state.ChangedSinceLastSync != 0);
		}

		internal static (bool Active, float Value, bool Changed) Float( XrInput kind, int hand )
		{
			if ( !synced || actions[(int)kind] == 0 ) return default;

			var info = Get( kind, hand );
			var state = new ActionStateFloat { Type = StructureType.ActionStateFloat };

			if ( xr.GetActionStateFloat( session, &info, &state ) != XrResult.Success ) return default;

			return (state.IsActive != 0, state.CurrentState, state.ChangedSinceLastSync != 0);
		}

		internal static (bool Active, Vector2 Value, bool Changed) Vector( XrInput kind, int hand )
		{
			if ( !synced || actions[(int)kind] == 0 ) return default;

			var info = Get( kind, hand );
			var state = new ActionStateVector2f { Type = StructureType.ActionStateVector2f };

			if ( xr.GetActionStateVector2f( session, &info, &state ) != XrResult.Success ) return default;

			return (state.IsActive != 0, new Vector2( state.CurrentState.X, state.CurrentState.Y ), state.ChangedSinceLastSync != 0);
		}

		//A hand's grip or aim, in the play area's space - see VRInput.Anchor.
		internal static (bool Active, Transform Pose) Pose( XrInput kind, int hand )
		{
			if ( !synced ) return default;

			return (tracked[hand], kind == XrInput.AimPose ? aims[hand] : grips[hand]);
		}

		//A buzz in one hand: seconds, hertz, and nought to one.
		internal static void Buzz( int hand, float seconds, float frequency, float amplitude )
		{
			if ( session == 0 || actions[(int)XrInput.Haptic] == 0 ) return;

			var info = new HapticActionInfo
			{
				Type = StructureType.HapticActionInfo,
				Action = new XrAction( actions[(int)XrInput.Haptic] ),
				SubactionPath = hands[hand],
			};

			var vibration = new HapticVibration
			{
				Type = StructureType.HapticVibration,
				Duration = (long)( Math.Max( seconds, 0.0f ) * 1.0e9 ),
				Frequency = frequency,
				Amplitude = Math.Clamp( amplitude, 0.0f, 1.0f ),
			};

			xr.ApplyHapticFeedback( session, &info, &vibration );
		}

		internal static ulong Path( string text )
		{
			var bytes = System.Text.Encoding.ASCII.GetBytes( text + "\0" );
			ulong path = 0;

			fixed ( byte* chars = bytes )
				xr.StringToPath( instance, chars, &path );

			return path;
		}

		private static string PathString( ulong path )
		{
			var buffer = stackalloc byte[256];
			uint length = 0;

			if ( xr.PathToString( instance, path, 256, &length, buffer ) != XrResult.Success ) return null;

			return Marshal.PtrToStringAnsi( (nint)buffer );
		}
	}
}
