namespace Editor;

/// <summary>What is being allocated while the recorder is running, and from where.</summary>
[Dock( "Editor", "Allocations", "timer", DockArea.Bottom )]
public class Allocations : PanelDockHost
{
	public Allocations( Widget parent ) : base( parent )
	{
		MinimumSize = 200;

		SetContent( new AllocationsPanel() );
	}
}
