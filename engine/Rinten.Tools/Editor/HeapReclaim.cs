using System;
using System.Runtime;

namespace Editor;

//One compacting GC after the startup compile to release Roslyn garbage SustainedLowLatency keeps
internal static class HeapReclaim
{
	//Off with -noreclaim , to measure what it is worth.
	private static readonly bool Enabled =
		!Rinten.Utility.CommandLine.HasSwitch( "-noreclaim" );

	//When to collect, or zero for not pending. A short wait rather than straight away: what follows a compile is the hotload swapping assemblies
	//over and the type library registering them, and collecting in the middle of that would walk a heap that is about to churn anyway.
	private static RealTimeUntil settleIn;
	private static bool pending;

	//Once a session, because the compiler's state is only built once.
	private static bool done;

	[Event( "compile.complete" )]
	private static void OnCompiled( object group )
	{
		if ( !Enabled || done ) return;

		pending = true;
		settleIn = 3.0f;
	}

	[EditorEvent.Frame]
	private static void Frame()
	{
		if ( !pending || settleIn > 0 ) return;

		pending = false;
		done = true;

		Reclaim();
	}

	private static void Reclaim()
	{
		var before = GC.GetTotalMemory( false );
		var setBefore = System.Environment.WorkingSet;

		var previous = GCSettings.LatencyMode;

		try
		{
			//The latency mode is the thing in the way, so it is what is put aside - asking for a blocking compacting collection under
			//SustainedLowLatency is asking for something the mode exists to refuse, and the runtime is within its rights to do nothing.
			GCSettings.LatencyMode = GCLatencyMode.Interactive;
			GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

			GC.Collect( 2, GCCollectionMode.Aggressive, blocking: true, compacting: true );

			//Twice, with the finalisers in between: the first pass queues whatever has one, and what they release is only free on the second.
			GC.WaitForPendingFinalizers();
			GC.Collect( 2, GCCollectionMode.Aggressive, blocking: true, compacting: true );
		}
		finally
		{
			GCSettings.LatencyMode = previous;
		}

		//The collection above only moves the managed half. The compiler's native allocation is free by now but still
		//held by the C runtime, and this is what actually hands it back - for a fraction of the collection's cost.
		Rinten.NativeHeap.Trim();

		var after = GC.GetTotalMemory( false );
		var setAfter = System.Environment.WorkingSet;

		//Kept for the perf report to say, rather than said here: a reclaim is something the editor
		//does on its own and a line about it in every run's log is a number nobody asked for.
		Rinten.Diagnostics.PerfReport.Reclaimed =
			$"managed {before / ( 1024 * 1024 )} -> {after / ( 1024 * 1024 )} MB, " +
			$"working set {setBefore / ( 1024 * 1024 )} -> {setAfter / ( 1024 * 1024 )} MB";
	}
}
