namespace Editor;

partial class ViewportToolsPanel
{
	private void BuildToolbarScene( List<Group> section )
	{
		//Tool mode picker first, since the rest of the bar and sidebar belong to that mode
		AddGroup( section, plain: true ).Items.Add( new Item { Panel = new ToolModePickerPanel() } );

		{
			var group = AddGroup( section );

			//First on the bar, because what it turns off is the first thing down the side of the
			//view: the tool strips and the palette under them, all of it - see MeshToolsSwitch.
			AddToggleButton(
				group,
				"Tool Strips",
				() => "handyman",
				() => MeshEditor.MeshToolsSwitch.Shown,
				( v ) => MeshEditor.MeshToolsSwitch.Shown = v
			);
		}

		{
			var group = AddGroup( section );

			AddToggleButton(
				group,
				"Global Space",
				() => "public",
				() => EditorScene.GizmoSettings.GlobalSpace,
				( v ) => EditorScene.GizmoSettings.GlobalSpace = v
			);
		}

		{
			var group = AddGroup( section );

			AddToggleButton(
				group,
				"Draw Gizmos",
				() => "touch_app",
				() => EditorScene.GizmoSettings.GizmosEnabled,
				( v ) => EditorScene.GizmoSettings.GizmosEnabled = v
			);

			//Half width - it only opens the menu belonging to the button beside it
			group.Items.Add( new Item { GizmoSettingsMenu = true } );
		}

		{
			var group = AddGroup( section );

			AddToggleButton(
				group,
				"Angle Snap",
				() => "rotate_90_degrees_cw",
				() => EditorScene.GizmoSettings.SnapToAngles,
				( v ) => EditorScene.GizmoSettings.SnapToAngles = v
			);

			{
				group.Items.Add( new Item { SnapStep = SnapStepKind.Angle, ToolTip = "Angle Step" } );
			}
		}

		{
			var group = AddGroup( section );

			AddToggleButton(
				group,
				"Grid Snap",
				() => "grid_on",
				() => EditorScene.GizmoSettings.SnapToGrid,
				( v ) => EditorScene.GizmoSettings.SnapToGrid = v
			);

			{
				group.Items.Add( new Item { SnapStep = SnapStepKind.Grid, ToolTip = "Grid Step (cm)" } );
			}
		}
	}

	//What a click in the viewport picks - objects, faces, edges, vertices - used to start this row. It is the column down the side of the view now,
	//with what each mode can do beside it - see ToolSidebarPanel - and what is left here is what applies to any scene, whatever you are doing to it.
}
