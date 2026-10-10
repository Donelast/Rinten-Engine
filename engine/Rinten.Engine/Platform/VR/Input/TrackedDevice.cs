using Rinten.XR;

namespace Rinten.VR;

//Describes a tracked VR device
internal record TrackedDevice
{
	/// <summary>This device's grip pose transform in absolute space (centered on palm/grip)</summary>
	public Transform Transform;

	/// <summary>This device's aim pose transform in absolute space (pointing forward)</summary>
	public Transform AimTransform;

	/// <summary>Velocity in tracker space, in units per second - metres, the same as OpenXR reports.</summary>
	public Vector3 Velocity;

	/// <summary>Angular velocity in degrees/s</summary>
	public Angles AngularVelocity;

	/// <summary>Where is this device (left hand, right hand, left ankle, chest, etc.)?</summary>
	public TrackedDeviceRole DeviceRole;

	/// <summary>What type of device is this (HMD, controller, tracker, etc.)?</summary>
	public TrackedDeviceType DeviceType;

	//The input source for this device
	internal InputSource InputSource;

	//Handle we should use internally for performance-sensitive calls
	internal ulong InputSourceHandle;

	//Is this tracked device currently active (connected)?
	internal bool IsActive;

	//Index we can use when referring to poses retrieved through WaitGetPose and similar functions
	internal uint DeviceIndex;

	public TrackedDevice( InputSource inputSource )
	{
		InputSource = inputSource;

		DeviceRole = VRSystem.GetTrackedDeviceRoleForInputSource( InputSource );
	}

	/// <summary>Update this tracked device's position, velocity, etc.</summary>
	public virtual void Update()
	{
		var gripPoseState = VRSystem.GetPoseActionState( VRSystem.PoseAction.GripPose, InputSource );
		var aimPoseState = VRSystem.GetPoseActionState( VRSystem.PoseAction.AimPose, InputSource );

		Transform = gripPoseState.pose.GetTransform();
		AimTransform = aimPoseState.pose.GetTransform();
		IsActive = gripPoseState.isActive;

		DeviceType = GetDeviceType();
	}

	private TrackedDeviceType GetDeviceType()
	{
		return InputSource switch
		{
			InputSource.Unknown => TrackedDeviceType.Invalid,
			InputSource.Head => TrackedDeviceType.Hmd,
			InputSource.LeftHand => TrackedDeviceType.Controller,
			InputSource.RightHand => TrackedDeviceType.Controller,
			_ => TrackedDeviceType.Invalid,
		};
	}

	public TrackedObject GetTrackedObject()
	{
		return new TrackedObject( this );
	}
}
