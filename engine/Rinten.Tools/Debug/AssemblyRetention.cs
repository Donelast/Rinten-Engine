using Editor.Mcp;
using Microsoft.Diagnostics.Runtime;
using Rinten.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Editor;

//Checks whether hotload-replaced assemblies really unloaded, and walks what still holds them
[SkipHotload]
internal static class AssemblyRetention
{
	//How far the walk will chase a reference chain before giving up on a path. Deep enough
	//for a static field to a component to a delegate, short enough that a report is readable.
	private const int MaxPathDepth = 64;

	//Objects visited before the walk gives up, to bound its own heap.
	private const int MaxVisited = 12_000_000;

	//The type whose surviving instances are the leak itself.
	private const string ContextType = "Rinten.Internal.IsolatedAssemblyContext";

	//Collects properly, then counts the contexts that were asked to unload and did not. Twice
	//with the finalizers in between, or the answer is a list of things that are already dead.
	private static void Collect()
	{
		GC.Collect( GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true );
		GC.WaitForPendingFinalizers();
		GC.Collect( GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true );
	}

	[ConCmd( "hotload_leaks", ConVarFlags.Protected, Help = "Assemblies replaced by a hotload that never actually unloaded" )]
	public static void LeaksCommand()
	{
		Log.Info( Census() );
	}

	[McpTool.ReadOnly( "hotload_leaks" )]
	[Description( "Assemblies that a hotload replaced and that never actually unloaded, after a full collection. Each one still resident is that assembly's metadata, jitted code and objects held for the life of the process. Cheap - no heap walk." )]
	public static string Leaks() => Census();

	private static string Census()
	{
		var before = Environment.WorkingSet;
		var timer = Stopwatch.StartNew();

		Collect();

		var contexts = LoadContext.Retired();
		var live = 0;

		var text = new StringBuilder();

		text.AppendLine( "retired assembly load contexts" );

		//Same-named contexts read as one line: what matters is how many copies of one assembly are still here, not which recompile each came from.
		var byName = new SortedDictionary<string, (int Total, int Alive, string Versions)>( StringComparer.Ordinal );

		foreach ( var context in contexts )
		{
			if ( context.Alive ) live++;

			var entry = byName.GetValueOrDefault( context.Name );

			entry.Total++;

			if ( context.Alive )
			{
				entry.Alive++;
				entry.Versions = string.IsNullOrEmpty( entry.Versions )
					? context.Version?.ToString()
					: $"{entry.Versions} {context.Version}";
			}

			byName[context.Name] = entry;
		}

		foreach ( var (name, entry) in byName )
		{
			text.AppendLine( entry.Alive == 0
				? $"  {name}: {entry.Total} retired, all freed"
				: $"  {name}: {entry.Total} retired, {entry.Alive} STILL RESIDENT ({entry.Versions})" );
		}

		if ( contexts.Count == 0 )
			text.AppendLine( "  none yet - nothing has been hotloaded" );

		text.AppendLine( $"{live} of {contexts.Count} retired contexts still resident" );
		text.AppendLine( Memory() );
		text.AppendLine( $"reclaim: asked {Rinten.NativeHeap.ReclaimAsked}, ran {Rinten.NativeHeap.ReclaimRan}, " +
			$"baseline {Rinten.NativeHeap.ReclaimBaseline / Megabyte:N0} MB, " +
			$"growth {Rinten.NativeHeap.LastGrowth / Megabyte:N0} MB" );
		text.AppendLine( $"(working set was {before / Megabyte:N0} MB before collecting, {timer.Elapsed.TotalSeconds:F1}s)" );

		return text.ToString();
	}

	private const long Megabyte = 1024 * 1024;

