using Silk.NET.OpenXR;
using XrResult = Silk.NET.OpenXR.Result;
using XrAction = Silk.NET.OpenXR.Action;

namespace Rinten;

unsafe partial class CoreXr
{
	//Eye gaze ray (XR_EXT_eye_gaze_interaction) for foveated rendering; its action joins the input set
	internal static class Gaze
	{
		internal const string Extension = "XR_EXT_eye_gaze_interaction";

		private const string Profile = "/interaction_profiles/ext/eye_gaze_interaction";
		private const string Binding = "/user/eyes_ext/input/gaze_ext/pose";

		private static ulong action;
		private static ulong space;

		//Whether the headset says it follows the eyes at all.
		internal static bool Available { get; private set; }

		//Whether Pose is where the eyes looked at the frame being made.
		internal static bool Tracked { get; private set; }

		//The gaze in the play area's space: from between the eyes, looking along Forward.
		internal static Transform Pose { get; private set; } = Transform.Zero;

		//The action, made in the controllers' set before it is attached. Nothing where the runtime has no eye tracking, or the headset says it has none.
		internal static void CreateAction( ulong actionSet )
		{
			Available = false;

			if ( !Enabled( Extension ) || actionSet == 0 ) return;

			var properties = new SystemProperties { Type = StructureType.SystemProperties };
			var eyes = new SystemEyeGazeInteractionPropertiesEXT { Type = StructureType.SystemEyeGazeInteractionPropertiesExt };
			properties.Next = &eyes;

			if ( xr.GetSystemProperties( instance, systemId, &properties ) != XrResult.Success || eyes.SupportsEyeGazeInteraction == 0 )
			{
				log.Info( $"{SystemName} does not follow the eyes - foveation stays in the middle of each eye" );
				return;
			}

			//No subaction paths: there is one gaze, not one a hand.
			var info = new ActionCreateInfo
			{
				Type = StructureType.ActionCreateInfo,
				ActionType = ActionType.PoseInput,
			};

			Write( info.ActionName, 64, "eye_gaze" );
			Write( info.LocalizedActionName, 128, "eye gaze" );

			ulong made;
			if ( xr.CreateAction( actionSet, &info, &made ) != XrResult.Success ) return;

			action = made;

			var suggested = new ActionSuggestedBinding { Action = new XrAction( action ), Binding = Input.Path( Binding ) };

			var binding = new InteractionProfileSuggestedBinding
			{
				Type = StructureType.InteractionProfileSuggestedBinding,
				InteractionProfile = Input.Path( Profile ),
				CountSuggestedBindings = 1,
				SuggestedBindings = &suggested,
			};

			var bound = xr.SuggestInteractionProfileBindings( instance, &binding );

			if ( bound != XrResult.Success )
			{
				log.Warning( $"the eye gaze would not bind ({bound}) - foveation stays in the middle of each eye" );
				action = 0;
				return;
			}

			Available = true;
		}

		//The space the gaze is located through, once the session has one to give.
		internal static void CreateSpace()
		{
			if ( action == 0 ) return;

			var info = new ActionSpaceCreateInfo
			{
				Type = StructureType.ActionSpaceCreateInfo,
				Action = new XrAction( action ),
				PoseInActionSpace = new Posef { Orientation = new Quaternionf { W = 1 } },
			};

			ulong made = 0;
			if ( xr.CreateActionSpace( session, &info, &made ) == XrResult.Success ) space = made;

			if ( space != 0 ) log.Info( "following the eyes - each eye is drawn finest where it looks" );
		}

		internal static void Destroy()
		{
			if ( space != 0 ) xr.DestroySpace( space );

			//The action goes with the instance, as the controllers' do.
			space = 0;
			action = 0;
			Available = false;
			Tracked = false;
		}

		//Where the eyes will be looking when the frame is shown. After the actions are synced - an unsynced action has no state - and only while it
		//says it is active: an eye tracker that has lost the eyes, or one the player has not allowed, is inactive, and the last ray would then stick.
		internal static void Locate( long time, bool synced )
		{
			Tracked = false;

			if ( space == 0 || !synced ) return;

			var get = new ActionStateGetInfo { Type = StructureType.ActionStateGetInfo, Action = new XrAction( action ) };
			var state = new ActionStatePose { Type = StructureType.ActionStatePose };

			if ( xr.GetActionStatePose( session, &get, &state ) != XrResult.Success || state.IsActive == 0 ) return;

			var location = new SpaceLocation { Type = StructureType.SpaceLocation };

			if ( xr.LocateSpace( space, stageSpace, time, &location ) != XrResult.Success ) return;
			if ( ( location.LocationFlags & SpaceLocationFlags.OrientationValidBit ) == 0 ) return;
			if ( ( location.LocationFlags & SpaceLocationFlags.OrientationTrackedBit ) == 0 ) return;

			Pose = ToTransform( location.Pose );
			Tracked = true;
		}
	}
}
