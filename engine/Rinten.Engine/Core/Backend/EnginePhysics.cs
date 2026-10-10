namespace Rinten;

//Physics console commands that measure the same things on either backend, for comparison
internal static class EnginePhysics
{
	[ConCmd( "physics_status", ConVarFlags.Protected, Help = "Report the physics backend and self-test it" )]
	public static void PrintStatus()
	{
		Log.Info( "Physics backend: our core (box3d)" );
		Log.Info( $"rintencore       : {CoreNative.BuildInfo}" );

		if ( CoreNative.Available )
		{
			Log.Info( "" );
			Log.Info( "native core, straight to the ABI:" );
			SelfTest();
		}
		else
		{
			Log.Info( "  native self-test skipped - build it with engine/Core/build-win64.cmd" );
		}

		Log.Info( "" );
		Log.Info( "through the engine's own physics API:" );
		ManagedSelfTest();
	}

	//Initialiser must be a constant for the ConVar attribute; the env var is read in the static ctor
	/// <summary>Seconds between automatic telemetry dumps, or zero for off</summary>
	[ConVar( "physics_telemetry", ConVarFlags.Protected,
		Help = "Seconds between physics telemetry dumps to the log. 0 is off." )]
	public static float TelemetryInterval { get; set; } = 0.0f;

	static EnginePhysics()
	{
		//Also settable before the console exists, for chasing something that happens during startup.
		if ( float.TryParse( Environment.GetEnvironmentVariable( "SBOX_PHYSICS_STATS" ), out var seconds ) )
			TelemetryInterval = seconds;
	}

	/// <summary>Draw the collision our core is actually simulating.</summary>
	[ConVar( "physics_draw", ConVarFlags.Protected, Help = "Draw the collision our core is simulating" )]
	public static bool DebugDraw { get; set; } = false;

	[ConVar( "physics_draw_bounds", ConVarFlags.Protected, Help = "Also draw each body's bounding box" )]
	public static bool DebugDrawBounds { get; set; } = false;

	private static readonly System.Diagnostics.Stopwatch statsTimer = System.Diagnostics.Stopwatch.StartNew();

	//Called after each step; dumps the counts on an interval, if asked to.
	internal static void TickStatsLog()
	{
		if ( TelemetryInterval <= 0 ) return;
		if ( statsTimer.Elapsed.TotalSeconds < TelemetryInterval ) return;

		statsTimer.Restart();
		PrintMotion();
	}

	/// <summary>Mass of a box of known size, on whichever backend is running. Mass is where a units mistake shows first and shows quietly: too heavy and forces stop moving anything, while gravity keeps working, because gravity does not care about mass. A single number that both backends can be asked for settles it.</summary>
	[ConCmd( "physics_compare", ConVarFlags.Protected, Help = "Measure the physics the same way on either backend, to compare them" )]
	public static void PrintComparison()
	{
		//A metre to a side, so at the density of water the answer is a tonne exactly, and
		//anything else is the units being wrong somewhere between here and the solver.
		const float halfExtent = 0.5f;

		PhysicsWorld world = null;

		try
		{
			world = new PhysicsWorld();

			var body = new PhysicsBody( world ) { BodyType = PhysicsBodyType.Dynamic };
			body.AddBoxShape( Vector3.Zero, Rotation.Identity, halfExtent );
			body.RebuildMass();

			var sphere = new PhysicsBody( world ) { BodyType = PhysicsBodyType.Dynamic };
			sphere.AddSphereShape( Vector3.Zero, halfExtent );
			sphere.RebuildMass();

			Log.Info( $"reference masses: " +
				$"{halfExtent * 2}-unit cube {body.Mass:N1} kg, radius-{halfExtent} sphere {sphere.Mass:N1} kg, " +
				$"cube inertia {body.Inertia}" );

			//What a known impulse does to a known mass. Mass alone cannot tell a units mistake in the force path
			//from a correct one, and this can: the answer is impulse over mass, in whatever units the engine speaks.
			const float impulse = 1000.0f;

			body.GravityEnabled = false;
			body.Velocity = Vector3.Zero;
			body.AngularVelocity = Vector3.Zero;
			body.ApplyImpulse( Vector3.Forward * impulse );

			world.Step( 1.0f / 60.0f, 1 );

			var speed = body.Velocity.Length;
			Log.Info( $"  impulse {impulse:N0} on {body.Mass:N1} kg gave {speed:N3} units/s " +
				$"(expected {impulse / body.Mass:N3} if impulses are kg*units/s)" );

			PrintPushTest();
			PrintSweepSemantics();
			PrintSpinUnits();
			PrintOffsetForce();
		}
		catch ( Exception e )
		{
			Log.Warning( $"reference mass failed: {e.Message}" );
		}
		finally
		{
			world?.Delete();
		}
	}

