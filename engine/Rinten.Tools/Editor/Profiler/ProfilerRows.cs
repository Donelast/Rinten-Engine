using Rinten;
using Rinten.Diagnostics;
using System;
using System.Collections.Generic;

namespace Editor;

//What a line of the profiler is.
internal enum ProfilerRowKind
{
	/// <summary>A heading between the two halves.</summary>
	Section,

	/// <summary>One span of the frame, out of the CPU tree.</summary>
	Cpu,

	/// <summary>One zone of the renderer's last finished frame.</summary>
	Gpu,
}

//Which column the siblings of the tree are ordered by.
internal enum ProfilerSort
{
	Time,
	Self,
	Max,
	Calls,
	Alloc,
	Name,
}

//One line of the profiler, as the list reads it.
internal readonly struct ProfilerRow
{
	public ProfilerRowKind Kind { get; private init; }
	public int Depth { get; private init; }
	public bool Foldable { get; private init; }

	/// <summary>The span this line is about, for a CPU line.</summary>
	public FrameProfiler.Entry Entry { get; private init; }

	/// <summary>What the line is called, for anything that is not a CPU line.</summary>
	public string Name { get; private init; }

	public float GpuMs { get; private init; }
	public float CpuMs { get; private init; }

	public static ProfilerRow Section( string title ) => new()
	{
		Kind = ProfilerRowKind.Section,
		Name = title,
	};

	public static ProfilerRow Cpu( FrameProfiler.Entry entry, int depth, bool foldable ) => new()
	{
		Kind = ProfilerRowKind.Cpu,
		Entry = entry,
		Depth = depth,
		Foldable = foldable && entry.HasChildren,
	};

	public static ProfilerRow Gpu( string name, float gpuMs, float cpuMs, int depth ) => new()
	{
		Kind = ProfilerRowKind.Gpu,
		Name = name,
		GpuMs = gpuMs,
		CpuMs = cpuMs,
		Depth = depth,
	};

	/// <summary>Whether two lines are about the same thing, whatever their numbers say now.</summary>
	public bool SameAs( in ProfilerRow other ) =>
		Kind == other.Kind &&
		Depth == other.Depth &&
		Foldable == other.Foldable &&
		ReferenceEquals( Entry, other.Entry ) &&
		string.Equals( Name, other.Name, StringComparison.Ordinal );
}

//Profiler tree flattened into list lines; Build reports whether rows need rebinding
internal sealed class ProfilerRows
{
	private List<ProfilerRow> rows = new();
	private List<ProfilerRow> next = new();

	//Where the headings are, so whatever is pinned over the list knows which one it is under.
	private readonly List<int> sections = new();

	private readonly List<List<(int Index, int End)>> levels = new();
	private readonly Comparison<(int Index, int End)> compare;

	private IReadOnlyList<FrameProfiler.Entry> source;
	private ProfilerSort order;

	private string gpuTitle;
	private string gpuTitleFor;

	public ProfilerRows()
	{
		compare = Compare;
	}

	public int Count => rows.Count;

	public ProfilerRow this[int index] => rows[index];

	/// <summary>Counts the times the lines came to be about different things.</summary>
	public int Version { get; private set; }

	/// <summary>The heading a line sits under, or -1 where it sits under none.</summary>
	public int SectionAt( int index )
	{
		var found = -1;

		for ( var at = 0; at < sections.Count; ++at )
		{
			if ( sections[at] > index ) break;

			found = sections[at];
		}

		return found;
	}

