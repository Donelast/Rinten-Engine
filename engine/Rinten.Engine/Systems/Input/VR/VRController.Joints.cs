using Rinten.XR;

namespace Rinten.VR;

partial record VRController
{
	//One hand's joints for a motion range, read lazily on first request rather than every frame
	class JointDataContainer
	{
		private InputSource source;
		private MotionRange range;
		private InputPoseHandState pose;
		private bool read;

		//Whether _jointData holds this frame's joints yet.
		private bool filled;

		/// <summary>How the hand is being had. Known without reading a joint.</summary>
		public HandPoseLevel Level { get; private set; }

		private readonly VRHandJointData[] _jointData = new VRHandJointData[Enum.GetValues<VRHandJoint>().Length];

		/// <summary>Said once a frame: what to ask, and that what was read is stale.</summary>
		public void Renew( InputSource inputSource, MotionRange motionRange )
		{
			source = inputSource;
			range = motionRange;
			Level = VRSystem.GetHandPoseLevel( inputSource, motionRange );
			read = false;
			filled = false;
		}

		private ref readonly InputPoseHandState Pose
		{
			get
			{
				if ( !read )
				{
					read = true;
					pose = VRSystem.GetHandPoseState( source, range );
				}

				return ref pose;
			}
		}

		internal Transform GetBoneTransform( VRHandJoint joint )
		{
			var index = (int)joint;
			return Pose[index].pose.GetTransform();
		}

		/// <summary>This frame's joints, filled on the first ask and handed out as they are after. A game asks for a hand's joints a dozen times a frame - what it holds, whether the fist is shut, where the pinch is - and each of those turned all twenty-six poses into transforms again for the same answer.</summary>
		public VRHandJointData[] GetJoints()
		{
			if ( !filled )
			{
				filled = true;
				UpdateJointData();
			}

			return _jointData;
		}

		private void UpdateJointData()
		{
			ref readonly var held = ref Pose;

			for ( int i = 0; i < _jointData.Length; i++ )
			{
				ref var jointData = ref _jointData[i];

				jointData.Joint = (VRHandJoint)i;
				jointData.Transform = held[i].pose.GetTransform();
			}
		}
	}

	private JointDataContainer _handJoints = new();
	private JointDataContainer _conformingJoints = new();

	internal Transform GetBoneTransform( VRHandJoint joint, MotionRange motionRange )
	{
		if ( motionRange == MotionRange.Hand )
			return _handJoints.GetBoneTransform( joint );
		else if ( motionRange == MotionRange.Controller )
			return _conformingJoints.GetBoneTransform( joint );

		return Transform.Zero;
	}

	/// <summary>Returns joint data for a specific motion range.</summary>
	public VRHandJointData[] GetJoints( MotionRange motionRange = MotionRange.Hand )
	{
		if ( motionRange == MotionRange.Hand )
			return _handJoints.GetJoints();
		else if ( motionRange == MotionRange.Controller )
			return _conformingJoints.GetJoints();

		return Array.Empty<VRHandJointData>();
	}

	/// <summary>Get the skeletal value (from 0 to 1) of a specified <see cref="FingerValue"/> - includes curl and splay.</summary>
	public float GetFingerValue( FingerValue value )
	{
		bool isCurl = value == FingerValue.ThumbCurl || value == FingerValue.IndexCurl || value == FingerValue.MiddleCurl || value == FingerValue.RingCurl || value == FingerValue.PinkyCurl;

		if ( isCurl )
		{
			return VRSystem.GetFingerCurl( _trackedDevice.InputSource, value );
		}

		return 0;
	}

	/// <summary>Get the skeletal value (from 0 to 1) of a specified finger curl index.</summary>
	public float GetFingerCurl( int index )
	{
		if ( index < 0 ) throw new ArgumentOutOfRangeException( "Should be 0-4", nameof( index ) );
		if ( index > 4 ) throw new ArgumentOutOfRangeException( "Should be 0-4", nameof( index ) );
		return GetFingerValue( (FingerValue)index );
	}

	/// <summary>Get the skeletal value (from 0 to 1) of a specified finger splay index.</summary>
	public float GetFingerSplay( int index )
	{
		if ( index < 0 ) throw new ArgumentOutOfRangeException( "Should be 0-3", nameof( index ) );
		if ( index > 3 ) throw new ArgumentOutOfRangeException( "Should be 0-3", nameof( index ) );
		return GetFingerValue( (FingerValue.ThumbIndexSplay + index) );
	}

	private readonly List<VRHandJointData> _jointList = new();

	[Obsolete( "Please use GetJoints()" )]
	public List<VRHandJointData> GetJointData()
	{
		_jointList.Clear();
		_jointList.AddRange( GetJoints( MotionRange.Hand ) );
		return _jointList;
	}
}
