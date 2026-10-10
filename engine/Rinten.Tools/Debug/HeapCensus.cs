using Microsoft.Diagnostics.Runtime;
using Rinten;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Editor;

//On-demand count of live managed objects by type, from a snapshot; costs nothing until asked
[SkipHotload]
internal static class HeapCensus
{
	/// <summary>One type's share of the heap, and how that changed since the baseline was marked.</summary>
	public sealed class Row
	{
		public string Type { get; init; }
		public long Bytes { get; init; }
		public long Count { get; init; }

		public long DeltaBytes { get; internal set; }
		public long DeltaCount { get; internal set; }

		/// <summary>What was found holding objects of this type, once asked. Null until then; empty means asked and nothing found.</summary>
		public IReadOnlyList<Referrer> Referrers { get; internal set; }
	}

	/// <summary>Something that holds objects of a type: another type, or a root.</summary>
	public sealed record Referrer( string Name, long Count );

	/// <summary>One finished walk.</summary>
	public sealed record Result(
		IReadOnlyList<Row> Rows,
		long TotalBytes,
		long TotalObjects,
		double Seconds,
		bool AgainstBaseline );

	/// <summary>The last finished census, or null.</summary>
	public static Result Last { get; private set; }

	/// <summary>Whether a walk is happening. One at a time.</summary>
	public static bool Busy { get; private set; }

	/// <summary>What the walk is doing, for somebody to put on screen.</summary>
	public static string Status { get; private set; } = "";

	//The counts a later census is compared against.
	private static Dictionary<string, (long Bytes, long Count)> baseline;

	/// <summary>Whether <see cref="TakeAsync"/> has anything to compare against.</summary>
	public static bool HasBaseline => baseline is not null;

	/// <summary>Marks the last census as the point to measure growth from. Everything after it reads as a delta, which is what actually finds a leak: absolute sizes in an editor are large and boring, the ones that grow are not.</summary>
	public static void MarkBaseline()
	{
		if ( Last is null ) return;

		baseline = new Dictionary<string, (long, long)>( Last.Rows.Count, StringComparer.Ordinal );

		foreach ( var row in Last.Rows )
			baseline[row.Type] = (row.Bytes, row.Count);

		//The census it was marked from compares against itself: all zeroes, which is the honest answer and stops the old deltas being read as new.
		ApplyBaseline( Last.Rows );

		Last = Last with { AgainstBaseline = true };
	}

	public static void ClearBaseline()
	{
		baseline = null;

		if ( Last is null ) return;

		foreach ( var row in Last.Rows )
		{
			row.DeltaBytes = 0;
			row.DeltaCount = 0;
		}

		Last = Last with { AgainstBaseline = false };
	}

	/// <summary>Walks the heap and totals it by type. Collects first, so what comes back is what is held rather than what has not been swept yet.</summary>
	public static async Task<Result> TakeAsync( bool collectFirst = true )
	{
		if ( Busy ) return Last;

		Busy = true;
		Status = "collecting…";

		try
		{
			//Resumes on the editor's thread: everything in tools is started from it and it carries a
			//synchronization context, which is what the rest of the editor's async work relies on too.
			var result = await Task.Run( () => Walk( collectFirst ) );

			if ( result is not null )
			{
				ApplyBaseline( result.Rows );
				Last = result with { AgainstBaseline = baseline is not null };
			}

			return Last;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Heap census failed: {e.Message}" );
			Status = e.Message;

			return Last;
		}
		finally
		{
			Busy = false;
		}
	}

	/// <summary>Finds what is holding a type: the types that reference it, and the roots that hold it directly. A second full walk - the heap knows what points out of an object, never what points into it - so it is asked for one type at a time, by clicking the line.</summary>
	public static async Task FindReferrersAsync( Row row )
	{
		if ( Busy || row is null ) return;

		Busy = true;
		Status = $"finding what holds {Short( row.Type )}…";

		try
		{
			var found = await Task.Run( () => WalkReferrers( row.Type ) );

			row.Referrers = found;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Referrer walk failed: {e.Message}" );
			Status = e.Message;
		}
		finally
		{
			Busy = false;
		}
	}

	private static void ApplyBaseline( IReadOnlyList<Row> rows )
	{
		foreach ( var row in rows )
		{
			if ( baseline is null )
			{
				row.DeltaBytes = 0;
				row.DeltaCount = 0;
				continue;
			}

			var (bytes, count) = baseline.GetValueOrDefault( row.Type );

			row.DeltaBytes = row.Bytes - bytes;
			row.DeltaCount = row.Count - count;
		}
	}

