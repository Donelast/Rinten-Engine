
namespace Editor.MeshEditor;

/// <summary>Mesh tools mode for creating and editing meshes.</summary>
[EditorTool( "tools.mesh-tool" )]
[Title( "Mapping" )]
[Icon( "hardware" )]
[Alias( "mesh" )]
[Group( "Scene" )]
[Order( -9999 )]
public partial class MeshTool : EditorTool
{
	/// <summary>The material a face is made with, which is not this tool's to own.</summary>
	public Material ActiveMaterial
	{
		get => MaterialSelection.Current.Active;
		set => MaterialSelection.Current.Active = value;
	}

	//What is painted with, both halves: the material and the colour it lands at. See MaterialPaint. Internal, like the palette it comes from. ActiveMaterial
	//stays public because half the mesh tools reach it through their tool and it has always been there; the pair is the editor's own business.
	internal MaterialPaint ActivePaint
	{
		get => MaterialSelection.Current.Paint;
		set => MaterialSelection.Current.Paint = value;
	}

	public MoveMode MoveMode { get; set; }

	public void SetMoveMode<T>() where T : MoveMode
	{
		if ( MoveMode?.GetType() == typeof( T ) ) return;
		MoveMode = EditorTypeLibrary.Create<MoveMode>( typeof( T ) );
	}

	public override IEnumerable<EditorTool> GetSubtools()
	{
		//Object select first, because the first is what the tool comes up in and picking things up is where anybody starts.
		yield return new ObjectSelection( this );
		yield return new PrimitiveTool( this );
		yield return new VertexTool( this );
		yield return new EdgeTool( this );
		yield return new FaceTool( this );
		yield return new VertexPaintTool( this );
		yield return new DisplacementTool( this );
	}

	public override void OnEnabled()
	{
		base.OnEnabled();

		AllowGameObjectSelection = false;
		AllowContextMenu = true;

		Selection.Clear();

		SetMoveMode<PositionMode>();

		LoadToolbarCookies();
	}

	public override void OnUpdate()
	{
		AllowGameObjectSelection = CurrentTool?.GetType() == typeof( ObjectSelection ) &&
								   (MoveMode?.AllowSceneSelection ?? true);

		DrawBadFaces();
	}

	//Outline faces that failed to triangulate so they can be seen and fixed.
	private void DrawBadFaces()
	{
		foreach ( var component in Scene.GetAllComponents<MeshComponent>() )
		{
			var mesh = component.Mesh;
			if ( mesh is null || mesh.BadFaces.Count == 0 )
				continue;

			using var scope = Gizmo.Scope( "BadFaces", component.WorldTransform );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.LineThickness = 3;
			Gizmo.Draw.Color = Color.Red;

			foreach ( var hFace in mesh.BadFaces )
			{
				if ( !hFace.IsValid )
					continue;

				Gizmo.Draw.Lines( mesh.GetFaceEdges( hFace ).Select( mesh.GetEdgeLine ) );
			}
		}
	}

	public override void OnSelectionChanged()
	{
		CurrentTool?.OnSelectionChanged();
	}

	public override void BuildSceneContextMenu( Menu menu, Ray ray, SceneTraceResult? trace )
	{
		menu.AddSeparator();
		AddMenuOption( menu, "Frame Selection", "center_focus_strong", FrameSelectionFromShortcut, "mesh.frame-selection", true );
	}

	private static void FrameSelectionFromShortcut()
	{
		InvokeShortcut( "mesh.frame-selection" );
	}

	[Shortcut( "tools.mesh-tool", "m", typeof( SceneViewPanel ) )]
	public static void ActivateTool()
	{
		if ( EditorToolManager.CurrentModeName == nameof( MeshTool ) )
			return;

		EditorToolManager.SetTool( nameof( MeshTool ) );
		EditorToolManager.SetSubTool( nameof( ObjectSelection ) );
	}

	//The material last picked out in the asset browser is the one painted with - see IconGridCanvasPanel.ShowInInspector .
	[Event( "asset.highlighted" )]
	private static void OnAssetHighlighted( Asset asset )
	{
		if ( asset?.AssetType != AssetType.Material ) return;
		var material = asset.LoadResource<Material>();
		if ( material is null || !material.IsValid() ) return;

		MaterialSelection.Current.Active = material;
	}
}
