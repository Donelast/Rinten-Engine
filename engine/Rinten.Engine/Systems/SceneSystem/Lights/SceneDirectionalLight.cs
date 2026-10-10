using System;

namespace Rinten;

/// <summary>A directional scene light that is used to mimic sun light in a <see cref="SceneWorld"/>. Direction is controlled by this object's <see cref="Rotation"/>.</summary>
[Expose]
public sealed class SceneDirectionalLight : SceneLight
{
	//Ambient light color outside of all light probes.
	[Obsolete( "Use AmbientLight Component or World.AmbientLightColor Instead." )] public Color SkyColor { get; set; }

	internal SceneDirectionalLight( HandleCreationData d ) : base( d )
	{
	}

	public SceneDirectionalLight( SceneWorld sceneWorld, Rotation rotation, Color color ) : base()
	{
		Assert.IsValid( sceneWorld );

		JoinWorld( sceneWorld );

		Rotation = rotation;
		LightColor = color;
	}

}
