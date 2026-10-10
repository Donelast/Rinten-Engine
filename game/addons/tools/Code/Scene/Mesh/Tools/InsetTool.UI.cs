namespace Editor.MeshEditor;

partial class InsetTool
{
	public static float InsetAmount { get; set; } = 8.0f;
	public static int InsetSteps { get; set; } = 0;

	public override Panel CreateToolSidebarPanel() => new InsetToolPanel( this );

	public class InsetToolPanel : ToolSettingsPanel
	{
		private readonly InsetTool _tool;

		private struct InsetProperties
		{
			[Title( "Amount" ), Range( -256.0f, 256.0f, true, false ), Step( 2.0f ), WideMode]
			public readonly float Amount { get => InsetAmount; set => InsetAmount = value; }

			[Title( "Steps" ), Range( 0, 32 ), Step( 1 ), WideMode]
			public readonly int Steps { get => InsetSteps; set => InsetSteps = value; }
		}

		[InlineEditor( Label = false )]
		readonly InsetProperties _insetProperties = new();

		public InsetToolPanel( InsetTool tool )
		{
			_tool = tool;

			AddTitle( "Inset Tool", "filter_center_focus" );

			var group = AddGroup( "Properties" );

			var serialized = this.GetSerialized();
			serialized.OnPropertyChanged += _ => UpdateMesh();

			AddSheet( group.Content ).AddPropertyRow( serialized.GetProperty( nameof( _insetProperties ) ) );

			var row = group.AddRow();

			AddTextButton( row, "Apply", "done", Apply ).Tooltip = "[Apply " + EditorShortcuts.GetKeys( "mesh.inset-apply" ) + "]";
			AddTextButton( row, "Cancel", "close", Cancel ).Tooltip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.inset-cancel" ) + "]";

			AddStretch();

			UpdateMesh();
		}

		void UpdateMesh()
		{
			_tool.UpdateInset( InsetAmount, InsetSteps );
		}

		[Shortcut( "mesh.inset-apply", "enter", ShortcutType.Application )]
		void Apply()
		{
			_tool.Apply();
		}

		[Shortcut( "mesh.inset-cancel", "ESC", ShortcutType.Application )]
		void Cancel()
		{
			_tool.Cancel();
		}

		[Shortcut( "mesh.inset-increase", "]", typeof( SceneViewPanel ) )]
		void IncreaseAmount()
		{
			InsetAmount = MathF.Min( 256.0f, InsetAmount + 1.0f );
			UpdateMesh();
		}

		[Shortcut( "mesh.inset-decrease", "[", typeof( SceneViewPanel ) )]
		void DecreaseAmount()
		{
			InsetAmount = MathF.Max( -256.0f, InsetAmount - 1.0f );
			UpdateMesh();
		}
	}
}
