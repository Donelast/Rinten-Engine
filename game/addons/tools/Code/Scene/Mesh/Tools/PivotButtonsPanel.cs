namespace Editor.MeshEditor;

/// <summary>The pivot buttons and the shortcuts that drive them. This is a panel of its own so that every selection mode's sidebar can host one - shortcuts resolve against a live instance of their declaring type, so putting them on a single mode's sidebar would leave them dead in the others.</summary>
public class PivotButtonsPanel : EditorPanel
{
	readonly SelectionTool _tool;

	public PivotButtonsPanel( ToolSettingsPanel sidebar, SelectionTool tool, bool enabled )
	{
		_tool = tool;

		//A column, as a group's content is. A panel is a row until told otherwise, and a row sizes the grid in it to the
		//grid's own content: four pixels of cells whose labels wrapped a letter to a line down the side of the group.
		AddClass( "column" );

		var grid = sidebar.AddButtons( this );
		Func<bool> isEnabled = () => enabled;

		sidebar.AddButton( grid, "Previous", "navigate_before", "mesh.previous-pivot", PreviousPivot, isEnabled );
		sidebar.AddButton( grid, "Next", "navigate_next", "mesh.next-pivot", NextPivot, isEnabled );
		sidebar.AddButton( grid, "Clear", "clear", "mesh.clear-pivot", ClearPivot, isEnabled );
		sidebar.AddButton( grid, "Center", "filter_center_focus", "mesh.center-pivot", CenterPivot, isEnabled );
		sidebar.AddButton( grid, "World Origin", "public", "mesh.zero-pivot", ZeroPivot, isEnabled );
	}

	[Shortcut( "mesh.previous-pivot", "Shift+MWheelDown", typeof( SceneViewPanel ) )]
	public void PreviousPivot() => _tool.Pivot.Previous();

	[Shortcut( "mesh.next-pivot", "Shift+MWheelUp", typeof( SceneViewPanel ) )]
	public void NextPivot() => _tool.Pivot.Next();

	[Shortcut( "mesh.center-pivot", "Ctrl+Home", typeof( SceneViewPanel ) )]
	public void CenterPivot() => _tool.Pivot.Center();

	[Shortcut( "mesh.clear-pivot", "Home", typeof( SceneViewPanel ) )]
	public void ClearPivot() => _tool.Pivot.Clear();

	[Shortcut( "mesh.zero-pivot", "Ctrl+End", typeof( SceneViewPanel ) )]
	public void ZeroPivot() => _tool.Pivot.Zero();
}
