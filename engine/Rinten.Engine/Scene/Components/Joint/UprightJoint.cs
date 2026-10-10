namespace Rinten;

/// <summary>Constrains a physics body to stay upright relative to another body or the world. Uses a spring to keep the local +Z axes of both joint frames parallel; those frames are aimed at world up, so the bodies stay the right way up.</summary>
[Expose]
[Title( "Upright Joint" )]
[Category( "Physics" )]
[Icon( "vertical_align_center" )]
[EditorHandle( "materials/gizmo/spring.png" )]
public sealed class UprightJoint : Joint
{
	/// <summary>Spring stiffness in cycles per second (Hertz). Higher values make the joint stiffer and snap back faster.</summary>
	[Property]
	public float Hertz
	{
		get;
		set
		{
			if ( value == field )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.Hertz = value;
				_joint.WakeBodies();
			}
		}
	} = 2.0f;

	/// <summary>Spring damping ratio (non-dimensional). A value of 1 is critically damped; values below 1 are springy, values above 1 are over-damped.</summary>
	[Property]
	public float DampingRatio
	{
		get;
		set
		{
			if ( value == field )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.DampingRatio = value;
				_joint.WakeBodies();
			}
		}
	} = 0.7f;

	/// <summary>Maximum torque the joint can apply in newton-meters. Set to 0 for unlimited torque.</summary>
	[Property]
	public float MaxTorque
	{
		get;
		set
		{
			if ( value == field )
				return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.MaxTorque = value;
				_joint.WakeBodies();
			}
		}
	} = 0.0f;

	Physics.UprightJoint _joint;

	protected override PhysicsJoint CreateJoint( PhysicsPoint point1, PhysicsPoint point2 )
	{
		//Box3D's parallel joint holds the two frames' local +Z axes together, so an upright joint wants that axis on world up. Conjugating
		//the body's rotation puts the frame at identity in the world; looking down then turns its +Z onto +Y, which is where up is.
		var upright = Rotation.LookAt( Vector3.Down, Vector3.Forward );

		var (localFrame1, localFrame2) = FramesFor(
			new Transform( point1.LocalPosition, point1.Body.Rotation.Conjugate * upright ),
			new Transform( point2.LocalPosition, point2.Body.Rotation.Conjugate * upright ) );

		point1 = new PhysicsPoint( point1.Body, localFrame1.Position, localFrame1.Rotation );
		point2 = new PhysicsPoint( point2.Body, localFrame2.Position, localFrame2.Rotation );

		_joint = PhysicsJoint.CreateUpright( point1, point2 );
		_joint.Hertz = Hertz;
		_joint.DampingRatio = DampingRatio;
		_joint.MaxTorque = MaxTorque;

		_joint.WakeBodies();

		return _joint;
	}
}
