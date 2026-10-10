using Rinten;

namespace Editor;

/// <summary>Memory profiler dock: process stats bar, per-world object census, and on-demand heap census</summary>
[Dock( "Editor", "Memory", "memory", DockArea.Bottom )]
public class MemoryDock : PanelDockHost
{
	public MemoryDock( Widget parent ) : base( parent )
	{
		MinimumSize = new Vector2( 560, 200 );

		SetContent( new MemoryPanel() );
	}
}