	//Drops a sphere to show whether EndPosition and HitPosition mean the centre or the contact point
	private static void PrintSweepSemantics()
	{
		PhysicsWorld world = null;

		try
		{
			world = new PhysicsWorld();

			//Floor with its top face at z = 0.
			var floor = new PhysicsBody( world )
			{
				BodyType = PhysicsBodyType.Static,
				Transform = new Transform( new Vector3( 0, 0, -16 ) ),
			};
			floor.AddBoxShape( Vector3.Zero, Rotation.Identity, new Vector3( 512, 512, 16 ) );

			//Step once before asking. A world that has never been stepped has no broadphase to trace against, and answers nonsense rather than nothing.
			world.Step( 1.0f / 60.0f, 1 );

			const float radius = 20.0f;
			var from = new Vector3( 0, 0, 200 );
			var to = new Vector3( 0, 0, -200 );

			var result = world.Trace.Sphere( radius, from, to ).Run();

			Log.Info( $"sweep semantics: radius {radius} sphere from {from.z} to {to.z} over a floor topped at 0" );
			Log.Info( $"  long drop  : hit {result.Hit}, fraction {result.Fraction:N4}, " +
				$"end z {result.EndPosition.z:N2}, hit z {result.HitPosition.z:N2}, distance {result.Distance:N2}" );
			Log.Info( $"  (centre at rest is {radius:N2}; the surface it touched is 0.00)" );

			//The same shape as a wheel: a capsule across the hub, swept down by the suspension travel from just above
			//the ground. Distance is what the suspension turns into compression, so this is the number that matters.
			const float travel = 40.0f;
			var hubTop = new Vector3( 0, 0, radius + 10.0f );
			var hubEnd = hubTop - new Vector3( 0, 0, travel );

			var wheel = new Capsule( new Vector3( 0, -8, 0 ), new Vector3( 0, 8, 0 ), radius );
			var swept = world.Trace.Capsule( wheel, hubTop, hubEnd ).Run();

			Log.Info( $"  wheel sweep: hit {swept.Hit}, fraction {swept.Fraction:N4}, " +
				$"end z {swept.EndPosition.z:N2}, hit z {swept.HitPosition.z:N2}, distance {swept.Distance:N2}" );
			Log.Info( $"  (hub starts at {hubTop.z:N2}, travel {travel:N0}, so a wheel resting on the floor " +
				$"stops with its centre at {radius:N2} - distance {hubTop.z - radius:N2}, compression {travel - (hubTop.z - radius):N2})" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"sweep semantics failed: {e.Message}" );
		}
		finally
		{
			world?.Delete();
		}
	}

	//What one unit of angular velocity actually turns into. Set a spin, run for a second, measure the angle swept. Radians per second
	//and degrees per second differ by 57, and a round-trip through the property cannot tell them apart - only the resulting rotation can.
	private static void PrintSpinUnits()
	{
		PhysicsWorld world = null;

		try
		{
			world = new PhysicsWorld { Gravity = Vector3.Zero };

			var body = new PhysicsBody( world ) { BodyType = PhysicsBodyType.Dynamic };
			body.AddBoxShape( Vector3.Zero, Rotation.Identity, 20 );
			body.RebuildMass();

			body.AngularVelocity = new Vector3( 0, 0, 1 );

			var before = body.Rotation;
			for ( var i = 0; i < 60; i++ ) world.Step( 1.0f / 60.0f, 1 );
			var swept = (body.Rotation * before.Inverse).Angle();

			Log.Info( $"spin units: " +
				$"one unit of angular velocity for one second swept {swept:N2} degrees " +
				$"(57.3 means radians per second, 1.0 means degrees per second)" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"spin units failed: {e.Message}" );
		}
		finally
		{
			world?.Delete();
		}
	}

