namespace Rinten;

public sealed partial class ModelBuilder
{
	private readonly List<PhysicsBodyBuilder> _bodies = [];

	/// <summary>Adds a physics body to the model. Joints refer to bodies by the order they are added.</summary>
	public PhysicsBodyBuilder AddBody( float mass = default, Surface surface = default, string boneName = default )
	{
		var builder = new PhysicsBodyBuilder
		{
			Mass = mass,
			Surface = surface,
			BoneName = boneName,
			BindPose = Transform.Zero
		};

		_bodies.Add( builder );
		return builder;
	}

}
