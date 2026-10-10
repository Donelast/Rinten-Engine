namespace Rinten;

/// <summary>A steady pull in one direction.</summary>
[Title( "Gravity" ), Category( "Update" ), Icon( "arrow_downward" )]
public sealed class GravityModule : ParticleModule, IParticleModuleStep
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Update;

	//Worth saying once for the whole module set: every distance and speed a module starts at is metric, because this engine counts in metres. The numbers a legacy effect is written in are inches and about forty times larger, which is a particle the size of a room leaving at highway speed.
	/// <summary>Units per second per second - and a unit is a metre, so the default is what things actually fall at.</summary>
	[Property]
	public ParticleVectorBinding Force { get; set; } = Vector3.Down * 9.8f;

	public override void Apply( ParticleModuleContext context ) { }

	public void Step( ParticleModuleContext context, float delta )
		=> context.Particle.Velocity += Force.Evaluate( context.Particle, context.Player ) * delta;
}

//Handed to the effect rather than stepped here: damping is one of the things the engine's own simulation already does, and doing it there means it happens with everything else that moves a particle rather than after all of it.
/// <summary>Air resistance - what stops a particle rather than turning it.</summary>
[Title( "Drag" ), Category( "Update" ), Icon( "air" )]
public sealed class DragModule : ParticleModule
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Update;

	/// <summary>How much of the speed is lost every second.</summary>
	[Property, Range( 0.0f, 10.0f )]
	public ParticleFloatBinding Damping { get; set; } = 0.1f;

	public override void Apply( ParticleModuleContext context )
	{
		if ( context.Effect.IsValid() )
			context.Effect.Damping = Damping.Resolve( context.Player );
	}
}

/// <summary>Turns a particle as it goes.</summary>
[Title( "Spin" ), Category( "Update" ), Icon( "rotate_right" )]
public sealed class SpinModule : ParticleModule, IParticleModuleStep
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Update;

	/// <summary>Degrees per second, per axis.</summary>
	[Property]
	public ParticleVectorBinding Speed { get; set; } = Vector3.Zero;

	public override void Apply( ParticleModuleContext context ) { }

	public void Step( ParticleModuleContext context, float delta )
		=> context.Particle.Angles += Speed.Evaluate( context.Particle, context.Player ) * delta;
}

//For anything with a front - a model, a stretched sprite. A billboard does not need it.
/// <summary>Points a particle the way it is travelling.</summary>
[Title( "Face Velocity" ), Category( "Update" ), Icon( "navigation" )]
public sealed class FaceVelocityModule : ParticleModule, IParticleModuleStep
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Update;

	public override void Apply( ParticleModuleContext context ) { }

	public void Step( ParticleModuleContext context, float delta )
	{
		var velocity = context.Particle.Velocity;

		if ( velocity.IsNearZeroLength ) return;

		context.Particle.Angles = Rotation.LookAt( velocity ).Angles();
	}
}

/// <summary>Where the particle positions are measured from.</summary>
[Expose]
public enum ParticleSpace
{
	/// <summary>Relative to the emitter.</summary>
	Local,

	/// <summary>An absolute place in the world.</summary>
	World
}

