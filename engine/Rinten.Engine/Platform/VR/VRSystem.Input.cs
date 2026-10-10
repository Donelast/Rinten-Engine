using Rinten.XR;

namespace Rinten.VR;

internal static unsafe partial class VRSystem
{
	private static readonly string[] BooleanActionStrings =
	{
		"/actions/default/in/joystick_button",
		"/actions/default/in/button_a",
		"/actions/default/in/button_b"
	};

	public enum BooleanAction
	{
		JoystickPress,
		ButtonA,
		ButtonB
	};

	private static readonly string[] FloatActionStrings =
	{
		"/actions/default/in/grip",
		"/actions/default/in/trigger",
	};

	public enum FloatAction
	{
		Grip,
		Trigger
	};

	private static readonly string[] Vector2ActionStrings =
	{
		"/actions/default/in/joystick",
	};

	public enum Vector2Action
	{
		Joystick
	};

	private static readonly string[] PoseActionStrings =
	{
		"/actions/default/in/hand_pose",
		"/actions/default/in/aim_pose",
	};

	public enum PoseAction
	{
		/// <summary>The grip pose - centered on the palm/grip of the controller</summary>
		GripPose,

		/// <summary>The aim pose - pointing forward, suitable for aiming/pointing</summary>
		AimPose
	};

	private static readonly string[] HapticActionStrings =
	{
		"/actions/default/out/vibrate_left",
		"/actions/default/out/vibrate_right",
	};

	public enum HapticAction
	{
		LeftHandHaptics,
		RightHandHaptics
	};

	internal enum TrackedControllerType
	{
		Unknown,
		HTCVive,
		HTCViveFocus3,
		HTCViveCosmos,
		MetaTouch,
		IndexKnuckles,
		WindowsMixedReality,
		HPReverbG2,
		Generic
	}

	private static readonly Dictionary<string, TrackedControllerType> ControllerStringToTypeMap = new()
	{
		{ "unknown", TrackedControllerType.Unknown },
		{ "vive_controller", TrackedControllerType.HTCVive },
		{ "vive_focus3_controller", TrackedControllerType.HTCViveFocus3 },
		{ "vive_cosmos_controller", TrackedControllerType.HTCViveCosmos },
		{ "oculus_touch", TrackedControllerType.MetaTouch },
		{ "knuckles", TrackedControllerType.IndexKnuckles },
		{ "holographic_controller", TrackedControllerType.WindowsMixedReality },
		{ "hpmotioncontroller", TrackedControllerType.HPReverbG2 },
		{ "generic_tracked", TrackedControllerType.Generic },
		{ "pico4_controller", TrackedControllerType.Generic },
	};

	internal static TrackedControllerType ControllerTypeFromString( string str )
	{
		if ( str == null )
			return TrackedControllerType.Unknown;

		if ( ControllerStringToTypeMap.TryGetValue( str, out var type ) )
			return type;

		return TrackedControllerType.Unknown;
	}

	internal enum HMDType
	{
		Unknown,
		HTC,
		Index,
		Oculus,
		Pico,
		HP,
		WindowsMixedReality,
		Bigscreen,
		Pimax,
	}

	private static readonly Dictionary<string, HMDType> HMDStringToTypeMap = new()
	{
		{ "unknown", HMDType.Unknown },
		{ "HTC", HMDType.HTC },
		//The string the runtime reports for the Index, which is a manufacturer's name and
		//not ours to rename: changing it here would mean the headset stops being recognised.
		{ "Valve", HMDType.Index },
		{ "Oculus", HMDType.Oculus },
		{ "Meta", HMDType.Oculus },
		{ "Pico", HMDType.Pico },
		{ "HP", HMDType.HP },
		{ "WindowsMR", HMDType.WindowsMixedReality },
		{ "Bigscreen", HMDType.Bigscreen },
		{ "Pimax", HMDType.Pimax }
	};

	internal static HMDType GetHMDType()
	{
		var identifier = GetSystemName();

		if ( identifier == null )
			return HMDType.Unknown;

		if ( HMDStringToTypeMap.TryGetValue( identifier, out var type ) )
			return type;

		return HMDType.Unknown;
	}

