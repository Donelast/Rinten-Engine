using System;
using System.Runtime;

namespace Editor;

//Compacts the heap when the user is idle and enough is fragmented, then trims native memory
internal static class IdleReclaim
{
	//Off with -noidlereclaim .
	private static readonly bool Enabled = !Rinten.Utility.CommandLine.HasSwitch( "-noidlereclaim" );

	//On in a measured run only with -perfidlereclaim : a run that times frames must
	//not have a collection land in one because nobody touched the keyboard during it.
	private static readonly bool InMeasuredRuns = Rinten.Utility.CommandLine.HasSwitch( "-perfidlereclaim" );

	//How long nobody has to have touched anything, in seconds.
	private const double Quiet = 2.0;

	//The least the holes have to come to, and their least share of the heap.
	private const long LeastHoles = 64L * 1024 * 1024;
	private const double LeastShare = 0.2;

	//How often the heap is asked about, and the least time between two of these.
	private const float AskEvery = 1.0f;
	private const float AtMostEvery = 30.0f;

	private static RealTimeSince sinceAsked;
	private static RealTimeSince sinceRan = AtMostEvery;

	//How many times it has run, for the perf report and the log.
	internal static int Ran { get; private set; }

	[EditorEvent.Frame]
	private static void Frame()
	{
		if ( !Enabled ) return;
		if ( sinceAsked < AskEvery ) return;

		sinceAsked = 0;

		if ( sinceRan < AtMostEvery ) return;
		if ( PerfScenario.Measuring && !InMeasuredRuns ) return;
		if ( Game.IsPlaying ) return;
		if ( Rinten.CoreWindow.SecondsSinceInput < Quiet ) return;

		//What the last collection found, of whatever kind - so the holes are the ones it saw, which is as current as anything short of collecting to find out.
		var info = GC.GetGCMemoryInfo();
		var holes = info.FragmentedBytes;

		if ( holes < LeastHoles || holes < info.HeapSizeBytes * LeastShare ) return;

		Reclaim( holes, info.HeapSizeBytes );
	}

	//The same, now, whatever the editor is doing - for something about to start
	//beside the editor that needs the memory it holds (the web module's compiler).
	internal static void Now()
	{
		var info = GC.GetGCMemoryInfo();
		Reclaim( info.FragmentedBytes, info.HeapSizeBytes );
	}

	private static void Reclaim( long holes, long heapBefore )
	{
		var setBefore = Environment.WorkingSet;
		var started = System.Diagnostics.Stopwatch.GetTimestamp();

		var previous = GCSettings.LatencyMode;

		try
		{
			//The latency mode is the thing in the way: a compacting collection asked for under SustainedLowLatency is what that mode exists to refuse.
			GCSettings.LatencyMode = GCLatencyMode.Interactive;
			GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

			GC.Collect( GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true );
		}
		finally
		{
			GCSettings.LatencyMode = previous;
		}

		Rinten.NativeHeap.Trim();

		sinceRan = 0;
		Ran++;

		var ms = ( System.Diagnostics.Stopwatch.GetTimestamp() - started ) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		var after = GC.GetGCMemoryInfo();

		//Said, because it is a stall the editor chose to take, and whoever wonders why the
		//memory dropped - or why the editor paused while they were away - should find it.
		Log.Info( $"idle reclaim: {holes >> 20} MB of holes, heap {heapBefore >> 20} -> {after.HeapSizeBytes >> 20} MB, " +
			$"working set {setBefore >> 20} -> {Environment.WorkingSet >> 20} MB, {ms:F0} ms" );
	}
}
