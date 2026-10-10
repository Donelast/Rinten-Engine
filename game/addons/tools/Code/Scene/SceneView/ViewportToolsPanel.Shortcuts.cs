namespace Editor;

partial class ViewportToolsPanel
{
	[Shortcut( "scene.toggle-gizmos", "SHIFT+G", typeof( SceneViewportPanel ) )]
	void ToggleGizmos()
	{
		EditorScene.GizmoSettings.GizmosEnabled = !EditorScene.GizmoSettings.GizmosEnabled;
		Refresh();
	}

	[Shortcut( "grid.toggle-grid-snap", "G", typeof( SceneViewportPanel ) )]
	void ToggleGridSnap()
	{
		EditorScene.GizmoSettings.SnapToGrid = !EditorScene.GizmoSettings.SnapToGrid;
		Refresh();
	}

	//Both walk the same ladder the grid step widget shows, so the keys and the header bar always agree on what the next size is.

	[Shortcut( "grid.decrease-grid-size", "[", typeof( SceneViewportPanel ) )]
	void DecreaseGridSize()
	{
		EditorScene.GizmoSettings.GridSpacing = GridSteps.Step( EditorScene.GizmoSettings.GridSpacing, -1 );
		Refresh();
	}

	[Shortcut( "grid.increase-grid-size", "]", typeof( SceneViewportPanel ) )]
	void IncreaseGridSize()
	{
		EditorScene.GizmoSettings.GridSpacing = GridSteps.Step( EditorScene.GizmoSettings.GridSpacing, 1 );
		Refresh();
	}

	//X rather than T. T is one of the strip's keys - see ToolSidebarPanel.Shortcuts , which lays them out W to Y down the column - and
	//this is on the viewport itself, so it took T everywhere the viewport had focus and the mode on that key could not be reached at all.
	[Shortcut( "scene.toggle-global-space", "X", typeof( SceneViewportPanel ) )]
	void ToggleGlobalSpace()
	{
		EditorScene.GizmoSettings.GlobalSpace = !EditorScene.GizmoSettings.GlobalSpace;
		Refresh();
	}

	//The strip's own keys - the digits and W to Y - are on the panel that draws the strip,
	//see ToolSidebarPanel.Shortcuts. What is here is what belongs to the bar across the top.

	//Commit/cancel keys for drawing, here so they work outside the mapping workspace

	[Shortcut( "mesh.primitive-tool-create", "enter", typeof( SceneViewPanel ) )]
	void PressPrimitiveCreate()
	{
		if ( sceneView?.Tools?.CurrentSubTool is MeshEditor.PrimitiveTool tool ) tool.Create();
	}

	[Shortcut( "mesh.primitive-tool-cancel", "ESC", typeof( SceneViewPanel ) )]
	void PressPrimitiveCancel()
	{
		if ( sceneView?.Tools?.CurrentSubTool is MeshEditor.PrimitiveTool tool ) tool.Cancel();
	}
}
