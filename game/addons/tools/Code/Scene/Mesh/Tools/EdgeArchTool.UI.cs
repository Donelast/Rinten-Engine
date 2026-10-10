namespace Editor.MeshEditor;

partial class EdgeArchTool
{
	public override Panel CreateToolSidebarPanel() => new EdgeArchToolPanel( this );

	public class EdgeArchToolPanel : ToolSettingsPanel
	{
		private readonly EdgeArchTool _tool;
		private readonly SerializedObject _serialized;
		private readonly SerializedProperty.CustomizableSerializedProperty _parameters;

		public EdgeArchToolPanel( EdgeArchTool tool )
		{
			_tool = tool;

			AddTitle( "Edge Arch Tool", "timeline" );

			var group = AddGroup( "Properties" );

			_serialized = _tool.GetSerialized();
			_parameters = _serialized.GetProperty( nameof( EdgeArchTool.Parameters ) ).GetCustomizable();
			_parameters.AddAttribute( new DefaultValueAttribute( new EdgeArchParameters() ) );

			_serialized.OnPropertyStartEdit += _ => _tool.BeginParameterEdit();
			_serialized.OnPropertyFinishEdit += OnParametersFinished;
			_serialized.OnPropertyChanged += _ => _tool.UpdateArch();

			AddSheet( group.Content ).AddPropertyRow( _parameters );

			var row = group.AddRow();

			AddTextButton( row, "Apply", "done", Apply ).Tooltip = "[Apply " + EditorShortcuts.GetKeys( "mesh.edge-arch-apply" ) + "]";
			AddTextButton( row, "Cancel", "close", Cancel ).Tooltip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.edge-arch-cancel" ) + "]";

			AddStretch();

			_tool.UpdateArch();
		}

		private void OnParametersFinished( SerializedProperty property )
		{
			_tool.CommitParameterEdit( $"Adjust Edge Arch {property.DisplayName}" );
		}

		[Shortcut( "mesh.edge-arch-increase", "]", typeof( SceneViewPanel ) )]
		private void IncreaseSteps()
		{
			AdjustSteps( 1 );
		}

		[Shortcut( "mesh.edge-arch-decrease", "[", typeof( SceneViewPanel ) )]
		private void DecreaseSteps()
		{
			AdjustSteps( -1 );
		}

		private void AdjustSteps( int delta )
		{
			_tool.BeginParameterEdit();

			var parameters = _tool.Parameters;
			parameters.Steps += delta;
			_tool.Parameters = parameters;

			_tool.CommitParameterEdit( "Adjust Edge Arch Steps" );

			//The sliders don't know we've been at the keyboard
			_serialized.NoteChanged( _parameters );
			_tool.UpdateArch();
		}

		[Shortcut( "mesh.edge-arch-cancel", "ESC", typeof( SceneViewPanel ) )]
		private void Cancel()
		{
			_tool.Cancel();
		}

		[Shortcut( "mesh.edge-arch-apply", "enter", typeof( SceneViewPanel ) )]
		private void Apply()
		{
			_tool.Apply();
		}
	}
}
