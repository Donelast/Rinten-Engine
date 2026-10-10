using System;
using System.Runtime.InteropServices;

namespace Rinten;

/// <summary>The half of a compile's memory the garbage collector cannot reach.</summary>
public static class NativeHeap
{
	//Set once we know whether the C runtime here has the call at all.
	private static bool unavailable;

	/// <summary>Ask the C runtime to release the memory it is holding but not using. Cheap - tens of milliseconds - and a no-op where the call doesn't exist.</summary>
	public static bool Trim()
	{
		//glibc only. Musl and the Windows and macOS runtimes have no equivalent entry point, and looking for one costs an exception every time.
		if ( unavailable || !OperatingSystem.IsLinux() ) return false;

		try
		{
			return MallocTrim( 0 ) == 1;
		}
		catch ( EntryPointNotFoundException )
		{
			unavailable = true;
		}
		catch ( DllNotFoundException )
		{
			unavailable = true;
		}

		return false;
	}

	[DllImport( "libc", EntryPoint = "malloc_trim" )]
	private static extern int MallocTrim( nuint pad );

	//The working set as it stood after the last Reclaim.
	private static long reclaimedAt;

	/// <summary>The working set the last <see cref="Reclaim"/> measured itself against.</summary>
	public static long ReclaimBaseline => reclaimedAt;

	/// <summary>Growth seen the last time <see cref="Reclaim"/> was asked.</summary>
	public static long LastGrowth { get; private set; }

	/// <summary>How many times <see cref="Reclaim"/> has been called, and has run.</summary>
	public static int ReclaimAsked { get; private set; }
	public static int ReclaimRan { get; private set; }

	/// <summary>How much the working set may grow between reclaims. Twelve hot reloads of a small project cost about half a gigabyte, so this is roughly one reclaim per dozen recompiles - and it holds the process inside a band instead of letting it climb for the length of a session.</summary>
	public const long DefaultReclaimBudget = 512L * 1024 * 1024;

	/// <summary>Full GC then trim, if the process grew past budget since last time; returns true if it ran</summary>
	public static bool Reclaim( long budget = DefaultReclaimBudget )
	{
		//Cheap, and independent of the budget: whatever a background collection has already freed goes back now rather than at the next reclaim.
		Trim();

		var current = Environment.WorkingSet;

		ReclaimAsked++;

		//Measure growth from a low-water mark, since the first reading is the startup peak
		if ( reclaimedAt == 0 || current < reclaimedAt )
		{
			reclaimedAt = current;
			return false;
		}

		LastGrowth = current - reclaimedAt;

		if ( LastGrowth < budget ) return false;

		ReclaimRan++;

		var previous = System.Runtime.GCSettings.LatencyMode;

		try
		{
			//The latency mode is the thing in the way: asking for a blocking compacting collection under SustainedLowLatency
			//is asking for the very thing that mode exists to refuse, and the runtime may simply not do it.
			System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Interactive;
			System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;

			GC.Collect( GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true );

			//Twice, with the finalizers in between: the first pass queues whatever has one, and what they release is only free on the second.
			GC.WaitForPendingFinalizers();
			GC.Collect( GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true );
		}
		finally
		{
			System.Runtime.GCSettings.LatencyMode = previous;
		}

		Trim();

		reclaimedAt = Environment.WorkingSet;

		return true;
	}
}