/// <summary>A point that pulls particles towards it - full strength inside its own size, falling away outside it.</summary>
[Title( "Attractor" ), Category( "Update" ), Icon( "my_location" )]
public sealed class AttractorModule : ParticleModule, IParticleModuleStep
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Update;

	[Property]
	public ParticleSpace Space { get; set; } = ParticleSpace.Local;

	[Property]
	public ParticleVectorBinding Position { get; set; } = Vector3.Zero;

	/// <summary>How hard it pulls, in units per second per second.</summary>
	[Property, Range( 0.0f, 100.0f )]
	public ParticleFloatBinding Strength { get; set; } = 5.0f;

	/// <summary>How far out the pull is at full strength.</summary>
	[Property, Range( 0.01f, 100.0f )]
	public float Size { get; set; } = 0.5f;

	/// <summary>Push rather than pull.</summary>
	[Property]
	public bool Invert { get; set; }

	/// <summary>How sharply the pull falls away past the edge. 1 is linear, 2 is inverse square, higher is sharper.</summary>
	[Property, Range( 0.1f, 8.0f )]
	public float Falloff { get; set; } = 2.0f;

	public override void Apply( ParticleModuleContext context ) { }

	public void Step( ParticleModuleContext context, float delta )
	{
		var origin = Position.Evaluate( context.Particle, context.Player );

		//Local means the emitter's own frame: turned with it as well as carried by it, so a point beside a pitched emitter stays beside it.
		if ( Space == ParticleSpace.Local && context.Emitter.IsValid() )
			origin = context.Emitter.WorldPosition + ( context.Emitter.WorldRotation * origin );

		var toward = origin - context.Particle.Position;
		var distance = toward.Length;

		if ( distance < 0.01f ) return;

		var strength = 1.0f;

		if ( distance > Size )
		{
			//How many of its own radii past the edge we are: zero at the surface, growing outward.
			var excess = ( distance - Size ) / Size;

			strength = 1.0f / MathF.Pow( 1.0f + excess, Falloff );
		}

		//A push is the pull the other way, with the same falloff - not the falloff turned inside out, which pushed nothing near and everything far.
		if ( Invert ) toward = -toward;

		context.Particle.Velocity += toward.Normal * Strength.Evaluate( context.Player ) * strength * delta;
	}
}

/// <summary>A swirl about an axis - particles orbit rather than fall in.</summary>
[Title( "Vortex" ), Category( "Update" ), Icon( "cyclone" )]
public sealed class VortexModule : ParticleModule, IParticleModuleStep
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Update;

	[Property]
	public ParticleSpace Space { get; set; } = ParticleSpace.Local;

	[Property]
	public Vector3 Center { get; set; } = Vector3.Zero;

	[Property]
	public ParticleVectorBinding Axis { get; set; } = Vector3.Up;

	[Property, Range( 0.0f, 1000.0f )]
	public ParticleFloatBinding Strength { get; set; } = 5.0f;

	public override void Apply( ParticleModuleContext context ) { }

	public void Step( ParticleModuleContext context, float delta )
	{
		var center = Center;
		var axis = Axis.Evaluate( context.Particle, context.Player ).Normal;

		//Local means the emitter's own frame: the axis turns with the emitter, so a portal stood on its side still spins in its own plane.
		if ( Space == ParticleSpace.Local && context.Emitter.IsValid() )
		{
			center = context.Emitter.WorldPosition + ( context.Emitter.WorldRotation * center );
			axis = context.Emitter.WorldRotation * axis;
		}

		var outward = context.Particle.Position - center;
		var distance = outward.Length;

		if ( distance < 0.01f ) return;
		var tangent = Vector3.Cross( axis, outward.Normal );

		context.Particle.Velocity += tangent * ( Strength.Evaluate( context.Player ) / distance ) * delta;
	}
}