	//Checks point velocity and off-centre forces, the two things a tyre model depends on
	private static void PrintOffsetForce()
	{
		PhysicsWorld world = null;

		try
		{
			world = new PhysicsWorld { Gravity = Vector3.Zero };

			var body = new PhysicsBody( world ) { BodyType = PhysicsBodyType.Dynamic };
			body.AddBoxShape( Vector3.Zero, Rotation.Identity, 20 );
			body.RebuildMass();

			//A point 40 units to the right of the centre, as a wheel would be.
			var offset = new Vector3( 0, 40, 0 );

			body.Velocity = new Vector3( 100, 0, 0 );
			body.AngularVelocity = new Vector3( 0, 0, 1 );

			var atPoint = body.GetVelocityAtPoint( body.Position + offset );

			Log.Info( $"wheel inputs: " +
				$"body moving 100 forward and spinning 1 about z" );
			Log.Info( $"  velocity 40 to the left of centre: {atPoint} (expected 100,0,0 plus spin cross offset = 60,0,0)" );

			body.Velocity = Vector3.Zero;
			body.AngularVelocity = Vector3.Zero;

			//One second of a force big enough to move a tonne, applied off centre.
			var force = new Vector3( 0, 0, body.Mass * 100.0f );

			for ( var i = 0; i < 60; i++ )
			{
				body.ApplyForceAt( body.Position + offset, force );
				world.Step( 1.0f / 60.0f, 1 );
			}

			Log.Info( $"  after a second of {force.z:N0} up at that point: " +
				$"v {body.Velocity} spin {body.AngularVelocity}" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"wheel inputs failed: {e.Message}" );
		}
		finally
		{
			world?.Delete();
		}
	}

	private static void PrintPushTest()
	{
		PushTest( "box floor", useHeightField: false );
		PushTest( "height field", useHeightField: true );
	}

	private static void PushTest( string label, bool useHeightField )
	{
		PhysicsWorld world = null;

		try
		{
			//The same gravity a scene runs at, in metres a second squared. It was eight hundred and fifty, which is that gravity in
			//inches - so this test dropped things thirty nine times too fast and measured a physics engine nothing else was using.
			world = new PhysicsWorld { Gravity = Vector3.Down * 21.6f };

			PhysicsBody floor;

			if ( useHeightField )
			{
				//Flat terrain, laid out and rotated exactly the way the Terrain component does it - the point is to exercise that path, not to make a hill.
				const int resolution = 64;
				const float cell = 128.0f;

				var heights = new ushort[resolution * resolution];
				var materials = new byte[resolution * resolution];

				floor = new PhysicsBody( world )
				{
					BodyType = PhysicsBodyType.Static,
					//The grid lies on X and Z now, and needs no turning - see CorePhysicsBodyBackend.CreateHeightFieldShape.
					Transform = new Transform( new Vector3( -2048, 0, -2048 ) ),
				};

				floor.AddHeightFieldShape( heights, materials, resolution, resolution, cell, 1.0f, 1 );
			}
			else
			{
				floor = new PhysicsBody( world )
				{
					BodyType = PhysicsBodyType.Static,
					Transform = new Transform( new Vector3( 0, 0, -16 ) ),
				};
				floor.AddBoxShape( Vector3.Zero, Rotation.Identity, new Vector3( 2048, 2048, 16 ) );
			}

			var box = new PhysicsBody( world )
			{
				BodyType = PhysicsBodyType.Dynamic,
				Transform = new Transform( new Vector3( 0, 0, 20 ) ),
			};
			box.AddBoxShape( Vector3.Zero, Rotation.Identity, 20 );
			box.RebuildMass();

			//Let it come to rest first, so the push is measured against contact rather than against a body still falling.
			for ( var i = 0; i < 60; i++ ) world.Step( 1.0f / 60.0f, 1 );

			var restingHeight = box.Position.z;
			var force = box.Mass * 850.0f;   //one gravity's worth, sideways

			for ( var i = 0; i < 60; i++ )
			{
				box.ApplyForce( Vector3.Forward * force );
				world.Step( 1.0f / 60.0f, 1 );
			}

			var friction = box.Shapes.FirstOrDefault()?.Friction ?? -1;
			var groundFriction = floor.Shapes.FirstOrDefault()?.Friction ?? -1;

			Log.Info( $"  push test on {label}: rested at z {restingHeight:N2}, one second of {force:N0} " +
				$"gave {box.Velocity.x:N1} units/s forward, {box.Velocity.Length:N1} total, " +
				$"friction box {friction:N2} ground {groundFriction:N2}" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"push test on {label} failed: {e.Message}" );
		}
		finally
		{
			world?.Delete();
		}
	}

