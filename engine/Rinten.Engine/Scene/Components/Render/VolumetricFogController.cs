namespace Rinten;

/// <summary>Internal component for storing the baked fog texture We don't need to expose the volumetric fog controller like we did previously with entities, But we need to be fetch the baked fog texture from the map file</summary>
[Title( "VolumetricFogController" )]
[Hide]
public class VolumetricFogController : Component, Component.ExecuteInEditor
{
	public Texture BakedFogTexture { get; set; }
	public float GlobalScale { get; set; } = 1.0f;

}
