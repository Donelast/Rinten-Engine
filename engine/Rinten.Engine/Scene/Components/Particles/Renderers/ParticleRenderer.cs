namespace Rinten;


/// <summary>Renders a set of particles. Should be attached to a <see cref="ParticleEffect"/>.</summary>
public abstract class ParticleRenderer : Renderer, Component.IHasBounds
{
	[RequireComponent]
	public ParticleEffect ParticleEffect { get; set; }

	protected override void OnEnabled()
	{
		if ( ParticleEffect.IsValid() )
		{
			ParticleEffect.OnParticleCreated += OnParticleCreatedInternal;
			ParticleEffect.OnPostStep += OnPostStepInternal;
		}
	}

	protected override void OnDisabled()
	{
		if ( ParticleEffect.IsValid() )
		{
			ParticleEffect.OnParticleCreated -= OnParticleCreatedInternal;
			ParticleEffect.OnPostStep -= OnPostStepInternal;
			ParticleEffect.OnControllerDisabled( this );
		}
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

	void OnPostStepInternal( float time )
	{
		try
		{
			OnPostStep( time );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e );
		}
	}

	protected virtual void OnParticleCreated( Particle p )
	{
		//fill me in
	}

	internal virtual void OnPostStep( float time )
	{
		//fill me in
	}

	//Return the bounds of this renderer in local space.
	protected virtual BBox GetLocalBounds()
	{
		if ( ParticleEffect.IsValid() )
		{
			var box = BBox.FromPositionAndSize( 0, Empty );

			foreach ( var p in ParticleEffect.Particles )
			{
				box = box.AddBBox( BBox.FromPositionAndSize( WorldTransform.PointToLocal( p.Position ), Around ) );
			}

			return box;
		}

		return BBox.FromPositionAndSize( 0, Empty );
	}

	//What an effect with no particles in it still takes up, in metres.
	private const float Empty = 0.4064f;

	//And what one particle is allowed for, since a sprite has a size.
	private const float Around = 0.1016f;

	BBox Component.IHasBounds.LocalBounds => GetLocalBounds();
}
