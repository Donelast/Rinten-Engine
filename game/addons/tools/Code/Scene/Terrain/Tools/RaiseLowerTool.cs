namespace Editor.TerrainEditor;

/// <summary>Click and drag to raise the ground, out along the way it faced where the stroke began.<br/> <br/> <b>Ctrl</b> - lower it</summary>
[Title( "Raise / Lower" )]
[Icon( "height" )]
[Alias( "tools.terrain.raise-lower" )]
[Group( "1" )]
[Order( 0 )]
public class RaiseLowerTool : BaseBrushTool
{
	//Metres a second at full opacity.
	const float Rate = 12.0f;

	public RaiseLowerTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
		AllowBrushInvert = true;
	}

	protected override void OnDab( Terrain terrain, TerrainMesh mesh, Vector3 centre, float radius, float amount, Vector3 viewDirection )
		=> mesh.Displace( centre, radius, StrokeNormal, amount * Rate );
}
