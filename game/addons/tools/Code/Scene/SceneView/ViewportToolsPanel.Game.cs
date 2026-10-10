using Rinten.Diagnostics;

namespace Editor;

partial class ViewportToolsPanel
{
	public void UpdateViewportFromCookie() => ResolutionModes.ApplyCookie( sceneView );

	private void BuildToolbarGame( List<Group> section )
	{
		{
			var group = AddGroup( section, plain: true );

			group.Items.Add( new Item { ResolutionMenu = true } );
		}

		AddDivider( section );

		{
			var group = AddGroup( section );

			group.Items.Add( new Item { Text = () => $"{PerformanceStats.LastSecond.FrameAvg:0.00}ms" } );
		}

		{
			var group = AddGroup( section );

			group.Items.Add( new Item { Text = () => $"{(1000.0f / PerformanceStats.LastSecond.FrameAvg):0} FPS" } );
		}
	}
}
