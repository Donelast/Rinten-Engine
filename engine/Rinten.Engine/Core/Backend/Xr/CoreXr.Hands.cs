using Silk.NET.OpenXR;
using XrResult = Silk.NET.OpenXR.Result;

namespace Rinten;

unsafe partial class CoreXr
{
	//Hand joints (XR_EXT_hand_tracking), 26 per hand in VRHandJoint order; motion range where supported
	internal static class Hands
	{
		internal const string Extension = "XR_EXT_hand_tracking";
		internal const string RangeExtension = "XR_EXT_hand_joints_motion_range";

		private const int Joints = 26;

		private static delegate* unmanaged[Cdecl]<ulong, HandTrackerCreateInfoEXT*, ulong*, XrResult> createTracker;
		private static delegate* unmanaged[Cdecl]<ulong, XrResult> destroyTracker;
		private static delegate* unmanaged[Cdecl]<ulong, HandJointsLocateInfoEXT*, HandJointLocationsEXT*, XrResult> locate;

		private static readonly ulong[] trackers = new ulong[2];

		//Each hand's joints, free and round the controller, and whether each was there.
		private static readonly HandJointLocationEXT[][] free = [new HandJointLocationEXT[Joints], new HandJointLocationEXT[Joints]];
		private static readonly HandJointLocationEXT[][] gripping = [new HandJointLocationEXT[Joints], new HandJointLocationEXT[Joints]];
		private static readonly bool[] freeActive = new bool[2];
		private static readonly bool[] grippingActive = new bool[2];

		//Whether this runtime and headset follow the hands at all.
		internal static bool Available => trackers[0] != 0 || trackers[1] != 0;

		internal static void Create()
		{
			if ( !Enabled( Extension ) ) return;

			var properties = new SystemProperties { Type = StructureType.SystemProperties };
			var hands = new SystemHandTrackingPropertiesEXT { Type = StructureType.SystemHandTrackingPropertiesExt };
			properties.Next = &hands;

			if ( xr.GetSystemProperties( instance, systemId, &properties ) != XrResult.Success || hands.SupportsHandTracking == 0 )
			{
				log.Info( $"{SystemName} does not follow the hands - the fingers are the controllers' estimate or nothing" );
				return;
			}

			createTracker = (delegate* unmanaged[Cdecl]<ulong, HandTrackerCreateInfoEXT*, ulong*, XrResult>)loader.Proc( instance, "xrCreateHandTrackerEXT" );
			destroyTracker = (delegate* unmanaged[Cdecl]<ulong, XrResult>)loader.Proc( instance, "xrDestroyHandTrackerEXT" );
			locate = (delegate* unmanaged[Cdecl]<ulong, HandJointsLocateInfoEXT*, HandJointLocationsEXT*, XrResult>)loader.Proc( instance, "xrLocateHandJointsEXT" );

			if ( createTracker == null || destroyTracker == null || locate == null ) return;

			for ( var hand = 0; hand < 2; hand++ )
			{
				var info = new HandTrackerCreateInfoEXT
				{
					Type = StructureType.HandTrackerCreateInfoExt,
					Hand = hand == 0 ? HandEXT.LeftExt : HandEXT.RightExt,
					HandJointSet = HandJointSetEXT.DefaultExt,
				};

				ulong made;
				if ( createTracker( session, &info, &made ) == XrResult.Success ) trackers[hand] = made;
			}

			if ( Available ) log.Info( $"following the hands{( Enabled( RangeExtension ) ? ", free and round the controllers" : "" )}" );
		}

		internal static void Destroy()
		{
			for ( var hand = 0; hand < 2; hand++ )
			{
				if ( trackers[hand] != 0 && destroyTracker != null ) destroyTracker( trackers[hand] );

				trackers[hand] = 0;
				freeActive[hand] = grippingActive[hand] = false;
			}
		}

		//Where every joint will be when the frame is shown.
		internal static void Locate( long time )
		{
			if ( !Available ) return;

			var ranged = Enabled( RangeExtension );

			for ( var hand = 0; hand < 2; hand++ )
			{
				if ( trackers[hand] == 0 ) continue;

				freeActive[hand] = LocateOne( hand, time, ranged ? HandJointsMotionRangeEXT.UnobstructedExt : null, free[hand] );

				if ( ranged )
				{
					grippingActive[hand] = LocateOne( hand, time, HandJointsMotionRangeEXT.ConformingToControllerExt, gripping[hand] );
				}
				else
				{
					Array.Copy( free[hand], gripping[hand], Joints );
					grippingActive[hand] = freeActive[hand];
				}
			}
		}

		private static bool LocateOne( int hand, long time, HandJointsMotionRangeEXT? range, HandJointLocationEXT[] into )
		{
			var motion = new HandJointsMotionRangeInfoEXT
			{
				Type = StructureType.HandJointsMotionRangeInfoExt,
				HandJointsMotionRange = range ?? HandJointsMotionRangeEXT.UnobstructedExt,
			};

			var info = new HandJointsLocateInfoEXT
			{
				Type = StructureType.HandJointsLocateInfoExt,
				Next = range is null ? null : &motion,
				BaseSpace = new Space( stageSpace ),
				Time = time,
			};

			fixed ( HandJointLocationEXT* joints = into )
			{
				var locations = new HandJointLocationsEXT
				{
					Type = StructureType.HandJointLocationsExt,
					JointCount = Joints,
					JointLocations = joints,
				};

				if ( locate( trackers[hand], &info, &locations ) != XrResult.Success ) return false;

				return locations.IsActive != 0;
			}
		}

		//A hand's joints, free or round the controller, and whether they were there - nought where the hand was not seen, or has no tracker.
		internal static bool Joint( int hand, bool roundController, int joint, out Transform pose )
		{
			pose = Transform.Zero;

			var active = roundController ? grippingActive[hand] : freeActive[hand];
			if ( !active ) return false;

			var location = ( roundController ? gripping : free )[hand][joint];
			if ( ( location.LocationFlags & SpaceLocationFlags.OrientationValidBit ) == 0 ) return false;

			pose = ToTransform( location.Pose );
			return true;
		}

		//Whether a hand is seen, free or round the controller.
		internal static bool Seen( int hand, bool roundController )
			=> roundController ? grippingActive[hand] : freeActive[hand];
	}
}
