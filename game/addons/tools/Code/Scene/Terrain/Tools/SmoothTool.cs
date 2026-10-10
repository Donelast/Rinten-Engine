namespace Editor.TerrainEditor;

/// <summary>Smooth the ground under the brush - a hillside, a cave's wall, the seam where a rock was set in.</summary>
[Title( "Smooth" )]
[Icon( "rounded_corner" )]
[Alias( "tools.terrain.smooth" )]
[Group( "1" )]
[Order( 0 )]
public class SmoothTool : BaseBrushTool
{
	//How much of the way to the ground's average round it a vertex goes in a second at full opacity. Measured 2026-09-27, 1 m edges at 60 frames a second
	//and full opacity: ripples under the brush are gone in half a second, and a 4 m mound as wide as a 25 m brush is 2 m in one second and 1.2 m in two.
	const float Rate = 16.0f;

	public SmoothTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
	}

	protected override void OnDab( Terrain terrain, TerrainMesh mesh, Vector3 centre, float radius, float amount, Vector3 viewDirection )
		=> mesh.Smooth( centre, radius, Math.Clamp( amount * Rate, 0.0f, 1.0f ), terrain.Storage?.EdgeLength ?? 0.0f );
}
