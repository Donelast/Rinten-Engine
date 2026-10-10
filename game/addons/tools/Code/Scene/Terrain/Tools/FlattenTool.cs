namespace Editor.TerrainEditor;

/// <summary>Flatten the ground to the plane it had where the stroke began - level on level ground, a ramp on a slope, a flat wall in a cave.</summary>
[Title( "Flatten" )]
[Icon( "trending_flat" )]
[Alias( "tools.terrain.flatten" )]
[Group( "1" )]
[Order( 0 )]
public class FlattenTool : BaseBrushTool
{
	const float Rate = 8.0f;

	public FlattenTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
	}

	protected override void OnDab( Terrain terrain, TerrainMesh mesh, Vector3 centre, float radius, float amount, Vector3 viewDirection )
		=> mesh.Flatten( centre, radius, StrokeStart, StrokeNormal, Math.Clamp( amount * Rate, 0.0f, 1.0f ) );
}
