using Rinten;
using Rinten.Diagnostics;
using Rinten.UI;
using System;
using System.Collections.Generic;

namespace Editor;

//Memory dock content: scene walk once a second, rebuilt only on change; stats bar on its own beat
internal sealed class MemoryPanel : EditorPanel
{
	//How often the worlds are walked again, in seconds.
	private const float CensusInterval = 1.0f;

	//How often the bar along the bottom is read again, in seconds.
	private const float StatsInterval = 0.25f;

	private readonly MemorySceneCensus census = new();
	private readonly MemoryRows rows = new();

	//Type names the user opened to see what holds them.
	private readonly HashSet<string> expanded = new();

	private readonly MemoryListPanel list;
	private readonly PanelListHeader header;
	private readonly MemoryRowPanel pinned;
	private readonly PanelToolbarButton baselineButton;

	private readonly PanelStat processStat;
	private readonly PanelStat heapStat;
	private readonly PanelStat committedStat;
	private readonly PanelStat generationsStat;
	private readonly PanelStat lohStat;
	private readonly PanelStat fragStat;
	private readonly PanelStat allocStat;
	private readonly PanelStat gpuStat;
	private readonly PanelStat busyStat;

	private bool showEverything;

	private int pinnedLine = -1;

	private RealTimeSince censusTimer;
	private RealTimeSince statsTimer;

	public MemoryPanel()
	{
		AddClass( "memory" );

		EditorStyles.Apply( this, "MemoryPanel.cs.scss" );

		var toolbar = AddChild<PanelToolbar>();

		toolbar.AddText( "Snapshot",
			"Walk the managed heap and total it by type. Freezes the editor for a moment.",
			() => _ = TakeSnapshot() );

		baselineButton = toolbar.AddText( "Baseline",
			"Measure growth from here. Take a snapshot, do the thing you suspect, snapshot again - what leaked is what grew.",
			ToggleBaseline );

		toolbar.AddSeparator();

		toolbar.AddIcon( "delete_sweep", "Collect everything collectable, so what is left is what is held", Collect );

		toolbar.AddSeparator();

		toolbar.AddToggle( "unfold_more", "Every type, not just the ones worth a megabyte", false,
			on => { showEverything = on; Rebuild(); } );

		header = AddChild( new PanelListHeader( "type" ) );
		header.AddColumn( "bytes" );
		header.AddColumn( "growth" );
		header.AddColumn( "count" );
		header.AddColumn( "growth" );

		var body = AddChild<Panel>( "body" );

		list = new MemoryListPanel( rows, expanded ) { Pressed = OnLinePressed };
		body.AddChild( list );

		pinned = body.AddChild<MemoryRowPanel>( "pinned" );

		var stats = AddChild<PanelStatsBar>();

		processStat = stats.AddStat( "process" );
		heapStat = stats.AddStat( "heap" );
		committedStat = stats.AddStat( "committed" );
		generationsStat = stats.AddStat( "gen0/1/2" );
		lohStat = stats.AddStat( "loh" );
		fragStat = stats.AddStat( "frag" );
		allocStat = stats.AddStat( "alloc" );
		gpuStat = stats.AddStat( "gpu" );
		busyStat = stats.AddStat( "" );

		census.Update();

		Rebuild();
		UpdateStats();
		UpdateBaselineButton();
	}

	public override void Tick()
	{
		base.Tick();

		if ( !IsVisible ) return;

		if ( censusTimer > CensusInterval )
		{
			censusTimer = 0.0f;

			//Only when it actually changed: rebuilding every second would throw away where
			//somebody had scrolled to, which is the half of this dock people read slowly.
			if ( census.Update() ) Rebuild();
		}

		if ( statsTimer > StatsInterval )
		{
			statsTimer = 0.0f;

			UpdateStats();
			UpdateBaselineButton();
		}

		UpdatePinned();
	}

	private void Rebuild()
	{
		if ( rows.Build( census.Rows, HeapCensus.Last, expanded, showEverything ) )
			list.Invalidate();
	}

	private async System.Threading.Tasks.Task TakeSnapshot()
	{
		await HeapCensus.TakeAsync();

		expanded.Clear();

		Rebuild();
	}