	//The three numbers that separate a managed leak from a heap nobody gave back from memory that was never the collector's
	//to begin with: what is live, what the collector holds committed for itself, and what the process has resident.
	private static string Memory()
	{
		var info = GC.GetGCMemoryInfo();

		return $"live {GC.GetTotalMemory( false ) / Megabyte:N0} MB, " +
			$"heap {info.HeapSizeBytes / Megabyte:N0} MB " +
			$"({info.FragmentedBytes / Megabyte:N0} MB fragmented), " +
			$"committed {info.TotalCommittedBytes / Megabyte:N0} MB, " +
			$"working set {Environment.WorkingSet / Megabyte:N0} MB";
	}

	[ConCmd( "hotload_reclaim", ConVarFlags.Protected, Help = "Compact the heap and hand what it is not using back to the system" )]
	public static void ReclaimCommand()
	{
		Log.Info( Reclaim() );
	}

	[McpTool( "hotload_reclaim" )]
	[Description( "Compacts the managed heap once and hands back what the collector was holding but not using, reporting the memory before and after. Stalls the editor for a few seconds - this is the expensive collection the game's latency mode normally forbids." )]
	public static string ReclaimTool() => Reclaim();

	//On-demand compacting GC that SustainedLowLatency otherwise never runs, to measure what it frees
	private static string Reclaim()
	{
		var before = Memory();
		var timer = Stopwatch.StartNew();

		var previous = System.Runtime.GCSettings.LatencyMode;

		try
		{
			System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Interactive;
			System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;

			GC.Collect( GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true );
			GC.WaitForPendingFinalizers();
			GC.Collect( GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true );
		}
		finally
		{
			System.Runtime.GCSettings.LatencyMode = previous;
		}

		var collected = Memory();
		var collectedFor = timer.Elapsed;

		timer.Restart();

		var trimmed = Trim();

		return $"before:    {before}\n" +
			$"collected: {collected} ({collectedFor.TotalSeconds:F1}s)\n" +
			$"trimmed:   {Memory()} ({trimmed}, {timer.Elapsed.TotalSeconds:F2}s)";
	}

	//Hands the C runtime's free memory back to the system - the other half of a compile's cost, and
	//the half the collector cannot reach. See Rinten.NativeHeap, which is also what the hotload calls.
	private static string Trim() => Rinten.NativeHeap.Trim() ? "released" : "nothing to release";

	[ConCmd( "hotload_retention", ConVarFlags.Protected, Help = "What is holding the assemblies a hotload failed to unload" )]
	public static void RetentionCommand()
	{
		_ = RetentionAsync().ContinueWith( x => Log.Info( x.Result ), TaskScheduler.Default );
	}

	[McpTool.ReadOnly( "hotload_retention" )]
	[Description( "Walks the heap and reports the shortest chain from a GC root to each assembly load context that a hotload failed to unload - the reference actually keeping the old assembly alive. Freezes the editor for a few seconds; ask for it, do not poll it." )]
	public static Task<string> Retention() => RetentionAsync();

	private static async Task<string> RetentionAsync()
	{
		Collect();

		if ( LoadContext.LiveRetiredCount == 0 )
			return "Nothing to chase: every retired context was freed.";

		try
		{
			return await Task.Run( Walk );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Retention walk failed: {e.Message}" );

			return $"Retention walk failed: {e.Message}";
		}
	}