	/// <summary>Rebuilds the lines, and says whether they are about different things now.</summary>
	public bool Build( IReadOnlyList<FrameProfiler.Entry> entries, CoreRenderDevice.FrameProfile? gpu,
		float minMs, string search, ProfilerSort sort, HashSet<int> collapsed )
	{
		next.Clear();
		sections.Clear();

		source = entries;
		order = sort;

		if ( entries.Count > 0 ) AddSection( "CPU — main thread" );

		if ( string.IsNullOrWhiteSpace( search ) )
			Emit( 0, entries.Count, 0, 0, minMs, collapsed );
		else
			EmitMatches( search, minMs );

		if ( gpu is { } profile )
		{
			AddSection( GpuTitle() );

			foreach ( var zone in profile.Zones )
				next.Add( ProfilerRow.Gpu( zone.Name, zone.GpuMs, zone.CpuMs, zone.Depth ) );
		}

		var changed = !SameShape();

		( rows, next ) = ( next, rows );
		source = null;

		if ( changed ) Version++;

		return changed;
	}

	private void AddSection( string title )
	{
		sections.Add( next.Count );
		next.Add( ProfilerRow.Section( title ) );
	}

	//The name of the card, kept rather than rebuilt twice a second.
	private string GpuTitle()
	{
		var device = CoreRenderDevice.DeviceName;

		if ( !ReferenceEquals( device, gpuTitleFor ) || gpuTitle is null )
		{
			gpuTitleFor = device;
			gpuTitle = $"GPU — {device}";
		}

		return gpuTitle;
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

	//Emits the children of one span in the order asked for, each followed by its own children. The published tree is flat and in pre-order, so a child's
	//own descendants are the run between it and the next child at its depth. That run is taken before the children are sorted, because sorting moves them.
	private void Emit( int from, int to, int depth, int level, float minMs, HashSet<int> collapsed )
	{
		var kids = Level( level );

		kids.Clear();

		for ( var at = from; at < to; ++at )
		{
			if ( source[at].Depth == depth ) kids.Add( (at, 0) );
		}

		if ( kids.Count == 0 ) return;

		for ( var at = 0; at < kids.Count; ++at )
		{
			kids[at] = ( kids[at].Index, at + 1 < kids.Count ? kids[at + 1].Index : to );
		}

		if ( order != ProfilerSort.Time ) kids.Sort( compare );

		for ( var at = 0; at < kids.Count; ++at )
		{
			var (index, end) = kids[at];
			var entry = source[index];

			//Under the threshold takes its children with it: they cannot be over it.
			if ( entry.AvgMs < minMs && entry.MaxMs < minMs * 4.0f ) continue;

			next.Add( ProfilerRow.Cpu( entry, depth, foldable: true ) );

			if ( collapsed.Contains( entry.Id ) ) continue;

			Emit( index + 1, end, depth + 1, level + 1, minMs, collapsed );
		}
	}

	//Searching gives up the tree and lists what matched, wherever it sat.
	private void EmitMatches( string search, float minMs )
	{
		for ( var at = 0; at < source.Count; ++at )
		{
			var entry = source[at];

			if ( entry.AvgMs < minMs && entry.MaxMs < minMs * 4.0f ) continue;
			if ( entry.Name is null ) continue;
			if ( entry.Name.IndexOf( search, StringComparison.OrdinalIgnoreCase ) < 0 ) continue;

			next.Add( ProfilerRow.Cpu( entry, depth: 0, foldable: false ) );
		}
	}

	private List<(int Index, int End)> Level( int level )
	{
		while ( levels.Count <= level ) levels.Add( new List<(int, int)>() );

		return levels[level];
	}

	private int Compare( (int Index, int End) a, (int Index, int End) b )
	{
		var x = source[a.Index];
		var y = source[b.Index];

		return order switch
		{
			ProfilerSort.Self => y.SelfMs.CompareTo( x.SelfMs ),
			ProfilerSort.Max => y.MaxMs.CompareTo( x.MaxMs ),
			ProfilerSort.Calls => y.Calls.CompareTo( x.Calls ),
			ProfilerSort.Alloc => y.AllocBytes.CompareTo( x.AllocBytes ),
			ProfilerSort.Name => string.CompareOrdinal( x.Name, y.Name ),
			_ => y.AvgMs.CompareTo( x.AvgMs ),
		};
	}
}
