namespace Rinten;

/// <inheritdoc cref="Physics.ControlJoint"/>
[Expose]
[Title( "Control Joint" )]
[Category( "Physics" )]
[Icon( "joystick" )]
[EditorHandle( "materials/gizmo/tracked_object.png" )]
public sealed class ControlJoint : Joint
{
	/// <inheritdoc cref="Physics.ControlJoint.LinearVelocity"/>
	[Property]
	public Vector3 LinearVelocity
	{
		get;
		set
		{
			if ( value == field )
				return;

			field = value;

			if ( joint.IsValid() )
			{
				joint.LinearVelocity = field;
				joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.ControlJoint.AngularVelocity"/>
	[Property]
	public Vector3 AngularVelocity
	{
		get;
		set
		{
			if ( value == field )
				return;

			field = value;

			if ( joint.IsValid() )
			{
				joint.AngularVelocity = field;
				joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.ControlJoint.MaxVelocityForce"/>
	[Property]
	public float MaxVelocityForce
	{
		get;
		set
		{
			if ( value == field )
				return;

			field = value;

			if ( joint.IsValid() )
			{
				joint.MaxVelocityForce = field;
				joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.ControlJoint.MaxVelocityTorque"/>
	[Property]
	public float MaxVelocityTorque
	{
		get;
		set
		{
			if ( value == field )
				return;

			field = value;

			if ( joint.IsValid() )
			{
				joint.MaxVelocityTorque = field;
				joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.ControlJoint.LinearSpring"/>
	[Property]
	public PhysicsSpring LinearSpring
	{
		get;
		set
		{
			if ( value == field )
				return;

			field = value;

			if ( joint.IsValid() )
			{
				joint.LinearSpring = field;
				joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.ControlJoint.AngularSpring"/>
	[Property]
	public PhysicsSpring AngularSpring
	{
		get;
		set
		{
			if ( value == field )
				return;

			field = value;

			if ( joint.IsValid() )
			{
				joint.AngularSpring = field;
				joint.WakeBodies();
			}
		}
	}

	private Physics.ControlJoint joint;

	protected override PhysicsJoint CreateJoint( PhysicsPoint point1, PhysicsPoint point2 )
	{
		var (localFrame1, localFrame2) = FramesFor( point1.LocalTransform, point2.LocalTransform );

		point1.LocalTransform = localFrame1;
		point2.LocalTransform = localFrame2;

		joint = PhysicsJoint.CreateControl( point1, point2 );

		UpdateProperties();

		return joint;
	}

	private void UpdateProperties()
	{
		if ( !joint.IsValid() )
			return;

		joint.LinearVelocity = LinearVelocity;
		joint.AngularVelocity = AngularVelocity;
		joint.MaxVelocityForce = MaxVelocityForce;
		joint.MaxVelocityTorque = MaxVelocityTorque;
		joint.LinearSpring = LinearSpring;
		joint.AngularSpring = AngularSpring;

		joint.WakeBodies();
	}
}
