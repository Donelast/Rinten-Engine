namespace Editor;

public partial class SceneViewportPanel
{
	//The menu a right click without a drag opens: what can be done to the selection,
	//what can be made where the pointer is, and whatever the current tool wants to add.
	private void OpenContextMenu()
	{
		var menu = new ContextMenu( HostWidget ) { Searchable = true };

		bool hasSelection = Session.Selection.OfType<GameObject>().Any();

		menu.AddOption( "Cut", "content_cut", EditorScene.Cut, "editor.cut" ).Enabled = hasSelection;
		menu.AddOption( "Copy", "content_copy", EditorScene.Copy, "editor.copy" ).Enabled = hasSelection;
		menu.AddOption( "Paste", "content_paste", PasteAtCursor, "editor.paste" );
		menu.AddOption( "Paste Special", "content_paste_go", EditorScene.PasteSpecial, "editor.paste-special" );
		menu.AddSeparator();
		menu.AddOption( "Duplicate", "file_copy", SceneEditorMenus.Duplicate, "editor.duplicate" ).Enabled = hasSelection;
		menu.AddOption( "Delete", "delete", SceneEditorMenus.Delete, "editor.delete" ).Enabled = hasSelection;

		menu.AddSeparator();

		//The mapping workspace works this same scene, split four ways with the tool modes over it - this is the way in and the tab is the way back
		menu.AddOption( "Open in Mapping", "hardware", () => EditorWindow.OpenMapping() )
			.Enabled = Session.Scene.IsValid();

		menu.AddSeparator();

		Menu addMenu = menu.AddMenu( "Create" );

		using ( GizmoInstance.Push() )
		{
			var ray = ScreenPixelToRay( pointer.PressPosition );
			var trace = GetCursorTracePosition( ray );

			GameObjectNode.CreateObjectMenu( addMenu, null, go =>
			{
				if ( trace is { } tr )
				{
					var normal = tr.Normal;
					var bounds = go.GetBounds();
					var halfExtent = Vector3.Dot( bounds.Size, normal.Abs() ) / 2.0f;
					go.LocalPosition = tr.HitPosition + normal * halfExtent;
				}

				EditorScene.Selection.Clear();
				EditorScene.Selection.Add( go );
			} );

			var ev = new EditorEvent.ShowContextMenuEvent( Session, menu, ray, trace );

			EditorEvent.RunInterface<EditorEvent.ISceneView>( x => x.ShowContextMenu( ev ) );

			var activeTool = Tools?.CurrentTool;
			activeTool?.BuildSceneContextMenu( menu, ray, trace );
			activeTool?.CurrentTool?.BuildSceneContextMenu( menu, ray, trace );
		}

		menu.OpenAtCursor();
	}
}
