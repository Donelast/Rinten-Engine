namespace Editor.MeshEditor;

partial class MirrorTool
{
	public override Panel CreateToolSidebarPanel() => new MirrorToolPanel( this );

	public class MirrorToolPanel : ToolSettingsPanel
	{
		readonly MirrorTool _tool;

		public MirrorToolPanel( MirrorTool tool )
		{
			_tool = tool;

			AddTitle( "Mirror Tool", "flip" );

			var row = AddRow();

			AddTextButton( row, "Apply", "done", Apply ).EnabledWhen = () => _tool.CanApply;
			AddTextButton( row, "Cancel", "close", Cancel );

			AddShortcuts(
				("Draw Mirror Line", "LMB Drag"),
				("Move Line / Handles", "LMB Drag"),
				("Apply Mirror", EditorShortcuts.GetKeys( "mesh.mirror-apply" )),
				("Cancel", EditorShortcuts.GetKeys( "mesh.mirror-cancel" ))
			);

			AddStretch();
		}

		[Shortcut( "mesh.mirror-apply", "enter", typeof( SceneViewPanel ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.mirror-cancel", "ESC", typeof( SceneViewPanel ) )]
		void Cancel() => _tool.Cancel();
	}
}
