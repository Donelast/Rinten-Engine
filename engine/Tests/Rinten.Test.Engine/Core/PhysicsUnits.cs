namespace EngineTests;

//Physics units are metres on both sides of the ABI; a conversion creeping back fails by magnitudes
[TestClass]
[DoNotParallelize]
public class PhysicsUnitsTest
{
	//A cubic metre of water weighs a tonne. Density is a mass over a length cubed, so it is the quantity a unit change hits hardest and the
	//one most likely to be left behind: at a hundred units to the metre this answer is out by a million, and at 39.37 by sixty thousand.
	[TestMethod]
	public void CubicMetreOfWaterWeighsATonne()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var world = CorePhysics.WorldCreate();
		Assert.AreNotEqual( 0ul, world, "could not create a world" );

		try
		{
			var body = CorePhysics.BodyCreate( world, CorePhysics.MotionType.Dynamic,
				Vector3.Zero, Rotation.Identity );

			Assert.AreNotEqual( 0ul, body, "could not create a body" );

			//Half extents, so this is a metre to a side.
			CorePhysics.BodyAddBox( body, Vector3.Zero, Rotation.Identity,
				new Vector3( 0.5f, 0.5f, 0.5f ), 1000.0f, false );

			Assert.AreEqual( 1000.0f, CorePhysics.BodyGetMass( body ), 1.0f,
				"a cubic metre at the density of water should weigh a tonne" );
		}
		finally
		{
			CorePhysics.WorldDestroy( world );
		}
	}

	//A dropped ball rests one radius up (length check) after the expected fall time (gravity check)
	[TestMethod]
	public void BallRestsOnTheFloorAfterFallingForAsLongAsItShould()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		const float radius = 0.25f;
		const float dropHeight = 5.0f;
		const float gravity = 9.81f;
		const float tick = 1.0f / 60.0f;

		var world = CorePhysics.WorldCreate();
		Assert.AreNotEqual( 0ul, world, "could not create a world" );

		try
		{
			CorePhysics.WorldSetGravity( world, new Vector3( 0, 0, -gravity ) );

			//Top face at zero, so the answer needs no arithmetic: the ball rests at its own radius.
			var floor = CorePhysics.BodyCreate( world, CorePhysics.MotionType.Static,
				new Vector3( 0, 0, -0.5f ), Rotation.Identity );
			CorePhysics.BodyAddBox( floor, Vector3.Zero, Rotation.Identity,
				new Vector3( 10, 10, 0.5f ), 1000.0f, false );

			var ball = CorePhysics.BodyCreate( world, CorePhysics.MotionType.Dynamic,
				new Vector3( 0, 0, dropHeight ), Rotation.Identity );
			CorePhysics.BodyAddSphere( ball, Vector3.Zero, radius, 1000.0f, false );

			//How long a body falls the distance between where it started and where it ends up, from rest: t = sqrt( 2s / g ).
			var distance = dropHeight - radius;
			var expectedTicks = (int)( System.MathF.Sqrt( 2.0f * distance / gravity ) / tick );

			var landedAt = -1;

			for ( var i = 0; i < expectedTicks * 4; i++ )
			{
				CorePhysics.WorldStep( world, tick, 4 );

				if ( landedAt < 0 && CorePhysics.BodyGetPosition( ball ).z <= radius + 0.01f )
					landedAt = i;
			}

			Assert.AreEqual( radius, CorePhysics.BodyGetPosition( ball ).z, 0.01f,
				"the ball should come to rest one radius above the floor" );

			Assert.IsTrue( landedAt >= 0, "the ball never reached the floor" );

			//Two ticks of slack for the solver's speculative contact, on a fall of about fifty. A
			//world in centimetres would land in a tenth of the time, and one in inches in a sixth.
			Assert.AreEqual( expectedTicks, landedAt, 2,
				$"the fall should take about {expectedTicks} ticks at {gravity} m/s^2" );
		}
		finally
		{
			CorePhysics.WorldDestroy( world );
		}
	}
}