	/// <summary>What the moving bodies are doing, to compare the same drive between backends</summary>
	[ConCmd( "physics_motion", ConVarFlags.Protected, Help = "What the moving bodies are doing, for comparing backends" )]
	public static void PrintMotion()
	{
		Log.Info( $"  {CorePhysicsTrace.Stats.TakeSummary()}" );

		foreach ( var world in PhysicsWorld.All )
		{
			if ( world is null ) continue;

			var bodies = world.Bodies.Where( x => x.IsValid() ).ToArray();
			if ( bodies.Length == 0 ) continue;

			//The breakdown first: "nothing is moving" and "nothing is dynamic" look the same in a list of movers, and they mean different things.
			var dynamic = bodies.Count( x => x.BodyType == PhysicsBodyType.Dynamic );
			var keyframed = bodies.Count( x => x.BodyType == PhysicsBodyType.Keyframed );
			var awake = bodies.Count( x => !x.Sleeping );

			Log.Info( $"  bodies {bodies.Length}: {dynamic} dynamic, {keyframed} keyframed, " +
				$"{bodies.Length - dynamic - keyframed} static, {awake} awake" );

			//Fastest first, asleep or not - a body that should be moving and is not is exactly what we are looking for.
			foreach ( var body in bodies.OrderByDescending( x => x.Velocity.Length ).Take( 4 ) )
			{
				if ( body.Velocity.Length < 0.01f && body.AngularVelocity.Length < 0.01f ) break;
				Describe( "fast", body );
			}

			//Anything constrained: a vehicle is the obvious case, and it is never the fastest thing on the map when it is the thing refusing to move.
			foreach ( var body in bodies.Where( x => x.Joints.Count > 0 )
				.OrderByDescending( x => x.Joints.Count ).Take( 8 ) )
			{
				Describe( "jointed", body );
			}
		}
	}

	private static void Describe( string why, PhysicsBody body )
	{
		var name = body.GameObject.IsValid() ? body.GameObject.Name : "<no object>";

		Log.Info( $"    {why,-8} {body.BodyType,-9} {(body.Sleeping ? "asleep" : "awake ")} {body.Mass,9:N0} kg  " +
			$"v {body.Velocity.Length,8:N1}  spin {body.AngularVelocity.Length,7:N2}  " +
			$"joints {body.Joints.Count}  shapes {body.ShapeCount}  {name}" );
	}

	[ConCmd( "physics_mass", ConVarFlags.Protected, Help = "Heaviest physics bodies, for comparing backends" )]
	public static void PrintMasses()
	{

		var index = 0;
		foreach ( var world in PhysicsWorld.All )
		{
			index++;
			if ( world is null ) continue;

			var bodies = world.Bodies
				.Where( x => x.IsValid() && x.BodyType == PhysicsBodyType.Dynamic )
				.OrderByDescending( x => x.Mass )
				.Take( 8 )
				.ToArray();

			var total = world.Bodies.Where( x => x.IsValid() ).Sum( x => x.Mass );

			Log.Info( $"  world {index}: {world.BodyCount} bodies, {total:N0} kg total, {bodies.Length} dynamic shown" );

			foreach ( var body in bodies )
			{
				var name = body.GameObject.IsValid() ? body.GameObject.Name : "<no object>";
				Log.Info( $"    {body.Mass,10:N1} kg  {body.ShapeCount} shapes  {name}" );
			}
		}
	}

	[ConCmd( "physics_core_stats", ConVarFlags.Protected, Help = "Live physics object counts, for finding leaks" )]
	public static unsafe void PrintStats()
	{

		//Managed against total says which half is growing before any of the per-object counts have to be read.
		var managed = GC.GetTotalMemory( false ) / (1024 * 1024);
		var process = Environment.WorkingSet / (1024 * 1024);

		Log.Info( $"memory            : {managed} MB managed of {process} MB process" );
		Log.Info( $"managed registries: {CoreShapeRegistry.Count} shapes, {CoreBodyRegistry.Count} bodies" );
		Log.Info( $"worlds            : {PhysicsWorld.All.Count}" );

		var index = 0;
		foreach ( var world in PhysicsWorld.All )
		{
			index++;
			if ( world?.Backend is not CorePhysicsWorldBackend core || !core.IsValid ) continue;

			Span<int> counts = stackalloc int[6];
			CorePhysics.WorldCounts( core.Handle, counts );

			Log.Info( $"  world {index}: bodies {counts[0]}, shapes {counts[1]}, contacts {counts[2]}, " +
				$"joints {counts[3]}, islands {counts[4]}, borrowed geometry {counts[5]}, " +
				$"touching {core.Events.TouchingCount}, managed bodies {world.BodyCount}" );
		}
	}

