using NativeEngine;

namespace Rinten;

/// <summary>Base class for light scene objects for use with a <see cref="SceneWorld"/>.</summary>
[Expose]
public class SceneLight : SceneObject
{
	internal SceneLight() { }
	internal SceneLight( HandleCreationData _ ) { }

	/// <summary>Color and brightness of the light</summary>
	public Color LightColor { get; set; } = Color.White;

	//The same five metres a PointLight starts at. It was a hundred, which was a hundred inches back when a unit was one - two and a half metres - and became a hundred metres the day the unit did.
	/// <summary>How far the light reaches, in metres.</summary>
	public float Radius { get; set; } = 5.0f;

	/// <summary>How fast the light dies with distance, as the power the squared distance is raised to - see <see cref="Light.Attenuation"/>, whose default this shares, so a light made here looks like a <see cref="PointLight"/> left alone. One is the inverse square.</summary>
	public float Falloff { get; set; } = 0.15f;

	/// <summary>Whether the light casts shadows. Only a <see cref="Light"/> component's light has shadow maps drawn for it; one put into the world by itself - a particle's glow, a preview's lamp - lights what it reaches and shadows nothing, which is what those are for.</summary>
	public bool ShadowsEnabled { get; set; } = true;

	public enum FogLightingMode
	{
		None,
		Baked,
		Dynamic,
		DynamicNoShadows
	}


	//Which way it points.
	internal Vector3 WorldDirection => Transform.Rotation.Forward;

	[Obsolete( "Use ScenePointLight (or stop fucking using SceneObjects at all)" )]
	public SceneLight( SceneWorld sceneWorld, Vector3 position, float radius, Color color )
	{
		Assert.IsValid( sceneWorld );

		JoinWorld( sceneWorld );

		Position = position;
		Radius = radius;
		LightColor = color;
	}

	[Obsolete( "Use ScenePointLight (or stop fucking using SceneObjects at all)" )]
	public SceneLight( SceneWorld sceneWorld ) : this( sceneWorld, Vector3.Zero, 5.0f, Color.White * 10.0f )
	{

	}


}
