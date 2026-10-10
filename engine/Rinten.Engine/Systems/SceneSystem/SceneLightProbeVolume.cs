namespace Rinten;

//Keep this internal for now. Baked light in a box: three maps and the box they apply in. Held here rather than in a native object, and read
//by nothing yet - our renderer lights from the sky and the scene's own lights, and probes come with the rest of the indirect lighting.
internal class SceneLightProbe : SceneObject
{
	internal SceneLightProbe( HandleCreationData d ) : base( d )
	{
	}

	internal SceneLightProbe( SceneWorld sceneWorld,
		Texture texture,
		Texture indicesTexture,
		Texture scalarsTexture,
		BBox bounds,
		Transform transform,
		int handshake,
		int renderPriority ) : base()
	{
		Assert.IsValid( sceneWorld );

		JoinWorld( sceneWorld );

		LightProbeTexture = texture;
		DirectLightIndicesTexture = indicesTexture;
		DirectLightScalarsTexture = scalarsTexture;
		Handshake = handshake;
		RenderPriority = renderPriority;

		LocalBounds = bounds;
		Transform = transform;

		RenderingEnabled = true;
	}

	public Texture LightProbeTexture { get; set; }

	public Texture DirectLightIndicesTexture { get; set; }

	public Texture DirectLightScalarsTexture { get; set; }

	/// <summary>Which bake these came from, so a stale volume can be told from a current one.</summary>
	public int Handshake { get; set; }

	public int RenderPriority { get; set; }
}