	//The drop test through PhysicsWorld/Body/Shape, so it runs on whichever backend is active
	private static void ManagedSelfTest()
	{
		const float radius = 10.0f;
		const float dropHeight = 200.0f;
		const int ticks = 240;

		PhysicsWorld world = null;

		try
		{
			world = new PhysicsWorld { Gravity = Vector3.Down * 20.3f };

			//Floor: top face at z = 0, so the ball should rest at exactly its radius.
			var floor = new PhysicsBody( world )
			{
				BodyType = PhysicsBodyType.Static,
				Transform = new Transform( new Vector3( 0, 0, -16 ) ),
			};
			floor.AddBoxShape( Vector3.Zero, Rotation.Identity, new Vector3( 512, 512, 16 ) );

			var ball = new PhysicsBody( world )
			{
				BodyType = PhysicsBodyType.Dynamic,
				Transform = new Transform( new Vector3( 0, 0, dropHeight ) ),
			};
			ball.AddSphereShape( Vector3.Zero, radius );
			ball.RebuildMass();

			var timer = System.Diagnostics.Stopwatch.StartNew();
			for ( var i = 0; i < ticks; i++ )
				world.Step( 1.0f / 60.0f, 4 );
			timer.Stop();

			var resting = ball.Position.z;
			var error = MathF.Abs( resting - radius );

			Log.Info( $"  bodies         : {world.BodyCount}, mass {ball.Mass:0.00} kg" );
			Log.Info( $"  {ticks} ticks      : {timer.Elapsed.TotalMilliseconds:0.00} ms" );
			Log.Info( $"  resting height : {resting:0.000} (expected {radius:0.000}), sleeping {ball.Sleeping}" );
			Log.Info( error < 0.1f ? "  self-test: OK" : "  self-test: FAILED" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"  self-test FAILED: {e.Message}" );
		}
		finally
		{
			world?.Delete();
		}
	}

	//Drops a sphere on a floor and checks where it rests; exercises the whole path incl. length units
	private static void SelfTest()
	{
		//A quarter-metre ball dropped from five metres, at the density of water, onto a twelve-metre floor.
		const float radius = 0.25f;
		const float dropHeight = 5.0f;
		const float density = 1000.0f;
		const int ticks = 240;

		var world = CorePhysics.WorldCreate();
		if ( world == 0 )
		{
			Log.Warning( "  self-test FAILED: could not create a world" );
			return;
		}

		try
		{
			Log.Info( $"  gravity        : {CorePhysics.WorldGetGravity( world )}" );

			//A floor whose top face sits at z = 0, so the resting height of the ball is just its radius and the expected answer needs no arithmetic.
			var floor = CorePhysics.BodyCreate( world, CorePhysics.MotionType.Static,
				new Vector3( 0, 0, -0.4f ), Rotation.Identity );
			CorePhysics.BodyAddBox( floor, Vector3.Zero, Rotation.Identity,
				new Vector3( 12, 12, 0.4f ), density, false );

			var ball = CorePhysics.BodyCreate( world, CorePhysics.MotionType.Dynamic,
				new Vector3( 0, 0, dropHeight ), Rotation.Identity );
			CorePhysics.BodyAddSphere( ball, Vector3.Zero, radius, density, false );

			if ( ball == 0 )
			{
				Log.Warning( "  self-test FAILED: could not create a body" );
				return;
			}

			var timer = System.Diagnostics.Stopwatch.StartNew();
			for ( var i = 0; i < ticks; i++ )
				CorePhysics.WorldStep( world, 1.0f / 60.0f, 4 );
			timer.Stop();

			var resting = CorePhysics.BodyGetPosition( ball ).z;
			var error = MathF.Abs( resting - radius );
			var asleep = !CorePhysics.BodyIsAwake( ball );

			Log.Info( $"  bodies         : {CorePhysics.WorldBodyCount( world )}" );
			Log.Info( $"  {ticks} ticks      : {timer.Elapsed.TotalMilliseconds:0.00} ms" );
			Log.Info( $"  resting height : {resting:0.000} (expected {radius:0.000}), asleep {asleep}" );

			//Three millimetres covers the contact softness the solver leaves behind, and is far tighter than any unit mistake.
			Log.Info( error < 0.003f && asleep
				? "  self-test: OK"
				: "  self-test: FAILED" );
		}
		finally
		{
			CorePhysics.WorldDestroy( world );
		}
	}
}
