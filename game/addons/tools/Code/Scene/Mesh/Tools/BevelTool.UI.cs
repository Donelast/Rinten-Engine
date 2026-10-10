namespace Editor.MeshEditor;

partial class BevelTool
{
	public override Panel CreateToolSidebarPanel() => new BevelToolPanel( this );

	public class BevelToolPanel : ToolSettingsPanel
	{
		private readonly BevelTool _tool;

		public BevelToolPanel( BevelTool tool )
		{
			_tool = tool;

			AddTitle( "Bevel Tool", "square_foot" );

			var group = AddGroup( "Properties" );

			var serialized = _tool.GetSerialized();
			var property = serialized.GetProperty( nameof( BevelTool.Parameters ) ).GetCustomizable();
			property.AddAttribute( new DefaultValueAttribute( new BevelParameters() ) );

			serialized.OnPropertyStartEdit += _ => _tool.BeginParameterEdit();
			serialized.OnPropertyFinishEdit += OnParametersFinished;
			serialized.OnPropertyChanged += _ => _tool.UpdateBevel();

			AddSheet( group.Content ).AddPropertyRow( property );

			var row = group.AddRow();

			AddTextButton( row, "Apply", "done", Apply ).Tooltip = "[Apply " + EditorShortcuts.GetKeys( "mesh.edge-bevel-apply" ) + "]";
			AddTextButton( row, "Cancel", "close", Cancel ).Tooltip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.edge-bevel-cancel" ) + "]";

			AddStretch();

			_tool.UpdateBevel();
		}

		private void OnParametersFinished( SerializedProperty property )
		{
			_tool.CommitParameterEdit( $"Adjust Bevel {property.DisplayName}" );
		}

		[Shortcut( "mesh.edge-bevel-cancel", "ESC", typeof( SceneViewPanel ) )]
		private void Cancel()
		{
			_tool.Cancel();
		}

		[Shortcut( "mesh.edge-bevel-apply", "enter", typeof( SceneViewPanel ) )]
		private void Apply()
		{
			_tool.Apply();
		}
	}
}
