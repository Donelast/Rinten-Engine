namespace Editor.MeshEditor;

partial class BooleanTool
{
	public override Panel CreateToolSidebarPanel() => new BooleanToolPanel( this );

	public class BooleanToolPanel : ToolSettingsPanel
	{
		readonly BooleanTool _tool;

		public BooleanToolPanel( BooleanTool tool )
		{
			_tool = tool;

			AddTitle( "Boolean Tool", "difference" );

			var operation = AddButtons( AddGroup( "Operation" ), 3 );

			AddMode( operation, "Union", "join_full", BooleanMode.Union );
			AddMode( operation, "Subtract", "join_left", BooleanMode.Subtract );
			AddMode( operation, "Intersect", "join_inner", BooleanMode.Intersect );

			var selection = AddGroup( "Selection" ).AddRow();

			var swap = AddTextButton( selection, "Swap A \u2194 B", null, () => _tool.SwapSelection() );
			swap.Tooltip = "Swap which object is A and which is B (Subtract only)";
			swap.EnabledWhen = () => _tool.Mode == BooleanMode.Subtract;

			//A property row rather than a bare checkbox: the same switch the inspector draws, which is the one the editor is dressed for.
			var options = AddGroup( "Options" );

			AddSheet( options.Content ).AddPropertyRow( _tool.GetSerialized().GetProperty( nameof( DeleteOtherMesh ) ) );

			var row = AddRow();

			AddTextButton( row, "Apply", "done", Apply ).EnabledWhen = () => _tool.CanApply;
			AddTextButton( row, "Cancel", "close", Cancel );

			AddStretch();
		}

		//One of the three ways two meshes can be put together, lit while it is the one.
		void AddMode( ButtonGridPanel grid, string title, string icon, BooleanMode mode )
		{
			AddButton( grid, title, icon, null, () => _tool.Mode = mode, active: () => _tool.Mode == mode );
		}

		[Shortcut( "mesh.boolean-apply", "enter", typeof( SceneViewPanel ) )]
		void Apply() => _tool.Apply();

		[Shortcut( "mesh.boolean-cancel", "ESC", typeof( SceneViewPanel ) )]
		void Cancel() => _tool.Cancel();
	}
}
