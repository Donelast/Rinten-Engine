namespace Rinten.VR;

public class VRInput
{
	/// <summary>The current relevant <see cref="VRInput"/> instance.</summary>
	public static VRInput Current { get; internal set; }

	private float _scale = 1.0f;

	/// <summary>Get or set the player's scale in the world. If you set it to 2 the player will be twice as big.</summary>
	public float Scale
	{
		get => _scale;
		set
		{
			if ( value == _scale ) return;
			if ( value == 0 ) return;

			_scale = value;
			VRSystem.WorldScale = 1.0f / value;
		}
	}

	/// <summary>Gets or sets where the center of the VR play area is in world space.</summary>
	public Transform Anchor { get; set; } = Transform.Zero;

	/// <summary>Returns true if SteamVR is drawing the controllers</summary>
	[Obsolete]
	public bool ControllersAreDrawing => false;

	/// <summary>Returns true if the left hand is dominant</summary>
	[Obsolete]
	public bool IsLeftHandDominant => false;

	internal Transform _head = Transform.Zero;

	/// <summary>Position and rotation of the Head Mounted Display in local space coordinates.</summary>
	public Transform Head => Input.VR.Anchor.ToWorld( _head );

	/// <summary>Whether the headset follows the player's eyes - a Steam Frame, a Quest Pro, a Pico 4 Pro. Where it does, <see cref="Gaze"/> says where they look.</summary>
	public bool HasEyeTracking => CoreHeadset.Gaze.Available;

	/// <summary>Whether <see cref="Gaze"/> is where the eyes are looking this frame. False while the tracker has lost them, or the player has not allowed it.</summary>
	public bool IsGazeTracked => CoreHeadset.Gaze.Tracked;

	/// <summary>Where the player is looking, in world space: from between the eyes, along Forward. The head's own pose where the eyes are not followed.</summary>
	public Transform Gaze
	{
		get
		{
			if ( !CoreHeadset.Gaze.Tracked ) return Head;

			var local = CoreHeadset.Gaze.Pose;
			var scale = 1.0f / MathF.Max( VRSystem.WorldScale, 0.0001f );

			return Anchor.ToWorld( local.WithPosition( local.Position * scale ) );
		}
	}

	/// <summary>Information about the left hand input.</summary>
	public VRController LeftHand { get; internal set; }

	/// <summary>Information about the right hand input.</summary>
	public VRController RightHand { get; internal set; }

	internal List<TrackedObject> _objectList;

	/// <summary>A list of available trackers.</summary>
	public IReadOnlyList<TrackedObject> TrackedObjects => _objectList;

	internal VRInput( TrackedDevice leftHand, TrackedDevice rightHand )
	{
		LeftHand = new( leftHand );
		RightHand = new( rightHand );
	}

	internal void Update()
	{
		//Update controllers
		LeftHand.Update();
		RightHand.Update();
	}
}
