namespace Rinten.Physics;

//TODO: How is this different from SpringJoint? Should they be merged?
/// <summary>A generic "rope" type constraint.</summary>
public partial class FixedJoint : PhysicsJoint
{
	internal FixedJoint( HandleCreationData _ ) { }

	/// <summary>How springy and tight the joint will be in its movement.</summary>
	public PhysicsSpring SpringLinear
	{
		get => backend.GetLinearSpring();
		set => backend.SetLinearSpring( value );
	}

	/// <summary>How springy and tight the joint will be in its rotation.</summary>
	public PhysicsSpring SpringAngular
	{
		get => backend.GetAngularSpring();
		set => backend.SetAngularSpring( value );
	}
}