	internal static Transform GetOffsetForDeviceRole( TrackedDeviceRole role )
	{
		return role switch
		{
			TrackedDeviceRole.LeftHand => new Transform( new Vector3( 5f, -2f, -3f ), Rotation.From( 10, -10, 90 ) ),
			TrackedDeviceRole.RightHand => new Transform( new Vector3( 5f, 2f, -3f ), Rotation.From( 10, 10, -90 ) ),
			_ => Transform.Zero
		};
	}

	internal static TrackedDeviceRole GetTrackedDeviceRoleForInputSource( InputSource source )
	{
		return source switch
		{
			InputSource.LeftHand => TrackedDeviceRole.LeftHand,
			InputSource.RightHand => TrackedDeviceRole.RightHand,
			InputSource.Head => TrackedDeviceRole.Head,
			_ => TrackedDeviceRole.Unknown,
		};
	}

	internal static void TriggerHapticVibration( float duration, float frequency, float amplitude, InputSource source )
	{
		var hand = HandOf( source );
		if ( hand < 0 ) return;

		CoreHeadset.Input.Buzz( hand, duration, frequency, amplitude );
	}

	//Which of the runtime's two hands an input source is, or none.
	private static int HandOf( InputSource source ) => source switch
	{
		InputSource.LeftHand => 0,
		InputSource.RightHand => 1,
		_ => -1,
	};

	//The action an action path names - see the action strings above and CoreHeadset.Input.
	private static XrInput? KindOf( string path ) => path switch
	{
		"/actions/default/in/joystick_button" => XrInput.JoystickPress,
		"/actions/default/in/button_a" => XrInput.ButtonA,
		"/actions/default/in/button_b" => XrInput.ButtonB,
		"/actions/default/in/grip" => XrInput.Grip,
		"/actions/default/in/trigger" => XrInput.Trigger,
		"/actions/default/in/joystick" => XrInput.Joystick,
		"/actions/default/in/hand_pose" => XrInput.GripPose,
		"/actions/default/in/aim_pose" => XrInput.AimPose,
		_ => null,
	};

	private static InputBooleanActionState ReadBooleanAction( string path, InputSource source )
	{
		if ( KindOf( path ) is not { } kind || HandOf( source ) is var hand && hand < 0 ) return default;

		var (active, down, changed) = CoreHeadset.Input.Boolean( kind, hand );
		return new InputBooleanActionState { isActive = active, state = down ? 1u : 0u, changedThisFrame = changed };
	}

	private static InputFloatActionState ReadFloatAction( string path, InputSource source )
	{
		if ( KindOf( path ) is not { } kind || HandOf( source ) is var hand && hand < 0 ) return default;

		var (active, value, changed) = CoreHeadset.Input.Float( kind, hand );
		return new InputFloatActionState { isActive = active, value = value, changedThisFrame = changed };
	}

	private static InputVector2ActionState ReadVector2Action( string path, InputSource source )
	{
		if ( KindOf( path ) is not { } kind || HandOf( source ) is var hand && hand < 0 ) return default;

		var (active, value, changed) = CoreHeadset.Input.Vector( kind, hand );
		return new InputVector2ActionState { isActive = active, x = value.x, y = value.y, changedThisFrame = changed };
	}

	private static InputPoseActionState ReadPoseAction( string path, InputSource source )
	{
		if ( KindOf( path ) is not { } kind || HandOf( source ) is var hand && hand < 0 ) return default;

		var (active, pose) = CoreHeadset.Input.Pose( kind, hand );
		return new InputPoseActionState { isActive = active, pose = TrackedDevicePose.FromTransform( pose ) };
	}

