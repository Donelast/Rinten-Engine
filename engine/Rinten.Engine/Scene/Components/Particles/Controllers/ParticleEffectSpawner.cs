namespace Rinten;

//Added by SpawnEffectOnDeathModule; the spawned player is hidden, unsaved and plays loops once
/// <summary>Plays another effect where a particle dies - a burst where a rocket ran out, a splash where a drop stopped, a puff where an ember went out.</summary>
[Hide]
[Expose]
[Title( "Particle Effect Spawner" )]
[Category( "Effects" )]
[Icon( "call_split" )]
public sealed class ParticleEffectSpawner : ParticleController
{
	/// <summary>What is played.</summary>
	[Property]
	public ParticleDefinition Effect { get; set; }

	/// <summary>How likely each death is to play it, nought to one.</summary>
	[Property, Range( 0.0f, 1.0f )]
	public float Chance { get; set; } = 1.0f;

	/// <summary>How big the spawned effect is, on top of its own scale.</summary>
	[Property]
	public float Scale { get; set; } = 1.0f;

	/// <summary>Whether the effect faces the way the particle was going.</summary>
	[Property]
	public bool AlignToVelocity { get; set; }

	/// <summary>Whether a particle that was killed by hitting something plays it, or only one that ran out of life.</summary>
	[Property]
	public bool OnCollision { get; set; } = true;

	/// <summary>Whether a particle that lived its life out plays it, or only one that died hitting something.</summary>
	[Property]
	public bool OnExpiry { get; set; } = true;

	/// <summary>The most alive at once, so a thousand sparks cannot become a thousand effects.</summary>
	[Property]
	public int MaxAlive { get; set; } = 16;

	readonly List<GameObject> _spawned = new();

	protected override void OnParticleDestroyed( Particle p )
	{
		if ( Effect is null ) return;
		if ( !Scene.IsValid() ) return;

		//A death by collision leaves a hit time behind; one by age does not.
		var collided = p.HitTime > 0.0f && ( Time.Now - p.HitTime ) < 0.1f;

		if ( collided && !OnCollision ) return;
		if ( !collided && !OnExpiry ) return;

		if ( Chance < 1.0f && Random.Shared.Float( 0.0f, 1.0f ) > Chance ) return;

		_spawned.RemoveAll( x => !x.IsValid() );

		if ( _spawned.Count >= MaxAlive ) return;

		var rotation = AlignToVelocity && !p.Velocity.IsNearlyZero()
			? Rotation.LookAt( p.Velocity.Normal )
			: Rotation.Identity;

		var go = new GameObject( true, $"{Effect.ResourceName} (spawned)" );

		go.WorldPosition = p.Position;
		go.WorldRotation = rotation;
		go.WorldScale = Scale;
		go.Flags = go.Flags
			.WithFlag( GameObjectFlags.Hidden, true )
			.WithFlag( GameObjectFlags.NotSaved, true )
			.WithFlag( GameObjectFlags.NotNetworked, true );

		var player = go.AddComponent<ParticlePlayer>();

		player.Definition = Effect;
		player.DestroyOnEnd = true;
		player.PlayOnce = true;

		_spawned.Add( go );
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		foreach ( var go in _spawned )
		{
			if ( go.IsValid() ) go.Destroy();
		}

		_spawned.Clear();
	}
}
