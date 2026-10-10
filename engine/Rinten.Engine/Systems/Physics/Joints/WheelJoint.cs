namespace Rinten.Physics;

/// <summary>The wheel joint can be used to simulate wheels on vehicles. The wheel joint restricts body B to move along a local axis in body A. Body B is free to rotate. Supports a linear spring, linear limits, and a rotational motor.</summary>
internal sealed class WheelJoint : PhysicsJoint
{
	const float TorqueScale = 40.0f;

	internal WheelJoint( HandleCreationData _ ) { }

	/// <summary>Enable or disable the wheel joint spring.</summary>
	public bool EnableSuspension
	{
		get => BackendValid && backend.Wheel_IsSuspensionEnabled();
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_EnableSuspension( value );
		}
	}

	/// <summary>Gets or sets the wheel joint suspension stiffness in Hertz.</summary>
	public float SuspensionHertz
	{
		get => !BackendValid ? 0.0f : backend.Wheel_GetSuspensionHertz();
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_SetSuspensionHertz( value );
		}
	}

	/// <summary>Gets or sets the wheel joint suspension damping ratio (non-dimensional).</summary>
	public float SuspensionDampingRatio
	{
		get => !BackendValid ? 0.0f : backend.Wheel_GetSuspensionDampingRatio();
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_SetSuspensionDampingRatio( value );
		}
	}

	/// <summary>Enable or disable the wheel joint suspension limit.</summary>
	public bool EnableSuspensionLimit
	{
		get => BackendValid && backend.Wheel_IsSuspensionLimitEnabled();
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_EnableSuspensionLimit( value );
		}
	}

	/// <summary>Gets or sets the wheel joint suspension limits.</summary>
	public Vector2 SuspensionLimits
	{
		get => !BackendValid ? default : new( backend.Wheel_GetLowerSuspensionLimit(), backend.Wheel_GetUpperSuspensionLimit() );
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_SetSuspensionLimits( value.x, value.y );
		}
	}

	/// <summary>Enable or disable the wheel joint spin motor.</summary>
	public bool EnableSpinMotor
	{
		get => BackendValid && backend.Wheel_IsSpinMotorEnabled();
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_EnableSpinMotor( value );
		}
	}

	/// <summary>Gets or sets the wheel joint spin motor speed in degrees per second.</summary>
	public float SpinMotorSpeed
	{
		get => !BackendValid ? 0.0f : backend.Wheel_GetSpinMotorSpeed().RadianToDegree();
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_SetSpinMotorSpeed( value.DegreeToRadian() );
		}
	}

	/// <summary>Gets or sets the wheel joint maximum spin motor torque, usually in newton-meters.</summary>
	public float MaxSpinTorque
	{
		get => !BackendValid ? 0.0f : backend.Wheel_GetMaxSpinTorque() / TorqueScale;
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_SetMaxSpinTorque( value * TorqueScale );
		}
	}

	/// <summary>Enable or disable wheel steering. Steering allows the wheel to rotate about the suspension axis.</summary>
	public bool EnableSteering
	{
		get => BackendValid && backend.Wheel_IsSteeringEnabled();
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_EnableSteering( value );
		}
	}

	/// <summary>Gets or sets the wheel joint steering stiffness in Hertz.</summary>
	public float SteeringHertz
	{
		get => !BackendValid ? 0.0f : backend.Wheel_GetSteeringHertz();
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_SetSteeringHertz( value );
		}
	}

	/// <summary>Gets or sets the wheel joint steering damping ratio (non-dimensional).</summary>
	public float SteeringDampingRatio
	{
		get => !BackendValid ? 0.0f : backend.Wheel_GetSteeringDampingRatio();
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_SetSteeringDampingRatio( value );
		}
	}

	/// <summary>Gets or sets the wheel joint maximum steering torque in N·m.</summary>
	public float MaxSteeringTorque
	{
		get => !BackendValid ? 0.0f : backend.Wheel_GetMaxSteeringTorque() / TorqueScale;
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_SetMaxSteeringTorque( value * TorqueScale );
		}
	}

	/// <summary>Enable or disable the wheel joint steering limit.</summary>
	public bool EnableSteeringLimit
	{
		get => BackendValid && backend.Wheel_IsSteeringLimitEnabled();
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_EnableSteeringLimit( value );
		}
	}

	/// <summary>Gets or sets the wheel joint steering limits in degrees.</summary>
	public Vector2 SteeringLimits
	{
		get => !BackendValid ? 0.0f : backend.Wheel_GetLowerSteeringLimit().RadianToDegree();
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_SetSteeringLimits( value.x.DegreeToRadian(), value.y.DegreeToRadian() );
		}
	}

	/// <summary>Gets or sets the wheel joint target steering angle in degrees.</summary>
	public float TargetSteeringAngle
	{
		get => !BackendValid ? 0.0f : backend.Wheel_GetTargetSteeringAngle().RadianToDegree();
		set
		{
			if ( !BackendValid ) return;
			backend.Wheel_SetTargetSteeringAngle( value.DegreeToRadian() );
		}
	}

	/// <summary>Gets the current wheel spin speed in degrees per second.</summary>
	public float SpinSpeed => !BackendValid ? 0.0f : backend.Wheel_GetSpinSpeed().RadianToDegree();

	/// <summary>Gets the current wheel spin torque in newton-meters.</summary>
	public float SpinTorque => !BackendValid ? 0.0f : backend.Wheel_GetSpinTorque();

	/// <summary>Gets the current wheel steering angle in degrees.</summary>
	public float SteeringAngle => !BackendValid ? 0.0f : backend.Wheel_GetSteeringAngle().RadianToDegree();

	/// <summary>Gets the current wheel steering torque in newton-meters.</summary>
	public float SteeringTorque => !BackendValid ? 0.0f : backend.Wheel_GetSteeringTorque();
}
