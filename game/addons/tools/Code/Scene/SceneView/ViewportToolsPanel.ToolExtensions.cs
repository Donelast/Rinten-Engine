namespace Editor;

partial class ViewportToolsPanel
{
	private Group toolExtensionDivider;
	private Group toolExtensionGroup;
	private EditorTool lastTool;

	//Keeps the end of the row for whatever the current tool wants to put on the bar. Empty until a tool has something to say.
	private void BuildToolExtensionToolbar( List<Group> section )
	{
		toolExtensionDivider = new Group { Divider = true, Visible = false };
		toolExtensionGroup = new Group { Plain = true, Visible = false };

		section.Add( toolExtensionDivider );
		section.Add( toolExtensionGroup );

		//The bar was built again, so whatever was on it is gone - which means the tool that is already picked has to be asked again
		lastTool = null;
	}

	//Puts the current tool's own toolbar on the bar, when the tool changes.
	private void UpdateToolExtensionToolbar()
	{
		if ( toolExtensionGroup is null )
			return;

		var currentTool = sceneView.Tools?.CurrentTool;
		var currentSubTool = sceneView.Tools?.CurrentSubTool;

		bool needsRebuild = lastTool != currentTool;

		if ( !needsRebuild && currentTool != null )
		{
			needsRebuild = currentTool.CurrentTool != currentSubTool;
		}

		if ( !needsRebuild )
			return;

		lastTool = currentTool;

		//The last tool's toolbar goes with the last tool - it makes a new one each time it is picked
		foreach ( var item in toolExtensionGroup.Items )
		{
			item.Panel?.Delete();
		}

		toolExtensionGroup.Items.Clear();

		if ( currentTool != null )
		{
			if ( currentSubTool != null && currentSubTool != currentTool && currentSubTool.CreateToolbarPanel() is { } subToolPanel )
			{
				toolExtensionGroup.Items.Add( new Item { Panel = subToolPanel } );
			}

			if ( currentTool.CreateToolbarPanel() is { } mainToolPanel )
			{
				toolExtensionGroup.Items.Add( new Item { Panel = mainToolPanel } );
			}
		}

		var anything = toolExtensionGroup.Items.Count > 0;

		toolExtensionGroup.Visible = anything;
		toolExtensionDivider.Visible = anything;

		Refresh();
	}
}
