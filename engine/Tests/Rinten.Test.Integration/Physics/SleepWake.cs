namespace PhysicsTests;

//Moving what a sleeping body rests on must wake it; the solver can't notice that on its own
[TestClass]
public class SleepWakeTest
{
	//A floor, and a box dropped on it and left to settle.
	static (PhysicsWorld World, PhysicsBody Ground, PhysicsBody Box) Settled()
	{
		var world = new PhysicsWorld { SleepingEnabled = true };

		var ground = new PhysicsBody( world ) { BodyType = PhysicsBodyType.Static };
		ground.AddBoxShape( Vector3.Zero, Rotation.Identity, new Vector3( 200, 200, 10 ) );

		var box = new PhysicsBody( world ) { BodyType = PhysicsBodyType.Dynamic };
		box.AddBoxShape( Vector3.Zero, Rotation.Identity, new Vector3( 10, 10, 10 ) );
		box.Transform = new Transform( new Vector3( 0, 0, 12 ) );

		//Long enough to land and stop being interesting.
		for ( var i = 0; i < 400; i++ )
			world.Step( 1.0f / 60.0f );

		return (world, ground, box);
	}

	[TestMethod]
	public void ABoxLeftAloneOnTheFloorGoesToSleep()
	{
		var (world, _, box) = Settled();

		Assert.IsTrue( box.Sleeping,
			"a box that has landed and stopped should sleep - the rest of this file means nothing if it does not" );

		world.Delete();
	}

	//The bug this covers: drag the floor away in a running scene and the box stays asleep in mid-air. Not falling slowly - not falling
	//at all, and nothing will ever start it, because the only thing that could have was the very move that took its ground away.
	[TestMethod]
	public void MovingTheGroundWakesWhatWasRestingOnIt()
	{
		var (world, ground, box) = Settled();

		Assert.IsTrue( box.Sleeping, "the box needs to be asleep before the ground moves" );

		var restingAt = box.Transform.Position.z;

		ground.Transform = new Transform( new Vector3( 0, 0, -500 ) );

		Assert.IsFalse( box.Sleeping,
			"moving the ground out from under a sleeping box should have woken it" );

		for ( var i = 0; i < 60; i++ )
			world.Step( 1.0f / 60.0f );

		Assert.IsTrue( box.Transform.Position.z < restingAt - 1.0f,
			$"woken, it should now be falling - it was at {restingAt} and is at {box.Transform.Position.z}" );

		world.Delete();
	}

	//The other half of the same rule: setting a body to the pose it already holds is how
	//a kinematic one is driven every frame, and it must not wake the world each time.
	[TestMethod]
	public void SettingTheSameTransformDoesNotWakeAnything()
	{
		var (world, ground, box) = Settled();

		Assert.IsTrue( box.Sleeping, "the box needs to be asleep first" );

		ground.Transform = ground.Transform;

		Assert.IsTrue( box.Sleeping,
			"a body set to where it already was has not moved, and should have woken nobody" );

		world.Delete();
	}
}
