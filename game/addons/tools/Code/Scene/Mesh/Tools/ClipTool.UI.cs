namespace Editor.MeshEditor;

partial class ClipTool
{
	public override Panel CreateToolSidebarPanel() => new ClipToolPanel( this );

	public class ClipToolPanel : ToolSettingsPanel
	{
		readonly ClipTool _tool;

		public ClipToolPanel( ClipTool tool )
		{
			_tool = tool;

			AddTitle( "Clipping Tool", "content_cut" );

			//Three across, so the words are the short ones - the group already says it is which side is kept.
			var keep = AddButtons( AddGroup( "Keep Mode" ), 3 );

			AddKeep( keep, "Front", "flip_to_front", ClipKeepMode.Front );
			AddKeep( keep, "Back", "flip_to_back", ClipKeepMode.Back );
			AddKeep( keep, "Both", "splitscreen", ClipKeepMode.Both );

			AddSheet().AddPropertyRow( tool.GetSerialized().GetProperty( nameof( CapNewSurfaces ) ) );

			var row = AddRow();

			var apply = AddTextButton( row, "Apply", "done", Apply );
			apply.Tooltip = "[Apply " + EditorShortcuts.GetKeys( "mesh.clip-apply" ) + "]";
			apply.EnabledWhen = () => _tool.CanApply;

			AddTextButton( row, "Cancel", "close", Cancel ).Tooltip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.clip-cancel" ) + "]";

			AddShortcuts(
				("Draw Clip Line", "LMB Drag"),
				("Cycle Keep Mode", EditorShortcuts.GetKeys( "mesh.clip-cycle-mode" )),
				("Apply", EditorShortcuts.GetKeys( "mesh.clip-apply" )),
				("Apply & Continue", EditorShortcuts.GetKeys( "mesh.clip-apply-stay" )),
				("Cancel", EditorShortcuts.GetKeys( "mesh.clip-cancel" ))
			);

			AddStretch();
		}

		//Which side of the cut survives it, lit while it is the one.
		void AddKeep( ButtonGridPanel grid, string title, string icon, ClipKeepMode mode )
		{
			AddButton( grid, title, icon, null, () => _tool.KeepMode = mode, active: () => _tool.KeepMode == mode );
		}

		[Shortcut( "mesh.clip-apply", "enter", typeof( SceneViewPanel ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.clip-apply-stay", "space", typeof( SceneViewPanel ) )]
		void ApplyAndContinue() => _tool.Apply( false );

		[Shortcut( "mesh.clip-cancel", "ESC", typeof( SceneViewPanel ) )]
		void Cancel() => _tool.Cancel();

		[Shortcut( "mesh.clip-cycle-mode", "shift+x", typeof( SceneViewPanel ) )]
		void CycleMode() => _tool.CycleMode();
	}
}
