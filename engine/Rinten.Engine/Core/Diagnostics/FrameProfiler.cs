using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Rinten.Diagnostics;

//Hierarchical main-thread frame profiler (time and allocations per span); free while disabled
internal static class FrameProfiler
{
	/// <summary>Recording on if the Profiler dock or a perf report wants it; two separate flags so neither clobbers</summary>
	public static bool Enabled
	{
		get => enabled || Reporting;
		set => enabled = value;
	}

	private static bool enabled;

	//Whether a perf report is recording, whatever the dock says - see Enabled.
	internal static bool Reporting;

	/// <summary>Whether every component's update gets a span of its own. The heart of "exact sources", and the expensive half: a scene of thousands of components pays a fraction of a millisecond for it, so it can be turned off while still watching the systems.</summary>
	public static bool PerComponent = true;

	/// <summary>How many spans were refused this frame for want of room.</summary>
	public static int Dropped { get; private set; }

	/// <summary>Counts finished frames, so readers can tell fresh data from stale.</summary>
	public static int FrameNumber { get; private set; }

	//-- recording --------------------------------------------------------------

	//One span as it happened: which name, inside which record.
	private struct Record
	{
		public int Name;
		public int Parent;
		public long Began;
		public long Ended;
		public long AllocBegan;
		public long AllocEnded;
	}

	private static Record[] records = new Record[4096];
	private static int recordCount;

	//The innermost open record, or -1. What the next span is inside of.
	private static int openTop = -1;

	private static int droppedNow;

	//Names are interned to indices once; records carry the index. The list only ever grows
	//- a profiler that ran all session holds a few hundred strings, which is nothing.
	private static readonly List<string> names = new();
	private static readonly Dictionary<string, int> nameIds = new( StringComparer.Ordinal );

	//A component type's display name, kept weakly so a hotloaded assembly is not pinned by its old types having been profiled once.
	private static readonly ConditionalWeakTable<Type, string> typeNames = new();

	private static int NameId( string name )
	{
		if ( nameIds.TryGetValue( name, out var id ) ) return id;

		id = names.Count;
		names.Add( name );
		nameIds.Add( name, id );

		return id;
	}

	/// <summary>An open span. Dispose closes it; a default one closes nothing.</summary>
	public struct Scope : IDisposable
	{
		//Record index plus one, so default(Scope) is inert.
		internal int Index;

		public void Dispose()
		{
			if ( Index == 0 ) return;

			Pop( Index - 1 );
			Index = 0;
		}
	}

	/// <summary>Opens a span. Free when disabled; nothing when called off the main thread, where a frame tree has no honest place to put it.</summary>
	public static Scope Push( string name )
	{
		if ( !Enabled || !ThreadSafe.IsMainThread || name is null ) return default;

		//Bounded rather than boundless: a runaway loop opening scopes should show up as a dropped count, not as the profiler eating the heap.
		if ( recordCount >= 65536 )
		{
			droppedNow++;
			return default;
		}

		if ( recordCount == records.Length )
			Array.Resize( ref records, records.Length * 2 );

		var index = recordCount++;

		records[index] = new Record
		{
			Name = NameId( name ),
			Parent = openTop,
			Began = Stopwatch.GetTimestamp(),
			AllocBegan = GC.GetAllocatedBytesForCurrentThread(),
		};

		openTop = index;

		return new Scope { Index = index + 1 };
	}

	/// <summary>A span named after a component's type, or nothing when the per-component half is off.</summary>
	public static Scope PushComponent( Component component )
	{
		if ( !Enabled || !PerComponent || component is null ) return default;

		var name = typeNames.GetValue( component.GetType(), static t => t.Name );

		return Push( name );
	}

	private static void Pop( int index )
	{
		if ( index >= recordCount ) return;

		var now = Stopwatch.GetTimestamp();
		var alloc = ThreadSafe.IsMainThread ? GC.GetAllocatedBytesForCurrentThread() : records[index].AllocBegan;

		//Anything opened inside this span and never closed - an exception took the stack past
		//its using, usually - closes with it, at the same moment, so the tree stays a tree.
		while ( openTop != -1 && openTop != index )
		{
			if ( records[openTop].Ended == 0 )
			{
				records[openTop].Ended = now;
				records[openTop].AllocEnded = alloc;
			}

			openTop = records[openTop].Parent;
		}

		records[index].Ended = now;
		records[index].AllocEnded = alloc;

		if ( openTop == index ) openTop = records[index].Parent;
	}

