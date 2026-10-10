namespace Editor.TerrainEditor;

/// <summary>Dig into the ground along the line of sight: look at a hillside and hold to bore a cave into it, look down to sink a pit. The mesh is made finer as it goes.<br/> <br/> <b>Ctrl</b> - pull the ground out towards the eye instead</summary>
[Title( "Dig" )]
[Icon( "hardware" )]
[Alias( "tools.terrain.dig" )]
[Group( "1" )]
[Order( 0 )]
public class DigTool : BaseBrushTool
{
	//Metres a second at full opacity.
	const float Rate = 10.0f;

	public DigTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
		AllowBrushInvert = true;
	}

	//How hard the walls are pushed back into the rock, against how fast the brush goes in.
	const float Widen = 0.6f;

	//Pushes along view direction and along each wall's normal, so the bore stays brush-wide
	protected override void OnDab( Terrain terrain, TerrainMesh mesh, Vector3 centre, float radius, float amount, Vector3 viewDirection )
	{
		mesh.Displace( centre, radius, viewDirection, amount * Rate );
		mesh.Inflate( centre, radius, -amount * Rate * Widen );
	}
}
