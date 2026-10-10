using System;
using System.Collections.Generic;

namespace Editor;

//What a line of the memory dock is.
internal enum MemoryRowKind
{
	/// <summary>A heading between the halves.</summary>
	Section,

	/// <summary>A world, or one type of object in it.</summary>
	Scene,

	/// <summary>One type on the managed heap.</summary>
	Heap,

	/// <summary>Something holding a type that was asked about.</summary>
	Referrer,
}

//One line of the memory dock, as the list reads it.
internal readonly struct MemoryRow
{
	public MemoryRowKind Kind { get; init; }
	public int Depth { get; init; }
	public bool Foldable { get; init; }

	public string Name { get; init; }

	/// <summary>The heap type this line folds, where it folds one.</summary>
	public string Key { get; init; }

	public long Bytes { get; init; }
	public long DeltaBytes { get; init; }
	public long Count { get; init; }
	public long DeltaCount { get; init; }

	/// <summary>How much of the bar is filled, 0 to 1.</summary>
	public float Portion { get; init; }

	/// <summary>Whether the count's delta is growth per second rather than since the baseline.</summary>
	public bool PerSecond { get; init; }

	public bool SameAs( in MemoryRow other ) =>
		Kind == other.Kind &&
		Depth == other.Depth &&
		Foldable == other.Foldable &&
		Bytes == other.Bytes &&
		DeltaBytes == other.DeltaBytes &&
		Count == other.Count &&
		DeltaCount == other.DeltaCount &&
		string.Equals( Name, other.Name, StringComparison.Ordinal ) &&
		string.Equals( Key, other.Key, StringComparison.Ordinal );
}

//The two censuses, flattened into the lines a list can show: the scene walk that runs every second, and whatever the last heap snapshot found.
internal sealed class MemoryRows
{
	private List<MemoryRow> rows = new();
	private List<MemoryRow> next = new();

	private HeapCensus.Result titleFor;
	private string title;

	public int Count => rows.Count;

	public MemoryRow this[int index] => rows[index];

	/// <summary>Rebuilds the lines, and says whether any of them says something new.</summary>
	public bool Build( IReadOnlyList<MemorySceneRow> scene, HeapCensus.Result census,
		HashSet<string> expanded, bool showEverything )
	{
		next.Clear();

		if ( scene.Count > 0 )
		{
			next.Add( Section( "Scene objects — live, every second" ) );

			foreach ( var row in scene )
			{
				next.Add( new MemoryRow
				{
					Kind = MemoryRowKind.Scene,
					Depth = row.Depth,
					Name = row.Name,
					Count = row.Count,
					DeltaCount = row.Delta,
					PerSecond = true,
				} );
			}
		}

		if ( census is null )
		{
			next.Add( Section( "Managed heap — press Snapshot" ) );
		}
		else
		{
			next.Add( Section( Title( census ) ) );

			//The biggest row is the bar's scale; everything else is read against it.
			var scale = (double)Math.Max( 1, census.Rows.Count > 0 ? census.Rows[0].Bytes : 1 );

			//A megabyte is the floor for a list somebody reads: an editor holds tens of thousands of types and all but a few dozen are noise.
			var floor = showEverything ? 0 : 1024 * 1024;

			foreach ( var row in census.Rows )
			{
				if ( row.Bytes < floor && Math.Abs( row.DeltaBytes ) < floor ) continue;

				var open = expanded.Contains( row.Type );

				next.Add( new MemoryRow
				{
					Kind = MemoryRowKind.Heap,
					Foldable = true,
					Name = row.Type,
					Key = row.Type,
					Bytes = row.Bytes,
					DeltaBytes = row.DeltaBytes,
					Count = row.Count,
					DeltaCount = row.DeltaCount,
					Portion = (float)( row.Bytes / scale ),
				} );

				if ( !open ) continue;

				if ( row.Referrers is null )
				{
					next.Add( new MemoryRow
					{
						Kind = MemoryRowKind.Referrer,
						Depth = 1,
						Name = HeapCensus.Busy ? "looking…" : "…",
					} );

					continue;
				}

				foreach ( var referrer in row.Referrers )
				{
					next.Add( new MemoryRow
					{
						Kind = MemoryRowKind.Referrer,
						Depth = 1,
						Name = referrer.Name,
						Count = referrer.Count,
					} );
				}
			}
		}

		var changed = !SameShape();

		( rows, next ) = ( next, rows );

		return changed;
	}

	/// <summary>The heading a line sits under, or -1 where it sits under none.</summary>
	public int SectionAt( int index )
	{
		var found = -1;

		for ( var at = 0; at <= index && at < rows.Count; ++at )
		{
			if ( rows[at].Kind == MemoryRowKind.Section ) found = at;
		}

		return found;
	}

	private static MemoryRow Section( string title ) => new()
	{
		Kind = MemoryRowKind.Section,
		Name = title,
	};

	//What the snapshot found, said once per snapshot rather than once a second.
	private string Title( HeapCensus.Result census )
	{
		if ( ReferenceEquals( titleFor, census ) && title is not null ) return title;

		titleFor = census;

		Span<char> buffer = stackalloc char[32];

		title = $"Managed heap — {new string( buffer[..PanelText.WriteBytes( buffer, census.TotalBytes )] )}" +
			$" in {census.TotalObjects:N0} objects, walked in {census.Seconds:F1}s" +
			( census.AgainstBaseline ? ", against baseline" : "" );

		return title;
	}

	private bool SameShape()
	{
		if ( rows.Count != next.Count ) return false;

		for ( var at = 0; at < rows.Count; ++at )
		{
			if ( !rows[at].SameAs( next[at] ) ) return false;
		}

		return true;
	}
}
