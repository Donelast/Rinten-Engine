namespace Editor.MeshEditor;

partial class DisplacementTool
{
	public override Panel CreateToolSidebarPanel() => new DisplacementToolPanel( this );

	public class DisplacementToolPanel : ToolSettingsPanel
	{
		readonly Rinten.UI.Label _selectionCount;
		readonly Inspectors.ControlSheet _flattenSubMode;
		readonly DisplacementTool _tool;

		public DisplacementToolPanel( DisplacementTool tool )
		{
			_tool = tool;

			AddTitle( "Displacement Tool", "landscape" );

			var so = tool.GetSerialized();

			{
				var group = AddGroup( "Displace On" );

				AddSheet( group.Content ).AddPropertyRow( so.GetProperty( nameof( tool.LimitMode ) ) );

				_selectionCount = group.Content.AddChild<Rinten.UI.Label>( "note" );
			}
			{
				var group = AddGroup( "Mode" );

				AddSheet( group.Content ).AddPropertyRow( so.GetProperty( nameof( tool.Mode ) ) );

				//Its own sheet, because only this row comes and goes with the mode.
				_flattenSubMode = AddSheet( group.Content );
				_flattenSubMode.AddPropertyRow( so.GetProperty( nameof( tool.FlattenSubMode ) ) );
			}
			{
				var group = AddGroup( "Brush" );
				var sheet = AddSheet( group.Content );

				sheet.AddPropertyRow( so.GetProperty( nameof( tool.Radius ) ) );
				sheet.AddPropertyRow( so.GetProperty( nameof( tool.Strength ) ) );
				sheet.AddPropertyRow( so.GetProperty( nameof( tool.Hardness ) ) );
				sheet.AddPropertyRow( so.GetProperty( nameof( tool.NormalDir ) ) );
				sheet.AddPropertyRow( so.GetProperty( nameof( tool.PaintBackfacing ) ) );
				sheet.AddPropertyRow( so.GetProperty( nameof( tool.ScaleWithDistance ) ) );
			}
			{
				var group = AddGroup( "Visualization" );

				AddSheet( group.Content ).AddPropertyRow( so.GetProperty( nameof( tool.ShowVerts ) ) );
			}

			AddStretch();

			AddShortcuts(
				("Displace", "LMB"),
				("Smooth", "Shift+LMB"),
				("Invert Direction", "Ctrl+LMB"),
				("Adjust Radius", "Shift+MMB Drag"),
				("Adjust Strength", "Ctrl+MMB ↕"),
				("Adjust Hardness", "Ctrl+MMB ↔")
			);
		}

		public override void Tick()
		{
			base.Tick();

			_flattenSubMode.SetClass( "hidden", _tool.Mode != DisplaceMode.Flatten );

			ShowSelectionCount();
		}

		//How much of the mesh the brush is allowed to touch, in words.
		void ShowSelectionCount()
		{
			if ( _tool.LimitMode == DisplaceLimitMode.Everything )
			{
				_selectionCount.SetClass( "hidden", true );
				return;
			}

			var (count, name) = _tool.LimitMode switch
			{
				DisplaceLimitMode.Objects => (
				_tool._selectedMeshes.Count,
				_tool._selectedMeshes.Count == 1 ? "object" : "objects"),

				DisplaceLimitMode.Faces => (
				SelectionTool.GetAllSelected<MeshFace>().Count(),
				SelectionTool.GetAllSelected<MeshFace>().Count() == 1 ? "face" : "faces"),

				DisplaceLimitMode.Vertices => (
				SelectionTool.GetAllSelected<MeshVertex>().Count(),
				SelectionTool.GetAllSelected<MeshVertex>().Count() == 1 ? "vertex" : "vertices"),

				_ => (0, "selected")
			};

			_selectionCount.SetClass( "hidden", false );
			_selectionCount.Text = $"{count} {name} selected";
		}
	}
}