	private void ToggleBaseline()
	{
		if ( HeapCensus.HasBaseline ) HeapCensus.ClearBaseline();
		else HeapCensus.MarkBaseline();

		UpdateBaselineButton();
		Rebuild();
	}

	private void UpdateBaselineButton() => baselineButton.IsActive = HeapCensus.HasBaseline;

	private static void Collect()
	{
		GC.Collect( GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true );
		GC.WaitForPendingFinalizers();
	}

	private void OnLinePressed( MemoryRow row )
	{
		if ( !row.Foldable || row.Key is null ) return;

		if ( !expanded.Remove( row.Key ) )
		{
			expanded.Add( row.Key );

			//Asked the first time it is opened and kept after that: it is a second walk of the whole heap, not something to repeat on a whim.
			if ( FindRow( row.Key ) is { Referrers: null } found ) _ = FindReferrers( found );
		}

		Rebuild();
	}

	private static HeapCensus.Row FindRow( string type )
	{
		if ( HeapCensus.Last is not { } census ) return null;

		foreach ( var row in census.Rows )
		{
			if ( string.Equals( row.Type, type, StringComparison.Ordinal ) ) return row;
		}

		return null;
	}

	private async System.Threading.Tasks.Task FindReferrers( HeapCensus.Row row )
	{
		await HeapCensus.FindReferrersAsync( row );

		Rebuild();
	}

	//Keeps the heading of the half being read at the top of the list.
	private void UpdatePinned()
	{
		var line = rows.SectionAt( list.FirstVisibleLine );

		pinned.SetClass( "hidden", line < 0 );

		if ( line < 0 ) return;
		if ( pinnedLine == line ) return;

		pinnedLine = line;

		pinned.Bind( rows[line], folded: false, band: 0 );
	}

	private void UpdateStats()
	{
		var info = GC.GetGCMemoryInfo();
		var generations = info.GenerationInfo;

		processStat.SetBytes( Environment.WorkingSet );

		heapStat.SetBytes( info.HeapSizeBytes,
			info.HeapSizeBytes > 2L * 1024 * 1024 * 1024 ? PanelStatTone.Alarm : PanelStatTone.Plain );

		committedStat.SetBytes( info.TotalCommittedBytes );

		var showGenerations = generations.Length >= 4;

		generationsStat.SetShown( showGenerations );
		lohStat.SetShown( showGenerations );

		if ( showGenerations )
		{
			WriteGenerations( generations );

			lohStat.SetBytes( generations[3].SizeAfterBytes );
		}

		var fragmented = info.HeapSizeBytes > 0 ? (float)info.FragmentedBytes / info.HeapSizeBytes * 100.0f : 0.0f;

		fragStat.SetNumber( fragmented, "F1", "%", fragmented > 25.0f ? PanelStatTone.Warn : PanelStatTone.Plain );

		allocStat.SetBytes( PerformanceStats.BytesAllocated,
			PerformanceStats.BytesAllocated > 256 * 1024 ? PanelStatTone.Warn : PanelStatTone.Plain );

		gpuStat.SetBytes( (long)PerformanceStats.GpuMemoryUsage );

		busyStat.SetShown( HeapCensus.Busy );

		if ( HeapCensus.Busy ) busyStat.Set( HeapCensus.Status, PanelStatTone.Warn );
	}

	//The three generations on one line, and no garbage to read them.
	private void WriteGenerations( ReadOnlySpan<GCGenerationInfo> generations )
	{
		Span<char> buffer = stackalloc char[96];

		var written = PanelText.WriteBytes( buffer, generations[0].SizeAfterBytes );

		written += Separator( buffer[written..] );
		written += PanelText.WriteBytes( buffer[written..], generations[1].SizeAfterBytes );

		written += Separator( buffer[written..] );
		written += PanelText.WriteBytes( buffer[written..], generations[2].SizeAfterBytes );

		generationsStat.Set( buffer[..written] );
	}

	private static int Separator( Span<char> into )
	{
		if ( into.Length < 3 ) return 0;

		" / ".CopyTo( into );

		return 3;
	}
}