	//-- the tree ---------------------------------------------------------------

	/// <summary>One line of the published tree. The objects persist across frames - the dock keys its collapsed state on <see cref="Id"/> and repaints live values without the list being rebuilt.</summary>
	public sealed class Entry
	{
		public int Id { get; internal set; }
		public string Name { get; internal set; }
		public int Depth { get; internal set; }
		public bool HasChildren { get; internal set; }

		/// <summary>This frame, smoothed, and the recent worst.</summary>
		public float LastMs { get; internal set; }
		public float AvgMs { get; internal set; }
		public float MaxMs { get; internal set; }

		/// <summary>What the span cost minus what its children cost - the part that belongs to nobody deeper.</summary>
		public float SelfMs { get; internal set; }

		/// <summary>Raw self time this frame, for readers averaging over their own window instead of the smoothed value</summary>
		public float LastSelfMs { get; internal set; }

		public int Calls { get; internal set; }

		/// <summary>Managed bytes allocated inside, children included and not.</summary>
		public long AllocBytes { get; internal set; }
		public long SelfAllocBytes { get; internal set; }
	}

	//A node of the persistent tree, keyed under its parent by name.
	private sealed class Node
	{
		public int NameId;
		public Node Parent;
		public Dictionary<int, Node> Children;
		public Entry Entry;

		/// <summary>The children again, in display order. Each node owns its own list so the recursive publish never shares scratch.</summary>
		public List<Node> Ordered;

		//This frame's accumulation.
		public long FrameTicks;
		public long ChildTicks;
		public long FrameAlloc;
		public long ChildAlloc;
		public int FrameCalls;
		public int LastSeenFrame;
	}

	private static readonly Node root = new();
	private static readonly List<Node> touched = new();

	//Which node each record landed in, so a child finds its parent's node.
	private static Node[] recordNodes = new Node[4096];

	private static readonly List<Entry> published = new();

	/// <summary>The last finished frame as a flattened tree, parents before children, siblings heaviest first. Rebuilt once a frame; the Entry objects themselves are stable.</summary>
	public static IReadOnlyList<Entry> Frame => published;

	private static int nextNodeId = 1;