	//Hand tracking level only, without reading joints; cheap check most frames need
	internal static HandPoseLevel GetHandPoseLevel( InputSource source, MotionRange motionRange )
	{
		var hand = HandOf( source );
		if ( hand < 0 ) return HandPoseLevel.Unknown;

		var roundController = motionRange == MotionRange.Controller;
		if ( !CoreHeadset.Hands.Seen( hand, roundController ) ) return HandPoseLevel.Unknown;

		//Held round a controller where a controller is being held; otherwise the hand itself, as the cameras or the finger sensors see it.
		var holding = CoreHeadset.Input.Pose( XrInput.GripPose, hand ).Active;

		return roundController && holding ? HandPoseLevel.Controller : HandPoseLevel.FullyTracked;
	}

	//A hand's 26 joints in play-area space and how they were tracked; read only on demand
	internal static InputPoseHandState GetHandPoseState( InputSource source, MotionRange motionRange )
	{
		var state = default( InputPoseHandState );

		var hand = HandOf( source );
		if ( hand < 0 ) return state;

		var roundController = motionRange == MotionRange.Controller;

		state.handPoseLevel = GetHandPoseLevel( source, motionRange );
		if ( state.handPoseLevel == HandPoseLevel.Unknown ) return state;

		//The joint fields lie one after another in OpenXR's order - see the struct.
		ref var first = ref state.joint0;

		for ( var joint = 0; joint < Constants.HandJointCount; joint++ )
		{
			if ( CoreHeadset.Hands.Joint( hand, roundController, joint, out var pose ) )
				System.Runtime.CompilerServices.Unsafe.Add( ref first, joint ).pose = TrackedDevicePose.FromTransform( pose );
		}

		return state;
	}

	//Per-finger constants for curl calculation. minDistance/maxDistance define the range of tip-to-palm distances.
	private readonly struct FingerCurlConstants
	{
		public readonly float MinDistance;
		public readonly float MaxDistance;
		public readonly VRHandJoint TipJoint;

		public FingerCurlConstants( float minDistance, float maxDistance, VRHandJoint tipJoint )
		{
			MinDistance = minDistance;
			MaxDistance = maxDistance;
			TipJoint = tipJoint;
		}
	}

	private static readonly FingerCurlConstants[] FingerConstants = new FingerCurlConstants[]
	{
		new( 0.10922f, 0.1016f, VRHandJoint.ThumbTip ),   //Thumb
		new( 0.14478f, 0.0508f, VRHandJoint.IndexTip ),   //Index
		new( 0.14478f, 0.0508f, VRHandJoint.MiddleTip ),  //Middle
		new( 0.14478f, 0.0508f, VRHandJoint.RingTip ),    //Ring
		new( 0.11303f, 0.0889f, VRHandJoint.LittleTip ),  //Pinky
	};

	internal static float GetFingerCurl( InputSource source, FingerValue finger )
	{
		if ( source != InputSource.LeftHand && source != InputSource.RightHand )
			return 0.0f;

		int fingerIndex = (int)finger;
		if ( fingerIndex < 0 || fingerIndex >= FingerConstants.Length )
			return 0.0f;

		var constants = FingerConstants[fingerIndex];
		//The hand itself where it is seen, and round the controller where it is holding one - a Quest follows one or the other, never both.
		var handState = GetHandPoseState( source, MotionRange.Hand );

		if ( handState.handPoseLevel == HandPoseLevel.Unknown )
			handState = GetHandPoseState( source, MotionRange.Controller );

		if ( handState.handPoseLevel == HandPoseLevel.Unknown )
			return 0.0f;

		var tipPose = handState[(int)constants.TipJoint].pose;
		var palmPose = handState[(int)VRHandJoint.Palm].pose;

		float dx = tipPose.posx - palmPose.posx;
		float dy = tipPose.posy - palmPose.posy;
		float dz = tipPose.posz - palmPose.posz;
		float distance = MathF.Sqrt( dx * dx + dy * dy + dz * dz );

		//Remap distance to 0-1 curl value
		float curl = (distance - constants.MinDistance) / (constants.MaxDistance - constants.MinDistance);
		return curl;
	}

	//Whether the headset follows the hands joint by joint - see CoreHeadset.Hands.
	internal static bool HandTrackingSupported() => CoreHeadset.Hands.Available;
}
