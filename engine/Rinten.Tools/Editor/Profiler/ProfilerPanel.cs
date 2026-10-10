using Rinten;
using Rinten.Diagnostics;
using Rinten.UI;
using System.Collections.Generic;

namespace Editor;

//Profiler content: line set rebuilt twice a second; visible text refreshed more often, if changed
internal sealed class ProfilerPanel : EditorPanel
{
	//How often the lines are worked out again, in seconds.
	private const float RebuildInterval = 0.5f;

	/// <summary>How often the numbers on screen are read again. Ten a second is as fast as a changing number can be read at all, and every beat costs a text raster for each value that moved.</summary>
	[ConVar( "profiler_refresh_hz", Help = "How often the profiler dock reads its numbers again, per second" )]
	public static float RefreshHz { get; set; } = 10.0f;

	private readonly ProfilerRows rows = new();

	//Line ids the user folded shut. Ids are stable across frames.
	private readonly HashSet<int> collapsed = new();

	private readonly ProfilerListPanel list;
	private readonly ProfilerHeaderPanel header;
	private readonly ProfilerRowPanel pinned;
	private readonly TextField search;

	private readonly PanelToolbarButton[] thresholds = new PanelToolbarButton[3];

	private readonly PanelStat frameStat;
	private readonly PanelStat allocStat;
	private readonly PanelStat gcStat;
	private readonly PanelStat gpuStat;
	private readonly PanelStat drawsStat;
	private readonly PanelStat trisStat;
	private readonly PanelStat vramStat;
	private readonly PanelStat droppedStat;

	private CoreRenderDevice.FrameProfile? gpu;

	private bool paused;
	private float minMs;
	private ProfilerSort sort = ProfilerSort.Time;

	private int pinnedLine = -1;
	private int pinnedVersion = -1;

	private RealTimeSince rebuildTimer;
	private RealTimeSince refreshTimer;

	private static readonly float[] Thresholds = [0.0f, 0.05f, 0.2f];
	private static readonly string[] ThresholdNames = ["all", "> 0.05 ms", "> 0.2 ms"];

	public ProfilerPanel()
	{
		AddClass( "profiler" );

		EditorStyles.Apply( this, "ProfilerPanel.cs.scss" );

		var toolbar = AddChild<PanelToolbar>();

		toolbar.AddToggle( "pause", "Freeze the picture to read it", false, on => paused = on );
		toolbar.AddToggle( "widgets",
			"A line per component type inside Update - the exact sources, at a small recording cost",
			FrameProfiler.PerComponent, on => FrameProfiler.PerComponent = on );

		toolbar.AddSeparator();

		for ( var at = 0; at < Thresholds.Length; ++at )
		{
			var value = Thresholds[at];

			thresholds[at] = toolbar.AddText( ThresholdNames[at], "Hide anything cheaper than this", () => SetThreshold( value ) );
		}

		toolbar.AddSeparator();

		toolbar.AddIcon( "unfold_less", "Fold everything to the top level", CollapseAll );
		toolbar.AddIcon( "unfold_more", "Unfold everything", () => { collapsed.Clear(); Rebuild(); } );

		toolbar.AddStretch();

		search = toolbar.AddChild<TextField>();
		search.Placeholder = "search";
		search.Icon = "search";
		search.ValueChanged = _ => Rebuild();

		header = AddChild<ProfilerHeaderPanel>();
		header.Picked = SetSort;

		var body = AddChild<Panel>( "body" );

		list = new ProfilerListPanel( rows, collapsed ) { Pressed = OnLinePressed };
		body.AddChild( list );

		//The heading of whatever half is being read, kept where it can be read. Deaf to the pointer, because it is lying over a line that is not.
		pinned = body.AddChild<ProfilerRowPanel>( "pinned" );

		var stats = AddChild<PanelStatsBar>();

		frameStat = stats.AddStat( "frame" );
		allocStat = stats.AddStat( "alloc" );
		gcStat = stats.AddStat( "gc" );
		gpuStat = stats.AddStat( "gpu" );
		drawsStat = stats.AddStat( "draws" );
		trisStat = stats.AddStat( "tris" );
		vramStat = stats.AddStat( "vram" );
		droppedStat = stats.AddStat( "dropped" );

		SetThreshold( 0.0f );
		header.SetSort( sort );

		Rebuild();
		UpdateStats();
	}

	public override void Tick()
	{
		base.Tick();

		if ( !IsVisible ) return;

		if ( !paused && rebuildTimer > RebuildInterval )
		{
			rebuildTimer = 0.0f;

			Rebuild();
		}

		if ( !paused && refreshTimer > 1.0f / System.Math.Clamp( RefreshHz, 1.0f, 60.0f ) )
		{
			refreshTimer = 0.0f;

			list.Refresh();
			UpdateStats();
		}

		UpdatePinned();
	}

