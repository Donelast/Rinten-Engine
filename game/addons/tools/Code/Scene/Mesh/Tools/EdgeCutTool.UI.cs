namespace Editor.MeshEditor;

partial class EdgeCutTool
{
	public override Panel CreateToolSidebarPanel() => new EdgeCutToolPanel( this );

	public class EdgeCutToolPanel : ToolSettingsPanel
	{
		private readonly EdgeCutTool _tool;

		public EdgeCutToolPanel( EdgeCutTool tool )
		{
			_tool = tool;

			AddTitle( "Edge Cut Tool", "polyline" );

			var group = AddGroup( "Loop Mode" );

			AddSheet( group.Content ).AddPropertyRow( tool.GetSerialized().GetProperty( nameof( EdgeCutTool.LoopMode ) ) );

			var row = AddRow();

			AddTextButton( row, "Apply", "done", Apply ).Tooltip = "[Apply " + EditorShortcuts.GetKeys( "mesh.edge-cut-apply" ) + "]";
			AddTextButton( row, "Cancel", "close", Cancel ).Tooltip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.edge-cut-cancel" ) + "]";

			AddShortcuts(
				("Apply Cut", "Enter"),
				("Cancel Cut", "Esc"),
				("Toggle Loop Mode", "V")
			);

			AddStretch();
		}

		[Shortcut( "mesh.edge-cut-apply", "enter", typeof( SceneViewPanel ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.edge-cut-cancel", "ESC", typeof( SceneViewPanel ) )]
		void Cancel() => _tool.Cancel();

		[Shortcut( "mesh.edge-cut-loop", "V", typeof( SceneViewPanel ) )]
		void ToggleLoopMode() => _tool.ToggleLoopMode();
	}
}
