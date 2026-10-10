namespace Rinten.Physics;

/// <summary>A slider constraint, basically allows movement only on the arbitrary axis between the 2 constrained objects on creation.</summary>
public partial class SliderJoint : PhysicsJoint
{
	internal SliderJoint( HandleCreationData _ ) { }

	/// <summary>Maximum length it should be allowed to go</summary>
	public float MaxLength
	{
		get => backend.GetMaxLength();
		set => backend.SetMaxLength( value );
	}

	/// <summary>Minimum length it should be allowed to go</summary>
	public float MinLength
	{
		get => backend.GetMinLength();
		set => backend.SetMinLength( value );
	}

	/// <summary>Slider friction.</summary>
	public float Friction
	{
		set => backend.SetFriction( value );
	}
}
