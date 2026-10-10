namespace Rinten;

/// <summary>Component that creates a projected decal relative to its GameObject.</summary>
[Hide]
[Obsolete( "DecalRenderer is obsolete, use Decal which uses textures instead of materials" )]
public class DecalRenderer : Renderer, Component.ExecuteInEditor
{
	[Obsolete]
	public Material Material { get; set; }

	//Most of a metre across, and deep enough to reach the wall behind whatever it is sprayed on. In metres: these were inches.
	[Property] public Vector3 Size { get; set; } = new( 0.8f, 0.8f, 6.5f );
	[Property] public Color TintColor { get; set; } = Color.White;
	[Property] public bool TriPlanar { get; set; } = false;
}