//The curl of a noise field, which is divergence-free by construction: whatever flows into a region flows out of it again. The field itself is a handful of sines rather than proper Perlin noise - cheap, and at these scales the difference does not show.
/// <summary>A flow field that swirls without ever pushing particles together - what makes smoke look like smoke rather than like a spray.</summary>
[Title( "Curl Noise" ), Category( "Update" ), Icon( "waves" )]
public sealed class CurlNoiseModule : ParticleModule, IParticleModuleStep
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Update;

	/// <summary>How hard the flow pushes.</summary>
	[Property, Range( 0.0f, 100.0f )]
	public ParticleFloatBinding Strength { get; set; } = 1.0f;

	/// <summary>How big the swirls are - smaller numbers make wider ones.</summary>
	[Property, Range( 0.01f, 10.0f )]
	public ParticleFloatBinding Scale { get; set; } = 1.0f;

	/// <summary>How fast the field itself moves.</summary>
	[Property, Range( 0.0f, 10.0f )]
	public ParticleFloatBinding TimeScale { get; set; } = 1.0f;

	/// <summary>Where in the field this effect sits, so two of them differ.</summary>
	[Property]
	public Vector3 Offset { get; set; } = Vector3.Zero;

	public override void Apply( ParticleModuleContext context ) { }

	public void Step( ParticleModuleContext context, float delta )
	{
		var particle = context.Particle;
		var position = ( particle.Position + Offset ) * Scale.Evaluate( context.Player );
		var time = particle.Age * TimeScale.Evaluate( context.Player );

		particle.Velocity += Curl( position, time ) * Strength.Evaluate( context.Player ) * delta;
	}

	//Curl of the potential field by central differences; each of the six samples taken once
	static Vector3 Curl( Vector3 position, float time )
	{
		const float epsilon = 0.001f;

		var x = new Vector3( epsilon, 0.0f, 0.0f );
		var y = new Vector3( 0.0f, epsilon, 0.0f );
		var z = new Vector3( 0.0f, 0.0f, epsilon );

		var aheadX = Potential( position + x, time );
		var behindX = Potential( position - x, time );
		var aheadY = Potential( position + y, time );
		var behindY = Potential( position - y, time );
		var aheadZ = Potential( position + z, time );
		var behindZ = Potential( position - z, time );

		var curl = new Vector3(
			( aheadY.z - behindY.z ) - ( aheadZ.y - behindZ.y ),
			( aheadZ.x - behindZ.x ) - ( aheadX.z - behindX.z ),
			( aheadX.y - behindX.y ) - ( aheadY.x - behindY.x ) );

		return curl / ( 2.0f * epsilon );
	}

	//Three offset samples of the same field, which makes a vector field out of a scalar one.
	static Vector3 Potential( Vector3 position, float time ) => new(
		Noise( position, time ),
		Noise( position + new Vector3( 31.416f, -47.853f, 12.793f ), time ),
		Noise( position + new Vector3( -17.737f, 86.214f, -59.482f ), time ) );

	static float Noise( Vector3 position, float time )
	{
		var p = position + new Vector3( time, time * 0.7f, time * 0.5f );

		return MathF.Sin( p.x * 1.0f + p.y * 1.3f ) * 0.5f
			+ MathF.Sin( p.y * 1.7f + p.z * 0.9f ) * 0.3f
			+ MathF.Sin( p.z * 2.1f + p.x * 1.1f ) * 0.2f
			+ MathF.Sin( p.x * 3.7f + p.y * 2.3f + p.z * 1.9f ) * 0.15f;
	}
}

//Rotates position about the axis at a fixed rate instead of applying a force
/// <summary>Carries the particles round an axis at a steady rate, whatever else they are doing - a ring that stays a ring.</summary>
[Title( "Orbit" ), Category( "Update" ), Icon( "autorenew" )]
public sealed class OrbitModule : ParticleModule, IParticleModuleStep
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Update;

	[Property]
	public ParticleSpace Space { get; set; } = ParticleSpace.Local;

	/// <summary>Where the axis passes through, from the emitter or in the world.</summary>
	[Property]
	public Vector3 Center { get; set; } = Vector3.Zero;

	/// <summary>Which way the axis points. Up is a carousel.</summary>
	[Property]
	public ParticleVectorBinding Axis { get; set; } = Vector3.Up;

	/// <summary>Degrees a second. Negative goes the other way round.</summary>
	[Property, Range( -720.0f, 720.0f )]
	public ParticleFloatBinding Speed { get; set; } = 90.0f;

	public override void Apply( ParticleModuleContext context ) { }

	public void Step( ParticleModuleContext context, float delta )
	{
		var center = Center;
		var axis = Axis.Evaluate( context.Particle, context.Player );

		if ( Space == ParticleSpace.Local && context.Emitter.IsValid() )
		{
			center = context.Emitter.WorldPosition + ( context.Emitter.WorldRotation * center );
			axis = context.Emitter.WorldRotation * axis;
		}

		if ( axis.IsNearlyZero() ) return;

		var degrees = Speed.Evaluate( context.Particle, context.Player ) * delta;
		if ( degrees == 0.0f ) return;

		var turn = Rotation.FromAxis( axis.Normal, degrees );
		var particle = context.Particle;

		particle.Position = center + ( turn * ( particle.Position - center ) );
		particle.Velocity = turn * particle.Velocity;
	}
}
