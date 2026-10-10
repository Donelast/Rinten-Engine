namespace Rinten;

//Handed to the emitter as a whole ParticleFloat rather than as this frame's number, so a rate that is a curve over the effect's life is the engine's business and not something that has to be re-read here every frame.
/// <summary>How many particles appear every second.</summary>
[Title( "Spawn Rate" ), Category( "Spawn" ), Icon( "speed" )]
public sealed class SpawnRateModule : ParticleModule
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Spawn;

	/// <summary>Particles per second.</summary>
	[Property, Range( 0.0f, 1000.0f )]
	public ParticleFloatBinding Rate { get; set; } = 10.0f;

	public override void Apply( ParticleModuleContext context )
	{
		if ( context.Emitter.IsValid() )
			context.Emitter.Rate = Rate.Resolve( context.Player );
	}
}

//A trail behind something moving, which is only a rate per second while it is moving at one speed.
/// <summary>How many particles appear for every unit the emitter travels.</summary>
[Title( "Spawn Over Distance" ), Category( "Spawn" ), Icon( "trending_flat" )]
public sealed class SpawnOverDistanceModule : ParticleModule
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Spawn;

	/// <summary>Particles per unit travelled.</summary>
	[Property, Range( 0.0f, 200.0f )]
	public ParticleFloatBinding Rate { get; set; } = 10.0f;

	public override void Apply( ParticleModuleContext context )
	{
		if ( context.Emitter.IsValid() )
			context.Emitter.RateOverDistance = Rate.Resolve( context.Player );
	}
}

/// <summary>A handful of particles at once, when the emitter starts.</summary>
[Title( "Spawn Burst" ), Category( "Spawn" ), Icon( "auto_awesome" )]
public sealed class SpawnBurstModule : ParticleModule
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Spawn;

	/// <summary>How many particles the burst is.</summary>
	[Property, Range( 0.0f, 1000.0f )]
	public ParticleFloatBinding Count { get; set; } = 50.0f;

	public override void Apply( ParticleModuleContext context )
	{
		if ( context.Emitter.IsValid() )
			context.Emitter.Burst = Count.Resolve( context.Player );
	}
}
