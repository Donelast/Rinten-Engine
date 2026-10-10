namespace Rinten;

/// <summary>Particles can have extra controllers that can modify the particles every frame.</summary>
public abstract class ParticleController : Component, Component.ExecuteInEditor
{
	/// <summary>The particle we're controlling</summary>
	public ParticleEffect ParticleEffect { get; set; }

	protected override void OnEnabled()
	{
		base.OnEnabled();

		ParticleEffect = Components.GetInAncestorsOrSelf<ParticleEffect>();

		if ( ParticleEffect is not null )
		{
			ParticleEffect.OnPreStep += OnBeforeStep;
			ParticleEffect.OnStep += OnParticleStep;
			ParticleEffect.OnPostStep += OnAfterStep;
			ParticleEffect.OnParticleCreated += OnParticleCreatedInternal;
			ParticleEffect.OnParticleDestroyed += OnParticleDestroyedInternal;
		}
		else
		{
			Log.Warning( $"No particle effect found for {this}" );
		}
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		if ( ParticleEffect is not null )
		{
			ParticleEffect.OnPreStep -= OnBeforeStep;
			ParticleEffect.OnStep -= OnParticleStep;
			ParticleEffect.OnPostStep -= OnAfterStep;
			ParticleEffect.OnParticleCreated -= OnParticleCreatedInternal;
			ParticleEffect.OnParticleDestroyed -= OnParticleDestroyedInternal;
			ParticleEffect.OnControllerDisabled( this );
		}
	}

	//Called before the particle step
	protected virtual void OnBeforeStep( float delta )
	{

	}

	//Called after the particle step
	protected virtual void OnAfterStep( float delta )
	{

	}

	//Called for each particle during the particle step. This is super threaded so you better watch out.
	protected virtual void OnParticleStep( Particle particle, float delta )
	{

	}

	protected virtual void OnParticleCreated( Particle p )
	{

	}

	void OnParticleCreatedInternal( Particle p )
	{
		try
		{
			OnParticleCreated( p );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e );
		}
	}

	void OnParticleDestroyedInternal( Particle p )
	{
		try
		{
			OnParticleDestroyed( p );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e );
		}
	}

	protected virtual void OnParticleDestroyed( Particle p )
	{

	}
}
