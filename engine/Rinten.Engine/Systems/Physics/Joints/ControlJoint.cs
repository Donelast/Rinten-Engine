namespace Rinten.Physics;

/// <summary>Drives a body with a spring and/or velocity motor while it still reacts to collisions.</summary>
public partial class ControlJoint : PhysicsJoint
{
	internal ControlJoint( HandleCreationData _ ) { }

	/// <summary>The desired relative linear velocity.</summary>
	public Vector3 LinearVelocity
	{
		get => !BackendValid ? default : backend.Motor_GetLinearVelocity();
		set
		{
			if ( !BackendValid ) return;
			backend.Motor_SetLinearVelocity( value );
		}
	}

	/// <summary>The desired relative angular velocity in radians per second.</summary>
	public Vector3 AngularVelocity
	{
		get => !BackendValid ? default : backend.Motor_GetAngularVelocity();
		set
		{
			if ( !BackendValid ) return;
			backend.Motor_SetAngularVelocity( value );
		}
	}

	/// <summary>The joint maximum force.</summary>
	public float MaxVelocityForce
	{
		get => !BackendValid ? 0.0f : backend.Motor_GetMaxVelocityForce();
		set
		{
			if ( !BackendValid ) return;
			backend.Motor_SetMaxVelocityForce( value );
		}
	}

	/// <summary>The joint maximum torque.</summary>
	public float MaxVelocityTorque
	{
		get => !BackendValid ? 0.0f : backend.Motor_GetMaxVelocityTorque();
		set
		{
			if ( !BackendValid ) return;
			backend.Motor_SetMaxVelocityTorque( value );
		}
	}

	/// <summary>The spring linear hertz stiffness and damping ratio.</summary>
	public PhysicsSpring LinearSpring
	{
		get
		{
			if ( !BackendValid ) return default;
			return new PhysicsSpring
			{
				Frequency = backend.Motor_GetLinearHertz(),
				Damping = backend.Motor_GetLinearDampingRatio(),
				Maximum = backend.Motor_GetMaxSpringForce()
			};
		}
		set
		{
			if ( !BackendValid ) return;
			backend.Motor_SetLinearHertz( value.Frequency );
			backend.Motor_SetLinearDampingRatio( value.Damping );
			backend.Motor_SetMaxSpringForce( value.Maximum );
		}
	}

	/// <summary>The spring angular hertz stiffness and damping ratio.</summary>
	public PhysicsSpring AngularSpring
	{
		get
		{
			if ( !BackendValid ) return default;
			return new PhysicsSpring
			{
				Frequency = backend.Motor_GetAngularHertz(),
				Damping = backend.Motor_GetAngularDampingRatio(),
				Maximum = backend.Motor_GetMaxSpringTorque()
			};
		}
		set
		{
			if ( !BackendValid ) return;
			backend.Motor_SetAngularHertz( value.Frequency );
			backend.Motor_SetAngularDampingRatio( value.Damping );
			backend.Motor_SetMaxSpringTorque( value.Maximum );
		}
	}
}
