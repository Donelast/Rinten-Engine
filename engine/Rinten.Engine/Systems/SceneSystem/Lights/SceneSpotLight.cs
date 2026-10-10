
namespace Rinten;

/// <summary>A simple spot light scene object for use in a <see cref="SceneWorld"/>.</summary>
[Expose]
public sealed class SceneSpotLight : SceneLight
{
	/// <summary>The inner cone of the spotlight, in half angle degrees.</summary>
	public float ConeInner { get; set; } = 45.0f;

	/// <summary>The outer cone of the spotlight, in half angle degrees</summary>
	public float ConeOuter { get; set; } = 60.0f;

	public SceneSpotLight( SceneWorld world, Vector3 position, Color color ) : base()
	{
		Assert.IsValid( world );

		JoinWorld( world );

		LightColor = color;
		Position = position;

		//Six metres, as a SpotLight starts at. It was a hundred, from when a unit was an inch.
		Radius = 6.0f;
	}

	public SceneSpotLight( SceneWorld world ) : this( world, Vector3.Zero, Color.White * 10.0f )
	{

	}
}
