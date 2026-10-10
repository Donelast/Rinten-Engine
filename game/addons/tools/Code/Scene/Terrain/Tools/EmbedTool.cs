namespace Editor.TerrainEditor;

/// <summary>Click a model standing on the terrain to take it into the ground: a real union with the terrain's mesh. The ground inside it goes, what it has under the ground goes, the two meet along one seam - and what is left is terrain, in the model's own materials, that every other brush here works on.</summary>
[Title( "Embed Model" )]
[Icon( "landslide" )]
[Alias( "tools.terrain.embed" )]
[Group( "2" )]
[Order( 0 )]
public class EmbedTool : EditorTool
{
	public EmbedTool( TerrainEditorTool terrainEditorTool )
	{
	}

	public override void OnEnabled()
	{
		AllowGameObjectSelection = false;
	}

	public override void OnUpdate()
	{
		var terrain = GetSelectedComponent<Terrain>() ?? Scene.Get<Terrain>();
		if ( !terrain.IsValid() || terrain.Mesh is null ) return;

		var hit = MeshTrace.Run();
		if ( !hit.Hit || !hit.GameObject.IsValid() ) return;

		//The ground's own chunks answer as the terrain, and so does anything already part of it.
		var target = hit.GameObject;
		if ( target == terrain.GameObject ) return;

		var renderer = target.Components.Get<ModelRenderer>();
		if ( !renderer.IsValid() || renderer.Model is null || renderer is SkinnedModelRenderer ) return;

		using ( Gizmo.Scope( "TerrainEmbedHover", target.WorldTransform ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.FromBytes( 140, 220, 120 );
			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.LineBBox( renderer.Model.Bounds );
		}

		if ( !Gizmo.WasLeftMousePressed ) return;

		var session = SceneEditorSession.Active;
		if ( session is null ) return;

		var edit = terrain.Embed( renderer.Model, target.WorldTransform, renderer.MaterialOverride );
		if ( edit is null ) return;

		//The ground first, so that undoing runs the other way: the object comes back, then the ground closes.
		session.UndoSystem.Insert( "Embed Model In Terrain",
			() => { if ( terrain.IsValid() ) terrain.Mesh?.Undo( edit ); },
			() => { if ( terrain.IsValid() ) terrain.Mesh?.Redo( edit ); } );

		//An object that is nothing but the model goes whole; one with children or a life of its own only gives up the model and what collides for it.
		var whole = target.Children.Count == 0
			&& target.Components.GetAll().All( c => c is ModelRenderer or ModelCollider );

		var given = target.Components.GetAll().Where( c => c is ModelRenderer or ModelCollider ).ToList();

		var scope = session.UndoScope( "Remove Embedded Model" );
		scope = whole ? scope.WithGameObjectDestructions( target ) : scope.WithComponentDestructions( given );

		using ( scope.Push() )
		{
			if ( whole ) target.Destroy();
			else foreach ( var component in given ) component.Destroy();
		}
	}
}
