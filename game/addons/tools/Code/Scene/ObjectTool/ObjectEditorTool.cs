
namespace Editor;

/// <summary>Move, rotate and scale objects</summary>
//Not offered in the mode list: Mapping is the mode objects are picked in. Kept for the movie maker's transform tracks
[EditorTool( "tools.object-tool", Hidden = true )]
[Title( "Object Select" )]
[Icon( "layers" )]
[Alias( "object" )]
public class ObjectEditorTool : EditorTool
{
	public override IEnumerable<EditorTool> GetSubtools()
	{
		yield return new PositionEditorTool();
		yield return new RotationEditorTool();
		yield return new ScaleEditorTool();

		//Drawing a block is one of this mode's own, rather than a reason to go into the mapping workspace and find the way back out of it.
		yield return new MeshEditor.BlockTool();
	}

	//Whether the block tool has the view rather than a transform one.
	private bool IsDrawingBlock => CurrentTool is MeshEditor.BlockTool;

	/// <summary>Same pick-mode column as the mesh tool, then this mode's move, rotate and scale</summary>
	public override ToolGroup CreateModeGroup()
	{
		var group = new ToolGroup( "Modes", ToolButtonSize.Mode );

		group.AddRange( MeshEditor.MeshSelectionModes.Picks( Manager ) );
		group.Add( MeshEditor.MeshSelectionModes.Shape( this ) );
		group.AddRange( SubToolCommand.ForSubToolsOf( this, MeshEditor.MeshSelectionModes.Represents ) );

		return ToolGroup.OrNull( group );
	}

	public override void OnUpdate()
	{
		base.OnUpdate();

		//Dragging a block out is not dragging a box over objects, and letting go at the end of one is not clicking on nothing.
		AllowGameObjectSelection = !IsDrawingBlock;

		if ( IsDrawingBlock )
			return;

		UpdateSelectionMode();
	}

	public override Panel CreateShortcutsPanel() => new ObjectEditorToolShortcutsPanel();

	protected override void OnBoxSelect( Frustum frustum, Rect screenRect, bool isFinal )
	{
		var selection = new HashSet<GameObject>();
		var previous = new HashSet<GameObject>();

		foreach ( var go in Scene.GetAllObjects( true ) )
		{
			//GetAllObjects starts with the scene itself, which encloses everything
			if ( go == Scene ) continue;

			//What a click cannot reach, a drag over it must not pick up either.
			if ( !CanDragSelect( go ) )
			{
				previous.Add( go );
				continue;
			}

			//Its own bounds, not its subtree's - see GetDragBounds.
			var bounds = GetDragBounds( go );

			//Nothing of its own to catch the box on and no handle to catch it at: an empty group, or a prefab root that does nothing but
			//hold the objects under it. Those answer for themselves, and a box that took the root as well took every one of them with it.
			if ( bounds.Size.AlmostEqual( 0.0f ) && !go.HasGizmoHandle )
			{
				previous.Add( go );
				continue;
			}

			//Partial, otherwise you'd have to fit the whole model in the box to select it
			if ( frustum.IsInside( bounds, true ) )
				selection.Add( go );
			else
				previous.Add( go );
		}

		ApplyDragSelection( selection, previous );
	}

	void UpdateSelectionMode()
	{
		if ( !Gizmo.HasMouseFocus )
			return;

		if ( Gizmo.WasLeftMouseReleased && !Gizmo.Pressed.Any && !IsBoxSelecting )
		{
			using ( Scene.Editor?.UndoScope( "Deselect all" ).Push() )
			{
				EditorScene.Selection.Clear();
			}
		}
	}

	public override bool HasBoxSelectionMode() => !IsDrawingBlock;
}

file class ObjectEditorToolShortcutsPanel : EditorPanel
{
	[Shortcut( "gameObject.duplicate-nudge-up", "SHIFT+UP", typeof( SceneViewPanel ) )]
	public void DuplicateNudgeUp() => SceneEditorMenus.DuplicateNudge( Vector2.Up );

	[Shortcut( "gameObject.duplicate-nudge-down", "SHIFT+DOWN", typeof( SceneViewPanel ) )]
	public void DuplicateNudgeDown() => SceneEditorMenus.DuplicateNudge( Vector2.Down );

	[Shortcut( "gameObject.duplicate-nudge-left", "SHIFT+LEFT", typeof( SceneViewPanel ) )]
	public void DuplicateNudgeLeft() => SceneEditorMenus.DuplicateNudge( Vector2.Left );

	[Shortcut( "gameObject.duplicate-nudge-right", "SHIFT+RIGHT", typeof( SceneViewPanel ) )]
	public void DuplicateNudgeRight() => SceneEditorMenus.DuplicateNudge( Vector2.Right );
}
