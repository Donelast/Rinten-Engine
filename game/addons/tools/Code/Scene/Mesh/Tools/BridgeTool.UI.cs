namespace Editor.MeshEditor;

partial class BridgeTool
{
	public static int NumSteps { get; set; } = 4;
	public static int Twist { get; set; } = 0;
	public static PolygonMesh.BridgeUVMode UVMode { get; set; } = PolygonMesh.BridgeUVMode.Auto;
	public static float RepeatsU { get; set; } = 1.0f;
	public static float RepeatsV { get; set; } = 1.0f;

	public override Panel CreateToolSidebarPanel() => new BridgeToolPanel( this );

	public class BridgeToolPanel : ToolSettingsPanel
	{
		private readonly BridgeTool _tool;

		private struct BridgeProperties
		{
			[Title( "Steps" ), Range( 1, 128 ), Step( 1 ), WideMode]
			public readonly int Steps { get => NumSteps; set => NumSteps = value; }

			[Title( "Twist" ), Range( -100, 100, true, false ), Step( 1 ), WideMode]
			public readonly int TwistAmount { get => Twist; set => Twist = value; }

			[Title( "UV Mode" ), WideMode]
			public readonly PolygonMesh.BridgeUVMode UVs { get => UVMode; set => UVMode = value; }

			[Title( "Repeats U" ), Range( 0.01f, 100.0f, true, false ), Step( 0.0625f ), WideMode]
			public readonly float U { get => RepeatsU; set => RepeatsU = value; }

			[Title( "Repeats V" ), Range( 0.01f, 100.0f, true, false ), Step( 0.0625f ), WideMode]
			public readonly float V { get => RepeatsV; set => RepeatsV = value; }
		}

		[InlineEditor( Label = false )]
		readonly BridgeProperties _bridgeProperties = new();

		public BridgeToolPanel( BridgeTool tool )
		{
			_tool = tool;

			AddTitle( "Bridge Tool", "device_hub" );

			var group = AddGroup( "Properties" );

			var serialized = this.GetSerialized();
			serialized.OnPropertyChanged += _ => UpdateMesh();

			AddSheet( group.Content ).AddPropertyRow( serialized.GetProperty( nameof( _bridgeProperties ) ) );

			var row = group.AddRow();

			AddTextButton( row, "Apply", "done", Apply );
			AddTextButton( row, "Cancel", "close", Cancel );

			AddStretch();

			UpdateMesh();
		}

		void UpdateMesh()
		{
			_tool.UpdateBridge( NumSteps, Twist, UVMode, RepeatsU, RepeatsV );
		}

		private void Cancel()
		{
			_tool.Cancel();
		}

		private void Apply()
		{
			_tool.Apply();
		}

		[Shortcut( "mesh.bridge-increase", "]", typeof( SceneViewPanel ) )]
		private void IncreaseSteps()
		{
			NumSteps = Math.Min( 128, NumSteps + 1 );
			UpdateMesh();
		}

		[Shortcut( "mesh.bridge-decrease", "[", typeof( SceneViewPanel ) )]
		private void DecreaseSteps()
		{
			NumSteps = Math.Max( 1, NumSteps - 1 );
			UpdateMesh();
		}

		[Shortcut( "mesh.bridge-apply", "enter", typeof( SceneViewPanel ) )]
		private void ApplyShortcut()
		{
			Apply();
		}

		[Shortcut( "mesh.bridge-cancel", "ESC", typeof( SceneViewPanel ) )]
		private void CancelShortcut()
		{
			Cancel();
		}
	}
}
