namespace Rinten;

//A decal. Use the Component. Just the description; the renderer doesn't project decals yet
internal sealed class DecalSceneObject : SceneObject
{
	internal DecalSceneObject() { }
	internal DecalSceneObject( HandleCreationData _ ) { }

	public Texture ColorTexture { get; set; }

	public Texture NormalTexture { get; set; }

	public Texture RMOTexture { get; set; }

	public Texture HeightTexture { get; set; }

	public Texture EmissionTexture { get; set; }

	public Color Color { get; set; } = Color.White;

	public uint SortOrder { get; set; }

	public uint ExclusionBitMask { get; set; }

	public float AttenuationAngle { get; set; }

	public float ColorMix { get; set; } = 1.0f;

	public float EmissionEnergy { get; set; }

	public uint SequenceIndex { get; set; }

	public float ParallaxStrength { get; set; }

	public int SamplerIndex { get; set; }

	public float CoverageAmount { get; set; } = 1.0f;

	public float CoverageRange { get; set; }

	public DecalSceneObject( SceneWorld world )
	{
		Assert.IsValid( world );

		JoinWorld( world );
	}
}
