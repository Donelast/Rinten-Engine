namespace Rinten.Physics;

/// <summary>A parallel joint that constrains the local +Z axes of two joint frames to be parallel using a spring. Useful for keeping a physics body upright relative to another body or a static anchor - see the component, which aims those frames at world up.</summary>
public partial class UprightJoint : PhysicsJoint
{
	internal UprightJoint( HandleCreationData _ ) { }

	/// <summary>The spring stiffness in cycles per second (Hertz). Higher values make the constraint stiffer.</summary>
	public float Hertz
	{
		get => backend.Parallel_GetHertz();
		set => backend.Parallel_SetHertz( value );
	}

	/// <summary>The spring damping ratio (non-dimensional). A value of 1 is critically damped; values below 1 are under-damped (springy).</summary>
	public float DampingRatio
	{
		get => backend.Parallel_GetDampingRatio();
		set => backend.Parallel_SetDampingRatio( value );
	}

	/// <summary>The maximum torque the joint can apply.</summary>
	public float MaxTorque
	{
		get => backend.Parallel_GetMaxTorque();
		set => backend.Parallel_SetMaxTorque( value );
	}
}
