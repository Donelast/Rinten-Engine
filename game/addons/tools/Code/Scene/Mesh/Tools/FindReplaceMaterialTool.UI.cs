namespace Editor.MeshEditor;

partial class FindReplaceMaterialTool
{
	public override Panel CreateToolSidebarPanel() => new FindReplaceMaterialPanel( this );

	public class FindReplaceMaterialPanel : ToolSettingsPanel
	{
		readonly FindReplaceMaterialTool _tool;
		readonly Rinten.UI.Label _matches;

		public FindReplaceMaterialPanel( FindReplaceMaterialTool tool )
		{
			_tool = tool;

			AddTitle( "Find / Replace Material", "find_replace" );

			var materials = AddGroup( "Materials" );

			var so = tool.GetSerialized();
			var sheet = AddSheet( materials.Content );

			sheet.AddPropertyRow( so.GetProperty( nameof( FindMaterial ) ) );
			sheet.AddPropertyRow( so.GetProperty( nameof( ReplaceMaterial ) ) );
			sheet.AddPropertyRow( so.GetProperty( nameof( Scope ) ) );

			_matches = AddGroup( "Matches" ).Content.AddChild<Rinten.UI.Label>( "note" );

			var row = AddRow();

			var replace = AddTextButton( row, "Replace", "done", Apply );
			replace.AddClass( "good" );
			replace.Tooltip = "[Replace " + EditorShortcuts.GetKeys( "mesh.find-replace-material-apply" ) + "]";
			replace.EnabledWhen = () => _tool.CanApply;

			var select = AddTextButton( row, "Select", "select_all", SelectMatches );
			select.Tooltip = "Select all matching faces [" + EditorShortcuts.GetKeys( "mesh.find-replace-material-select" ) + "]";
			select.EnabledWhen = () => _tool.CanSelect;

			AddTextButton( AddRow(), "Cancel", "close", CloseTool )
				.Tooltip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.find-replace-material-close" ) + "]";

			AddStretch();
		}

		public override void Tick()
		{
			base.Tick();

			_matches.Text = _tool.FindMaterial is null
				? $"{_tool.MatchCount} untextured faces on {_tool.MatchComponentCount} meshes"
				: $"{_tool.MatchCount} faces on {_tool.MatchComponentCount} meshes";
		}

		[Shortcut( "mesh.find-replace-material-apply", "enter", typeof( SceneViewPanel ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.find-replace-material-select", "shift+enter", typeof( SceneViewPanel ) )]
		void SelectMatches() => _tool.SelectMatches();

		[Shortcut( "mesh.find-replace-material-close", "ESC", typeof( SceneViewPanel ) )]
		void CloseTool() => _tool.Close();
	}
}