	//-- the walk -------------------------------------------------------------

	//Opens a frozen copy of ourselves. Windows takes it copy on write, so the editor carries on while the walk reads a heap that cannot move under it.
	private static DataTarget Attach()
	{
		return DataTarget.CreateSnapshotAndAttach( Environment.ProcessId );
	}

	private static Result Walk( bool collectFirst )
	{
		if ( collectFirst )
		{
			//Twice, with the finalizers in between: one pass queues them, the second takes what they
			//released. Otherwise a census reads a heap of things that are already dead and calls them held.
			GC.Collect( GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true );
			GC.WaitForPendingFinalizers();
			GC.Collect( GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true );
		}

		Status = "walking the heap…";

		var timer = Stopwatch.StartNew();

		var totals = new Dictionary<string, (long Bytes, long Count)>( 4096, StringComparer.Ordinal );

		long totalBytes = 0;
		long totalObjects = 0;

		using ( var target = Attach() )
		{
			using var runtime = CreateRuntime( target );
			if ( runtime is null ) return null;

			foreach ( var obj in runtime.Heap.EnumerateObjects() )
			{
				if ( !obj.IsValid || obj.IsFree ) continue;

				var size = (long)obj.Size;
				var name = obj.Type?.Name ?? "<unknown>";

				var existing = totals.GetValueOrDefault( name );
				totals[name] = (existing.Bytes + size, existing.Count + 1);

				totalBytes += size;
				totalObjects++;
			}
		}

		var rows = new List<Row>( totals.Count );

		foreach ( var (name, value) in totals )
			rows.Add( new Row { Type = name, Bytes = value.Bytes, Count = value.Count } );

		rows.Sort( static ( a, b ) => b.Bytes.CompareTo( a.Bytes ) );

		Status = "";

		return new Result( rows, totalBytes, totalObjects, timer.Elapsed.TotalSeconds, false );
	}

	private static IReadOnlyList<Referrer> WalkReferrers( string typeName )
	{
		var tally = new Dictionary<string, long>( 256, StringComparer.Ordinal );

		using ( var target = Attach() )
		{
			using var runtime = CreateRuntime( target );
			if ( runtime is null ) return Array.Empty<Referrer>();

			var heap = runtime.Heap;

			//The objects we are looking for, by address: cheaper to test than reading every referenced object's type back off the heap.
			var wanted = new HashSet<ulong>();

			foreach ( var obj in heap.EnumerateObjects() )
			{
				if ( !obj.IsValid || obj.IsFree ) continue;
				if ( obj.Type?.Name == typeName ) wanted.Add( obj.Address );
			}

			if ( wanted.Count == 0 ) return Array.Empty<Referrer>();

			foreach ( var obj in heap.EnumerateObjects() )
			{
				if ( !obj.IsValid || obj.IsFree ) continue;

				var name = obj.Type?.Name;
				if ( name is null || name == typeName ) continue;

				foreach ( var reference in obj.EnumerateReferences( carefully: true ) )
				{
					if ( !wanted.Contains( reference.Address ) ) continue;

					tally[name] = tally.GetValueOrDefault( name ) + 1;
				}
			}

			//A thing held by nothing on the heap is held by a static, a stack frame or a handle, and that is worth saying out loud.
			foreach ( var root in heap.EnumerateRoots() )
			{
				if ( !wanted.Contains( root.Object.Address ) ) continue;

				var name = $"‹root: {root.RootKind}›";
				tally[name] = tally.GetValueOrDefault( name ) + 1;
			}
		}

		var referrers = new List<Referrer>( tally.Count );

		foreach ( var (name, count) in tally )
			referrers.Add( new Referrer( name, count ) );

		referrers.Sort( static ( a, b ) => b.Count.CompareTo( a.Count ) );

		if ( referrers.Count > 24 ) referrers.RemoveRange( 24, referrers.Count - 24 );

		return referrers;
	}

	private static ClrRuntime CreateRuntime( DataTarget target )
	{
		foreach ( var version in target.ClrVersions )
			return version.CreateRuntime();

		return null;
	}

	//The last part of a type name, for a status line that has to fit.
	private static string Short( string type )
	{
		var at = type.LastIndexOf( '.' );

		return at >= 0 && at < type.Length - 1 ? type[( at + 1 )..] : type;
	}
}
