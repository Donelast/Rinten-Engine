using System;

namespace SceneTests.Components;

//Pins the emitter geometry that ParticleTests.cs leaves untouched: the cone emitter's spawn envelope and velocity direction, the ring emitter's radius,
//arc, flatness and velocity-from-center behavior, plus the pure helpers on Particle itself (Create defaults, the data store, ApplyDamping and Rand).
[TestClass]
public class ParticleEmitterGeometryTest
{
	//Creates a GameObject at the given position holding a ParticleEffect with a long lifetime, so burst particles survive the assertion tick.
	static ParticleEffect CreateEffect( Scene scene, Vector3 position, out GameObject go )
	{
		go = scene.CreateObject();
		go.WorldPosition = position;

		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;

		return effect;
	}

	//A cone emitter in default placement mode (neither OnEdge nor InVolume) spawns every particle on
	//the near-plane disc: zero forward offset and a radial distance of at most tan(angle) * ConeNear.
	[TestMethod]
	public void ConeEmitter_DefaultSpawnsOnNearDisc()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var effect = CreateEffect( scene, new Vector3( 100, 200, 300 ), out var go );

		var emitter = go.Components.Create<ParticleConeEmitter>();
		emitter.Burst = 64.0f;
		emitter.Rate = 0.0f;
		emitter.ConeAngle = 30.0f;
		emitter.ConeNear = 10.0f;
		emitter.ConeFar = 50.0f;
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 64, effect.Particles.Count );

		var maxRadius = MathF.Tan( MathX.DegreeToRadian( 30.0f ) ) * 10.0f;

		foreach ( var p in effect.Particles )
		{
			var local = p.StartPosition - go.WorldPosition;
			var radial = MathF.Sqrt( local.x * local.x + local.y * local.y );

			Assert.IsTrue( MathF.Abs( local.z ) <= 0.01f, $"default placement spawns on the near plane, got z {local.z}" );
			Assert.IsTrue( radial <= maxRadius + 0.1f, $"radial {radial} exceeds the near disc radius {maxRadius}" );
		}
	}

	//With InVolume enabled the spawn positions fill the truncated cone between ConeNear and ConeFar: the
	//forward offset spans [0, far - near] and the radial distance never exceeds tan(angle) * (offset + near).
	[TestMethod]
	public void ConeEmitter_InVolumeStaysInsideConeEnvelope()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var effect = CreateEffect( scene, new Vector3( -50, 0, 25 ), out var go );

		var emitter = go.Components.Create<ParticleConeEmitter>();
		emitter.Burst = 96.0f;
		emitter.Rate = 0.0f;
		emitter.ConeAngle = 30.0f;
		emitter.ConeNear = 10.0f;
		emitter.ConeFar = 50.0f;
		emitter.InVolume = true;
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 96, effect.Particles.Count );

		var tan = MathF.Tan( MathX.DegreeToRadian( 30.0f ) );

		foreach ( var p in effect.Particles )
		{
			var local = p.StartPosition - go.WorldPosition;
			//The cone opens along the emitter's forward, which is -z.
			var forward = -local.z;
			var radial = MathF.Sqrt( local.x * local.x + local.y * local.y );

			Assert.IsTrue( forward >= -0.01f && forward <= 40.01f, $"forward offset {forward} outside [0, 40]" );
			Assert.IsTrue( radial <= tan * (forward + 10.0f) + 0.25f,
				$"radial {radial} at forward offset {forward} pokes out of the 30 degree cone" );
		}
	}

	//With OnEdge enabled every particle sits exactly on the cone surface: the radial
	//distance equals tan(angle) * (offset + near) instead of filling the interior.
	[TestMethod]
	public void ConeEmitter_OnEdgeSpawnsOnConeSurface()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var effect = CreateEffect( scene, Vector3.Zero, out var go );

		var emitter = go.Components.Create<ParticleConeEmitter>();
		emitter.Burst = 64.0f;
		emitter.Rate = 0.0f;
		emitter.ConeAngle = 30.0f;
		emitter.ConeNear = 10.0f;
		emitter.ConeFar = 50.0f;
		emitter.OnEdge = true;
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 64, effect.Particles.Count );

		var tan = MathF.Tan( MathX.DegreeToRadian( 30.0f ) );

		foreach ( var p in effect.Particles )
		{
			var local = p.StartPosition - go.WorldPosition;
			var radial = MathF.Sqrt( local.x * local.x + local.y * local.y );
			var expected = tan * (-local.z + 10.0f);

			Assert.AreEqual( expected, radial, 0.25f, $"OnEdge particle at offset {-local.z} should sit on the cone surface" );
		}
	}

	//The cone emitter redirects the effect's start velocity along the cone (from the cone tip through the spawn point) and scales it
	//by VelocityMultiplier - so every velocity stays within the cone angle of the emitter's forward axis with the multiplied speed.
	[TestMethod]
	public void ConeEmitter_VelocityFollowsConeDirection()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var effect = CreateEffect( scene, Vector3.Zero, out var go );
		effect.StartVelocity = 100.0f;

		var emitter = go.Components.Create<ParticleConeEmitter>();
		emitter.Burst = 64.0f;
		emitter.Rate = 0.0f;
		emitter.ConeAngle = 30.0f;
		emitter.ConeNear = 10.0f;
		emitter.ConeFar = 50.0f;
		emitter.InVolume = true;
		emitter.VelocityMultiplier = 2.0f;
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 64, effect.Particles.Count );

		var minDot = MathF.Cos( MathX.DegreeToRadian( 31.0f ) );

		foreach ( var p in effect.Particles )
		{
			Assert.AreEqual( 200.0f, p.Velocity.Length, 2.0f, "the 100 start velocity should be doubled by the multiplier" );

			var dot = Vector3.Dot( p.Velocity.Normal, Vector3.Forward );
			Assert.IsTrue( dot >= minDot, $"velocity {p.Velocity} leaves the 30 degree cone (dot {dot})" );
		}
	}

	//A ring emitter with zero thickness spawns every particle exactly on the ring:
	//at the configured radius on the emitter's ground plane, which is x and z.
	[TestMethod]
	public void RingEmitter_SpawnsOnRadiusInPlane()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var effect = CreateEffect( scene, new Vector3( 10, 20, 30 ), out var go );

		var emitter = go.Components.Create<ParticleRingEmitter>();
		emitter.Burst = 64.0f;
		emitter.Rate = 0.0f;
		emitter.Radius = 50.0f;
		emitter.Thickness = 0.0f;
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 64, effect.Particles.Count );

		foreach ( var p in effect.Particles )
		{
			var local = p.StartPosition - go.WorldPosition;
			var planar = MathF.Sqrt( local.x * local.x + local.z * local.z );

			Assert.AreEqual( 50.0f, planar, 0.1f, "zero thickness particles sit exactly on the ring" );
			Assert.IsTrue( MathF.Abs( local.y ) <= 0.01f, $"the ring is flat in y, got {local.y}" );
		}
	}

	//AngleStart/Angle restrict spawning to an arc: a 90 degree arc starting at 0 uses angles
	//whose sin and cos are both non-negative, so every spawn offset has non-negative x and z.
	[TestMethod]
	public void RingEmitter_ArcRestrictsSpawnAngles()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var effect = CreateEffect( scene, Vector3.Zero, out var go );

		var emitter = go.Components.Create<ParticleRingEmitter>();
		emitter.Burst = 64.0f;
		emitter.Rate = 0.0f;
		emitter.Radius = 50.0f;
		emitter.Thickness = 0.0f;
		emitter.AngleStart = 0.0f;
		emitter.Angle = 90.0f;
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 64, effect.Particles.Count );

		foreach ( var p in effect.Particles )
		{
			var local = p.StartPosition - go.WorldPosition;

			Assert.IsTrue( local.x >= -0.01f, $"a 0-90 arc never spawns at negative x, got {local.x}" );
			Assert.IsTrue( local.z >= -0.01f, $"a 0-90 arc never spawns at negative z, got {local.z}" );
		}
	}

	//VelocityFromCenter pushes each particle radially away from the ring center with exactly that speed when the effect itself adds no start velocity.
	[TestMethod]
	public void RingEmitter_VelocityFromCenterPushesOutward()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var effect = CreateEffect( scene, Vector3.Zero, out var go );

		var emitter = go.Components.Create<ParticleRingEmitter>();
		emitter.Burst = 32.0f;
		emitter.Rate = 0.0f;
		emitter.Radius = 50.0f;
		emitter.Thickness = 0.0f;
		emitter.VelocityFromCenter = 25.0f;
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 32, effect.Particles.Count );

		foreach ( var p in effect.Particles )
		{
			var outward = (p.StartPosition - go.WorldPosition).Normal;

			Assert.AreEqual( 25.0f, p.Velocity.Length, 0.5f, "the velocity magnitude comes straight from VelocityFromCenter" );
			Assert.IsTrue( Vector3.Dot( p.Velocity.Normal, outward ) >= 0.999f,
				$"velocity {p.Velocity} should point radially outward from the center" );
		}
	}

	//Thickness scatters particles around the ring (up to the thickness in any
	//direction), and Flatness 1 removes the vertical component of that scatter entirely.
	[TestMethod]
	public void RingEmitter_FlatnessRemovesVerticalScatter()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var effect = CreateEffect( scene, Vector3.Zero, out var go );

		var emitter = go.Components.Create<ParticleRingEmitter>();
		emitter.Burst = 64.0f;
		emitter.Rate = 0.0f;
		emitter.Radius = 50.0f;
		emitter.Thickness = 20.0f;
		emitter.Flatness = 1.0f;
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 64, effect.Particles.Count );

		foreach ( var p in effect.Particles )
		{
			var local = p.StartPosition - go.WorldPosition;
			var planar = MathF.Sqrt( local.x * local.x + local.z * local.z );

			Assert.IsTrue( MathF.Abs( local.y ) <= 0.01f, $"full flatness removes all vertical scatter, got {local.y}" );
			Assert.IsTrue( planar >= 29.0f && planar <= 71.0f,
				$"thickness 20 keeps particles within 20 units of the 50 ring, got {planar}" );
		}
	}

	//Particle.Create resets the pooled instance to its documented defaults: white color, full
	//alpha, size 5, zero velocity/age/angles, frame and sequence zero and a unit time scale.
	[TestMethod]
	public void Particle_CreateDefaults()
	{
		var p = Particle.Create();

		Assert.AreEqual( 0f, p.Age );
		Assert.AreEqual( 0, p.Frame );
		Assert.AreEqual( Angles.Zero, p.Angles );
		Assert.AreEqual( Vector3.Zero, p.Velocity );
		Assert.AreEqual( Color.White, p.Color );
		Assert.AreEqual( Color.White.WithAlpha( 0 ), p.OverlayColor );
		Assert.AreEqual( 1f, p.Alpha );
		Assert.AreEqual( 0, p.Sequence );
		Assert.AreEqual( Vector3.Zero, p.SequenceTime );
		Assert.AreEqual( new Vector3( 5, 5, 5 ), p.Size );
		Assert.AreEqual( 1f, p.TimeScale );
		Assert.AreEqual( -1000f, p.HitTime );
		Assert.AreEqual( -1000f, p.LastHitTime );
	}

	//The per-particle data store: Set/Get round trips typed values, keys are case-insensitive,
	//missing keys return the type default and setting an existing key overwrites it.
	[TestMethod]
	public void Particle_DataStore()
	{
		var p = Particle.Create();

		Assert.AreEqual( 0, p.Get<int>( "missing" ), "missing keys return default" );
		Assert.IsNull( p.Get<string>( "missing" ) );

		p.Set( "speed", 42 );
		Assert.AreEqual( 42, p.Get<int>( "speed" ) );
		Assert.AreEqual( 42, p.Get<int>( "SPEED" ), "keys are case-insensitive" );

		p.Set( "Speed", 7 );
		Assert.AreEqual( 7, p.Get<int>( "speed" ), "setting an existing key overwrites it" );

		p.Set( "target", new Vector3( 1, 2, 3 ) );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), p.Get<Vector3>( "target" ) );
	}

	//ApplyDamping uses source-style friction with a stop speed of 100: fast velocities bleed proportionally, slow
	//velocities bleed against the stop speed (so they hit zero), zero damping changes nothing and direction is preserved.
	[TestMethod]
	public void Particle_ApplyDamping()
	{
		var p = Particle.Create();

		p.Velocity = new Vector3( 1000, 0, 0 );
		p.ApplyDamping( 0.5f );
		Assert.AreEqual( 500f, p.Velocity.x, 0.01f, "above the stop speed the drop is speed * amount" );

		p.Velocity = new Vector3( 1000, 0, 0 );
		p.ApplyDamping( 0f );
		Assert.AreEqual( 1000f, p.Velocity.x, 0.01f, "zero damping leaves the velocity alone" );

		p.Velocity = new Vector3( 50, 0, 0 );
		p.ApplyDamping( 0.5f );
		Assert.AreEqual( Vector3.Zero, p.Velocity, "below the 100 stop speed the drop is 100 * amount, stopping it dead" );

		p.Velocity = new Vector3( 0, 300, 400 );
		p.ApplyDamping( 0.2f );
		Assert.AreEqual( 400f, p.Velocity.Length, 0.1f, "speed 500 drops by 500 * 0.2" );
		Assert.AreEqual( 240f, p.Velocity.y, 0.1f, "damping preserves the direction" );
		Assert.AreEqual( 320f, p.Velocity.z, 0.1f );
	}

	//Particle.Rand is deterministic per particle: the same seed and line produce the same
	//value in [0, 1), so controllers can derive stable per-particle randomness from it.
	[TestMethod]
	public void Particle_RandIsDeterministic()
	{
		var p = Particle.Create();

		var a = p.Rand( 3, 77 );
		var b = p.Rand( 3, 77 );

		Assert.AreEqual( a, b, "the same seed and line always produce the same value" );
		Assert.IsTrue( a >= 0f && a < 1f, $"Rand should stay in [0, 1), got {a}" );

		var c = p.Rand( 4, 77 );
		Assert.IsTrue( c >= 0f && c < 1f );
	}
}
