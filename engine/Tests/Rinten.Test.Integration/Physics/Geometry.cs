
namespace PhysicsTests;

//Shape queries (triangulation, outlines, overlap, buoyancy) asserted as physical facts for parity
[TestClass]
public class GeometryTest
{
	//Half-extent of the reference box. A cube a metre a side.
	private const float HalfExtent = 0.5f;

	private static PhysicsBody MakeBox( PhysicsWorld world, Vector3 position )
	{
		var body = new PhysicsBody( world )
		{
			BodyType = PhysicsBodyType.Dynamic,
			Transform = new Transform( position ),
		};

		body.AddBoxShape( Vector3.Zero, Rotation.Identity, HalfExtent );
		body.RebuildMass();

		return body;
	}

	//A box's triangles should close up into a box. Summing the signed volume of the tetrahedra from the origin to each
	//triangle gives the volume enclosed, which for a closed surface is exact whatever the triangulation looks like.
	[TestMethod]
	public void TriangulationEnclosesTheShape()
	{
		var world = new PhysicsWorld();

		try
		{
			var body = MakeBox( world, Vector3.Zero );
			var shape = body.Shapes.First();

			shape.Triangulate( out var positions, out var indices );

			Assert.IsTrue( indices.Length >= 36, $"a box needs at least 12 triangles, got {indices.Length / 3}" );
			Assert.AreEqual( 0, indices.Length % 3, "indices should come in threes" );

			var volume = 0.0f;

			for ( var i = 0; i + 2 < indices.Length; i += 3 )
			{
				var a = positions[(int)indices[i + 0]];
				var b = positions[(int)indices[i + 1]];
				var c = positions[(int)indices[i + 2]];

				volume += Vector3.Dot( a, Vector3.Cross( b, c ) ) / 6.0f;
			}

			var side = HalfExtent * 2.0f;
			var expected = side * side * side;

			//The winding decides the sign and the two backends need not agree on it.
			Assert.AreEqual( expected, System.MathF.Abs( volume ), expected * 0.02f );
		}
		finally
		{
			world.Delete();
		}
	}

	//Two boxes a long way apart do not overlap; one moved on top of the other does, and by the amount it was moved by.
	[TestMethod]
	public void PenetrationMeasuresTheOverlap()
	{
		var world = new PhysicsWorld();

		try
		{
			var a = MakeBox( world, Vector3.Zero );
			var b = MakeBox( world, new Vector3( 1000, 0, 0 ) );

			Assert.IsFalse( a.CheckOverlap( b, b.Transform ), "boxes 1000 units apart should not overlap" );

			//Sunk a quarter of a metre into A along x, so three quarters of the metre-wide side are still clear.
			const float sink = 0.25f;
			var overlapping = new Transform( new Vector3( HalfExtent * 2.0f - sink, 0, 0 ) );

			Assert.IsTrue( a.CheckOverlap( b, overlapping ), "boxes should overlap once moved together" );
			Assert.IsTrue( a.ComputePenetration( b, overlapping, out var direction, out var distance ) );

			Assert.AreEqual( sink, distance, 0.03f, "the depth should be how far one was pushed into the other" );
			Assert.AreEqual( 1.0f, System.MathF.Abs( direction.Normal.x ), 0.05f, "they were pushed together along x" );
		}
		finally
		{
			world.Delete();
		}
	}

	//A model's physics is a set of parts held together by joints. Instancing it should produce exactly those, and taking it away should leave nothing.
	[TestMethod]
	public void AggregateBuildsEveryPartAndJoint()
	{
		//The citizen is the ragdoll in the test content: many parts, many joints.
		var model = Model.Load( "models/citizen/citizen.vmdl" );

		Assert.IsNotNull( model?.Physics, "the citizen should have physics" );
		Assert.IsTrue( model.Physics.Parts.Count > 1, "a ragdoll should have more than one part" );

		var world = new PhysicsWorld();

		try
		{
			//Whatever a fresh world starts with. It may be handed out with a body already
			//in it, so the test is that removal gets back to here rather than to nothing.
			var before = world.Bodies.Count();

			var group = world.SetupPhysicsFromModel( model, PhysicsMotionType.Dynamic );

			Assert.IsNotNull( group, "instancing a model's physics should give a group" );
			Assert.AreEqual( model.Physics.Parts.Count, group.BodyCount, "one body per part" );
			Assert.AreEqual( model.Physics.Joints.Count, group.Joints.Count(), "one joint per described joint" );
			Assert.IsTrue( group.Mass > 0.0f, "a ragdoll should weigh something" );

			foreach ( var body in group.Bodies )
			{
				Assert.IsTrue( body.IsValid(), "every body in the group should be alive" );
				Assert.IsTrue( body.Shapes.Any(), "every part should have brought its shapes" );
			}

			group.Remove();

			Assert.AreEqual( before, world.Bodies.Count(), "removing the group should take every body it added with it" );
		}
		finally
		{
			world.Delete();
		}
	}

	//A submerged body is pushed up by the weight of the fluid it displaces, and by exactly that much.
	[TestMethod]
	public void BuoyancyPushesUp()
	{
		var world = new PhysicsWorld();

		try
		{
			//Said out loud rather than left to the world's default, because the push is the weight of the displaced fluid and weight needs gravity.
			const float gravity = -9.81f;
			world.Gravity = new Vector3( 0, 0, gravity );

			var body = MakeBox( world, Vector3.Zero );

			body.Mass = 100.0f;
			body.Velocity = Vector3.Zero;

			//Gravity off, so the only thing left acting on it is the push.
			body.GravityEnabled = false;

			//Surface well above the box, so all of it is under.
			var surface = new Plane( new Vector3( 0, 0, 500 ), Vector3.Up );

			const float dt = 1.0f / 60.0f;
			const float density = 1000.0f;
			const int steps = 10;

			for ( var i = 0; i < steps; ++i )
			{
				body.Backend.ApplyBuoyancyImpulse( surface.Position, surface.Normal, density,
					0.0f, 0.0f, Vector3.Zero, world.Gravity, dt );
				world.Step( dt, 1 );
			}

			//Archimedes, in units: the displaced volume times the fluid's density is the mass of fluid pushed aside, and its weight is the force.
			var side = HalfExtent * 2.0f;
			var volume = side * side * side;
			var force = density * volume * ( -gravity );
			var expected = force / body.Mass * dt * steps;

			Assert.AreEqual( expected, body.Velocity.z, expected * 0.05f,
				"a submerged body should rise at the weight of the water it displaces" );
		}
		finally
		{
			world.Delete();
		}
	}

	//A body entirely above the surface is not in the fluid and nothing should happen to it.
	[TestMethod]
	public void BuoyancyIgnoresWhatIsOutOfTheWater()
	{
		var world = new PhysicsWorld();

		try
		{
			world.Gravity = new Vector3( 0, 0, -800 );

			var body = MakeBox( world, new Vector3( 0, 0, 500 ) );

			body.Velocity = Vector3.Zero;
			body.GravityEnabled = false;

			var surface = new Plane( Vector3.Zero, Vector3.Up );

			const float dt = 1.0f / 60.0f;

			for ( var i = 0; i < 10; ++i )
			{
				body.Backend.ApplyBuoyancyImpulse( surface.Position, surface.Normal, 1000.0f,
					0.0f, 0.0f, Vector3.Zero, world.Gravity, dt );
				world.Step( dt, 1 );
			}

			Assert.AreEqual( 0.0f, body.Velocity.z, 0.5f, "a body out of the water should be left alone" );
		}
		finally
		{
			world.Delete();
		}
	}
}