	//Works out which lines there are, and tells the list only when they moved.
	private void Rebuild()
	{
		gpu = CoreRenderDevice.ReadProfile( CoreRenderProfiler.LastViewport );

		if ( rows.Build( FrameProfiler.Frame, gpu, minMs, search?.Value, sort, collapsed ) )
			list.Invalidate();

		list.Refresh();
	}

	private void SetThreshold( float value )
	{
		minMs = value;

		for ( var at = 0; at < thresholds.Length; ++at )
		{
			if ( thresholds[at] is { } button ) button.IsActive = Thresholds[at] == value;
		}

		Rebuild();
	}

	private void SetSort( ProfilerSort value )
	{
		sort = value;

		header.SetSort( value );

		Rebuild();
	}

	private void CollapseAll()
	{
		collapsed.Clear();

		foreach ( var entry in FrameProfiler.Frame )
		{
			if ( entry.HasChildren ) collapsed.Add( entry.Id );
		}

		Rebuild();
	}

	private void OnLinePressed( ProfilerRow row )
	{
		if ( !row.Foldable ) return;

		if ( !collapsed.Remove( row.Entry.Id ) ) collapsed.Add( row.Entry.Id );

		Rebuild();
	}

	//Keeps the heading of the half being read at the top of the list.
	private void UpdatePinned()
	{
		var line = rows.SectionAt( list.FirstVisibleLine );

		pinned.SetClass( "hidden", line < 0 );

		if ( line < 0 ) return;
		if ( pinnedLine == line && pinnedVersion == rows.Version ) return;

		pinnedLine = line;
		pinnedVersion = rows.Version;

		pinned.Bind( rows[line], folded: false );
	}

	private void UpdateStats()
	{
		var frameMs = (float)( PerformanceStats.FrameTime * 1000.0 );

		frameStat.SetNumber( frameMs, "F2", " ms",
			frameMs > 16.7f ? PanelStatTone.Warn : PanelStatTone.Plain );

		allocStat.SetBytes( PerformanceStats.BytesAllocated,
			PerformanceStats.BytesAllocated > 256 * 1024 ? PanelStatTone.Warn : PanelStatTone.Plain );

		UpdateGc();

		var showGpu = gpu is not null;

		gpuStat.SetShown( showGpu );
		drawsStat.SetShown( showGpu );
		trisStat.SetShown( showGpu );
		vramStat.SetShown( showGpu );

		if ( gpu is { } profile )
		{
			gpuStat.SetNumber( profile.GpuMs, "F2", " ms",
				profile.GpuMs > 16.7f ? PanelStatTone.Warn : PanelStatTone.Plain );

			drawsStat.SetCount( profile.Draws );
			trisStat.SetNumber( profile.Triangles / 1000.0f, "F0", "k" );
			vramStat.SetBytes( profile.TextureBytes + profile.BufferBytes );
		}

		droppedStat.SetShown( FrameProfiler.Dropped > 0 );

		if ( FrameProfiler.Dropped > 0 ) droppedStat.SetCount( FrameProfiler.Dropped, PanelStatTone.Alarm );
	}

	//The three generations and what the collector took, as one line and no garbage.
	private void UpdateGc()
	{
		System.Span<char> buffer = stackalloc char[64];

		var written = 0;

		written += Write( buffer[written..], PerformanceStats.Gen0Collections );
		written += Write( buffer[written..], '/' );
		written += Write( buffer[written..], PerformanceStats.Gen1Collections );
		written += Write( buffer[written..], '/' );
		written += Write( buffer[written..], PerformanceStats.Gen2Collections );

		var pauseMs = (float)System.TimeSpan.FromTicks( PerformanceStats.GcPause ).TotalMilliseconds;

		written += Write( buffer[written..], ' ' );
		written += Write( buffer[written..], pauseMs );
		written += Write( buffer[written..], " ms" );

		gcStat.Set( buffer[..written], pauseMs > 1.0f ? PanelStatTone.Alarm : PanelStatTone.Plain );
	}

	private static int Write( System.Span<char> into, long value ) =>
		value.TryFormat( into, out var written, default, System.Globalization.CultureInfo.InvariantCulture ) ? written : 0;

	private static int Write( System.Span<char> into, float value ) =>
		value.TryFormat( into, out var written, "F2", System.Globalization.CultureInfo.InvariantCulture ) ? written : 0;

	private static int Write( System.Span<char> into, char value )
	{
		if ( into.IsEmpty ) return 0;

		into[0] = value;

		return 1;
	}

	private static int Write( System.Span<char> into, System.ReadOnlySpan<char> value )
	{
		if ( value.Length > into.Length ) return 0;

		value.CopyTo( into );

		return value.Length;
	}
}
