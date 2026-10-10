using Rinten;
using Rinten.Diagnostics;

namespace Editor;

/// <summary>Profiler dock: CPU tree from FrameProfiler plus GPU zones, draws and memory read a frame late</summary>
[Dock( "Editor", "Profiler", "speed", DockArea.Bottom )]
public class ProfilerDock : PanelDockHost
{
	//Off, recording follows the dock: a tab behind another one costs nothing. On, the picture is already warm when the tab comes back - which is what somebody watching a stall that only happens elsewhere wants.
	/// <summary>Whether the profiler keeps recording while no dock is looking at it.</summary>
	[ConVar( "profiler_record_always", Help = "Keep recording frame profiler spans while the Profiler dock is hidden" )]
	public static bool RecordAlways { get; set; }

	public ProfilerDock( Widget parent ) : base( parent )
	{
		MinimumSize = new Vector2( 500, 200 );

		SetContent( new ProfilerPanel() );

		FrameProfiler.Enabled = true;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		FrameProfiler.Enabled = false;
	}

	//Recording costs everything in the engine a branch per scope and the main thread two timestamps; a dock nobody can see should not be asking for it.
	[EditorEvent.Frame]
	public void OnEditorFrame()
	{
		FrameProfiler.Enabled = RecordAlways || Visible;
	}
}
