using Rinten.XR;

namespace Rinten.VR;

internal static unsafe partial class VRSystem
{

	//BooleanAction functions
	private static InputBooleanActionState GetBooleanActionStateInternal( string path, InputSource inputSource )
	{
		return ReadBooleanAction( path, inputSource );
	}
	
	public static InputBooleanActionState GetBooleanActionState( BooleanAction action, InputSource inputSource )
	{
		return GetBooleanActionStateInternal( BooleanActionStrings[ (int)action ], inputSource );
	}

	//Vector2Action functions
	private static InputVector2ActionState GetVector2ActionStateInternal( string path, InputSource inputSource )
	{
		return ReadVector2Action( path, inputSource );
	}
	
	public static InputVector2ActionState GetVector2ActionState( Vector2Action action, InputSource inputSource )
	{
		return GetVector2ActionStateInternal( Vector2ActionStrings[ (int)action ], inputSource );
	}

	//FloatAction functions
	private static InputFloatActionState GetFloatActionStateInternal( string path, InputSource inputSource )
	{
		return ReadFloatAction( path, inputSource );
	}
	
	public static InputFloatActionState GetFloatActionState( FloatAction action, InputSource inputSource )
	{
		return GetFloatActionStateInternal( FloatActionStrings[ (int)action ], inputSource );
	}

	//PoseAction functions
	private static InputPoseActionState GetPoseActionStateInternal( string path, InputSource inputSource )
	{
		return ReadPoseAction( path, inputSource );
	}
	
	public static InputPoseActionState GetPoseActionState( PoseAction action, InputSource inputSource )
	{
		return GetPoseActionStateInternal( PoseActionStrings[ (int)action ], inputSource );
	}
}
