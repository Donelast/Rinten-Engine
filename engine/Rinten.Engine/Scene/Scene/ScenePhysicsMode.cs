namespace Rinten;

/// <summary>Which physics simulation a <see cref="Scene"/> runs. For use with <see cref="Scene.PhysicsMode"/>.</summary>
[Expose]
public enum ScenePhysicsMode
{
	/// <summary>Full 3D rigid body physics. This is the default.</summary>
	[Title( "3D" )]
	Physics3D,

	/// <summary>2D physics, simulated by Box2D in the world's XY plane, seen side-on: X is right, Y is up, bodies turn about Z, and depth is kept but not simulated. The same colliders, rigidbodies and joints work, laid on the plane.</summary>
	[Title( "2D" )]
	Physics2D
}
