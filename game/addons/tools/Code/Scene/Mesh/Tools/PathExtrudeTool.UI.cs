namespace Editor.MeshEditor;

partial class PathExtrudeTool
{
	public static PathExtrudeOrigin ProfileOrigin { get; set; } = PathExtrudeOrigin.ObjectLocal;
	public static bool DeleteSource { get; set; } = false;

	public override Panel CreateToolSidebarPanel() => new PathExtrudeToolPanel( this );

	public class PathExtrudeToolPanel : ToolSettingsPanel
	{
		private readonly PathExtrudeTool _tool;

		private struct PathExtrudeProperties
		{
			[Title( "Delete Source" ), WideMode]
			public readonly bool Delete { get => DeleteSource; set => DeleteSource = value; }

			[Title( "Profile Origin" ), WideMode]
			public readonly PathExtrudeOrigin Origin { get => ProfileOrigin; set => ProfileOrigin = value; }
		}

		[InlineEditor( Label = false )]
		readonly PathExtrudeProperties _properties = new();

		public PathExtrudeToolPanel( PathExtrudeTool tool )
		{
			_tool = tool;

			AddTitle( "Path Extrude", "route" );

			var group = AddGroup( "Properties" );

			var serialized = this.GetSerialized();
			serialized.OnPropertyChanged += _ => _tool.UpdateMesh();

			AddSheet( group.Content ).AddPropertyRow( serialized.GetProperty( nameof( _properties ) ) );

			var row = group.AddRow();

			AddTextButton( row, "Apply", "done", () => _tool.Apply() );
			AddTextButton( row, "Cancel", "close", () => _tool.Cancel() );

			AddStretch();

			_tool.UpdateMesh();
		}

		[Shortcut( "mesh.path-extrude-apply", "enter", typeof( SceneViewPanel ) )]
		private void ApplyShortcut() => _tool.Apply();

		[Shortcut( "mesh.path-extrude-cancel", "ESC", typeof( SceneViewPanel ) )]
		private void CancelShortcut() => _tool.Cancel();
	}
}