	//Closes the frame: folds the records into the persistent tree, updates the published list, and
	//starts the next frame's recording. Called once a frame by the engine loop, from the main thread.
	internal static void Flip()
	{
		if ( !Enabled && recordCount == 0 )
		{
			//Nothing recorded and nothing recording: publish emptiness once, cheaply, rather than rebuilding an empty tree every frame.
			if ( published.Count > 0 ) published.Clear();
			return;
		}

		FrameNumber++;

		//A span still open when the frame flips closes here - the frame boundary is the outermost scope there is.
		var now = Stopwatch.GetTimestamp();

		while ( openTop != -1 )
		{
			if ( records[openTop].Ended == 0 ) records[openTop].Ended = now;
			openTop = records[openTop].Parent;
		}

		if ( recordNodes.Length < records.Length )
			Array.Resize( ref recordNodes, records.Length );

		//Records are in pre-order - a parent is recorded before anything inside it - so one forward walk resolves every record to its node.
		for ( var at = 0; at < recordCount; ++at )
		{
			ref var record = ref records[at];

			//A parent that resolved to nothing means the records raced - more than one thread claiming to be main, which
			//the test host does. Diagnostics must never crash what they diagnose: the orphan hangs off the root instead.
			var parent = record.Parent >= 0 && record.Parent < at ? recordNodes[record.Parent] : root;
			parent ??= root;

			parent.Children ??= new Dictionary<int, Node>();

			if ( !parent.Children.TryGetValue( record.Name, out var node ) )
			{
				node = new Node { NameId = record.Name, Parent = parent };
				parent.Children.Add( record.Name, node );
			}

			recordNodes[at] = node;

			if ( node.FrameCalls == 0 && node.LastSeenFrame != FrameNumber )
			{
				node.LastSeenFrame = FrameNumber;
				touched.Add( node );
			}

			var ticks = record.Ended - record.Began;
			var alloc = record.AllocEnded - record.AllocBegan;

			if ( ticks < 0 ) ticks = 0;
			if ( alloc < 0 ) alloc = 0;

			node.FrameTicks += ticks;
			node.FrameAlloc += alloc;
			node.FrameCalls++;

			if ( parent != root )
			{
				parent.ChildTicks += ticks;
				parent.ChildAlloc += alloc;
			}
		}

		recordCount = 0;
		Dropped = droppedNow;
		droppedNow = 0;

		//Fold the frame into each touched node's running picture.
		const float blend = 0.08f;
		var toMs = 1000.0 / Stopwatch.Frequency;

		foreach ( var node in touched )
		{
			var entry = node.Entry ??= new Entry { Id = nextNodeId++ };

			var frameMs = (float)( node.FrameTicks * toMs );
			var selfMs = (float)( ( node.FrameTicks - node.ChildTicks ) * toMs );

			entry.LastMs = frameMs;
			entry.AvgMs = entry.AvgMs <= 0 ? frameMs : entry.AvgMs + ( frameMs - entry.AvgMs ) * blend;

			//The worst recent frame, let down slowly so a spike stays visible long enough to be read.
			entry.MaxMs = MathF.Max( frameMs, entry.MaxMs * 0.995f );

			//Self time smoothed like the total, so the two columns don't contradict on spiky frames
			selfMs = MathF.Max( 0, selfMs );
			entry.LastSelfMs = selfMs;
			entry.SelfMs = entry.SelfMs <= 0 ? selfMs : entry.SelfMs + ( selfMs - entry.SelfMs ) * blend;
			entry.Calls = node.FrameCalls;
			entry.AllocBytes = node.FrameAlloc;
			entry.SelfAllocBytes = Math.Max( 0, node.FrameAlloc - node.ChildAlloc );

			node.FrameTicks = 0;
			node.ChildTicks = 0;
			node.FrameAlloc = 0;
			node.ChildAlloc = 0;
			node.FrameCalls = 0;
		}

		touched.Clear();

		Publish();
	}

	//Flattens the tree, heaviest siblings first, decaying and pruning what has not run lately
	//- a node that fired once must fade rather than squatting in the list all session.
	private static void Publish()
	{
		published.Clear();
		PublishChildren( root, 0 );
	}

	private static void PublishChildren( Node parent, int depth )
	{
		if ( parent.Children is null || parent.Children.Count == 0 ) return;

		var ordered = parent.Ordered ??= new List<Node>();
		ordered.Clear();

		foreach ( var child in parent.Children.Values )
		{
			var entry = child.Entry;
			if ( entry is null ) continue;

			//Not seen this frame: no calls, and the average fades.
			if ( child.LastSeenFrame != FrameNumber )
			{
				entry.LastMs = 0;
				entry.LastSelfMs = 0;
				entry.Calls = 0;
				entry.AllocBytes = 0;
				entry.SelfAllocBytes = 0;
				entry.SelfMs = 0;
				entry.AvgMs *= 0.98f;
				entry.MaxMs *= 0.995f;
			}

			ordered.Add( child );
		}

		//Idle nodes leave. Checked here, where the parent's dictionary is at hand; a few hundred frames of silence is gone-for-good, not a hitch.
		for ( var at = ordered.Count - 1; at >= 0; --at )
		{
			var child = ordered[at];

			if ( child.Entry.AvgMs < 0.0005f && FrameNumber - child.LastSeenFrame > 600 )
			{
				parent.Children.Remove( child.NameId );
				ordered.RemoveAt( at );
			}
		}

		ordered.Sort( static ( a, b ) => b.Entry.AvgMs.CompareTo( a.Entry.AvgMs ) );

		foreach ( var child in ordered )
		{
			child.Entry.Name = names[child.NameId];
			child.Entry.Depth = depth;
			child.Entry.HasChildren = child.Children is { Count: > 0 };

			published.Add( child.Entry );
			PublishChildren( child, depth + 1 );
		}
	}
}
