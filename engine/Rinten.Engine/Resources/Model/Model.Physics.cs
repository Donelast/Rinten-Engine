namespace Rinten;

public sealed partial class Model : Resource
{
	PhysicsGroupDescription _physics;

	public PhysicsGroupDescription Physics
	{
		get
		{
			if ( _physics is not null ) return _physics;
			if ( core is not { IsValid: true } ) return null;

			_physics = CoreModelPhysics.Describe( core.Loaded, ResourcePath ?? Name );
			return _physics;
		}
	}
}

