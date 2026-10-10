namespace Editor.TerrainEditor;

/// <summary>Cut the ground away under the brush: its triangles are removed, from what is drawn and from what is collided with alike. Undo puts them back.<br/> <br/> <b>Ctrl</b> - put the ground back</summary>
[Title( "Hole" )]
[Icon( "trip_origin" )]
[Alias( "tools.terrain.hole" )]
[Group( "1" )]
[Order( 0 )]
public class HoleTool : BaseBrushTool
{
	public HoleTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
	}

	//Nothing is moved, so there is nothing to make good.
	protected override bool Reshapes => false;

	protected override void OnDab( Terrain terrain, TerrainMesh mesh, Vector3 centre, float radius, float amount, Vector3 viewDirection )
	{
		//A frame with no time still has Ctrl held. The cut and the fill share the inner ring of the preview, so one stroke undoes the other.
		if ( Gizmo.IsCtrlPressed )
			mesh.FillHole( centre, radius * 0.5f );
		else
			mesh.DeleteTriangles( centre, radius * 0.5f );
	}

	public override bool GetHitPosition( Terrain terrain, out Vector3 position )
	{
		if ( base.GetHitPosition( terrain, out position ) ) return true;

		position = default;

		//A hole has no triangle for the ray to meet, so Ctrl would never land.
		if ( !Gizmo.IsCtrlPressed || terrain.Mesh is not { } mesh ) return false;

		var local = Gizmo.CurrentRay.ToLocal( terrain.WorldTransform );
		var direction = local.Forward.Normal;
		var scale = MathF.Max( terrain.WorldTransform.UniformScale, 1e-6f );
		var distance = Gizmo.RayDepth / scale;

		if ( !mesh.TraceOpening( local.Position, direction, distance, _parent.BrushSettings.Size * 0.5f, out position, out var normal ) )
			return false;

		HitNormal = normal;
		return true;
	}
}
