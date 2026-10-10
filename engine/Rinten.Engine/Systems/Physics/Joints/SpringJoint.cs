namespace Rinten.Physics;

/// <summary>A rope-like constraint that is has springy/bouncy.</summary>
public partial class SpringJoint : PhysicsJoint
{
	internal SpringJoint( HandleCreationData _ ) { }

	/// <summary>How springy and tight the joint will be</summary>
	public PhysicsSpring SpringLinear
	{
		get => backend.GetLinearSpring();
		set => backend.SetLinearSpring( value );
	}

	/// <summary>Maximum length it should be allowed to go</summary>
	public float MaxLength
	{
		get => backend.GetMaxLength();
		set => backend.SetMaxLength( value );
	}

	/// <summary>Minimum length it should be allowed to go. At which point it acts a bit like a rod.</summary>
	public float MinLength
	{
		get => backend.GetMinLength();
		set => backend.SetMinLength( value );
	}

	/// <summary>Maximum force it should be allowed to go. Set to zero to only allow stretching.</summary>
	public float MaxForce
	{
		get => backend.GetMaxForce();
		set => backend.SetMaxForce( value );
	}

	/// <summary>Minimum force it should be allowed to go.</summary>
	public float MinForce
	{
		get => backend.GetMinForce();
		set => backend.SetMinForce( value );
	}

	[Obsolete( "doesn't exist, not used" )]
	public float ReferenceMass
	{
		get => default;
		set { }
	}
}