	//Breadth-first from the roots, remembering how each object was reached, and stopping at the surviving contexts. Breadth-first because the shortest
	//chain is the one worth reading - the field somebody forgot to clear is near a root, and the hundred objects hanging off it are not the fault.
	private static string Walk()
	{
		var timer = Stopwatch.StartNew();
		var text = new StringBuilder();

		using var target = DataTarget.CreateSnapshotAndAttach( Environment.ProcessId );
		using var runtime = CreateRuntime( target );

		if ( runtime is null )
			return "No CLR found in the snapshot.";

		var heap = runtime.Heap;

		//The contexts we are looking for. An unloaded one still on the heap is the leak; a loaded one is the assembly currently in use and is not.
		var wanted = new HashSet<ulong>();

		foreach ( var obj in heap.EnumerateObjects() )
		{
			if ( !obj.IsValid || obj.IsFree ) continue;
			if ( obj.Type?.Name != ContextType ) continue;

			wanted.Add( obj.Address );
		}

		if ( wanted.Count == 0 )
			return "No isolated contexts on the heap at all - the snapshot missed them.";

		//How each object was first reached, and from which root. Only ever written once per object, so what comes out is the shortest chain.
		var cameFrom = new Dictionary<ulong, ulong>( 1 << 20 );
		var rootOf = new Dictionary<ulong, string>();
		var queue = new Queue<ulong>( 1 << 16 );

		foreach ( var root in heap.EnumerateRoots() )
		{
			var address = root.Object.Address;

			if ( address == 0 || cameFrom.ContainsKey( address ) ) continue;

			cameFrom[address] = 0;
			rootOf[address] = $"{root.RootKind}";
			queue.Enqueue( address );
		}

		var found = new List<(ulong Address, int Depth)>();
		var truncated = false;

		while ( queue.Count > 0 && found.Count < wanted.Count )
		{
			var address = queue.Dequeue();

			if ( wanted.Contains( address ) )
			{
				found.Add( (address, Depth( cameFrom, address )) );
				continue;
			}

			if ( cameFrom.Count > MaxVisited )
			{
				truncated = true;
				break;
			}

			var obj = heap.GetObject( address );
			if ( !obj.IsValid || obj.IsFree ) continue;

			foreach ( var reference in obj.EnumerateReferences( carefully: true ) )
			{
				var next = reference.Address;

				if ( next == 0 || !cameFrom.TryAdd( next, address ) ) continue;

				queue.Enqueue( next );
			}
		}

		text.AppendLine( $"{wanted.Count} unfreed context{( wanted.Count == 1 ? "" : "s" )} on the heap, " +
			$"{found.Count} reached from a root in {timer.Elapsed.TotalSeconds:F1}s" +
			( truncated ? $" (gave up after {MaxVisited:N0} objects)" : "" ) );

		foreach ( var (address, depth) in found )
		{
			text.AppendLine();
			text.AppendLine( $"held at depth {depth}:" );

			foreach ( var line in Chain( heap, cameFrom, rootOf, address ) )
				text.AppendLine( $"  {line}" );
		}

		if ( found.Count < wanted.Count )
		{
			text.AppendLine();
			text.AppendLine( $"{wanted.Count - found.Count} were not reached from any root - " +
				"they are held by something the walk cannot see (a handle, a stack frame, " +
				"or native code) rather than by a managed field." );
		}

		return text.ToString();
	}

	private static int Depth( Dictionary<ulong, ulong> cameFrom, ulong address )
	{
		var depth = 0;

		while ( cameFrom.TryGetValue( address, out var parent ) && parent != 0 && depth < MaxPathDepth )
		{
			address = parent;
			depth++;
		}

		return depth;
	}

	//The chain root-first, as type names.
	private static List<string> Chain( ClrHeap heap, Dictionary<ulong, ulong> cameFrom,
		Dictionary<ulong, string> rootOf, ulong address )
	{
		var chain = new List<string>();

		for ( var depth = 0; depth < MaxPathDepth; depth++ )
		{
			var obj = heap.GetObject( address );

			chain.Add( obj.Type?.Name ?? "‹unknown›" );

			if ( !cameFrom.TryGetValue( address, out var parent ) || parent == 0 )
			{
				chain.Add( $"‹root: {rootOf.GetValueOrDefault( address, "unknown" )}›" );
				break;
			}

			address = parent;
		}

		chain.Reverse();

		var lines = new List<string>( chain.Count );

		for ( var i = 0; i < chain.Count; i++ )
			lines.Add( $"{new string( ' ', i )}{( i == 0 ? "" : "↳ " )}{chain[i]}" );

		return lines;
	}

	private static ClrRuntime CreateRuntime( DataTarget target )
	{
		foreach ( var version in target.ClrVersions )
			return version.CreateRuntime();

		return null;
	}
}
