namespace Rinten.UI;

//Under -ticktypes: which of the tick admission reasons let each panel in, and what it cost
internal static class TickReasons
{
	internal enum Reason
	{
		GameInterface,
		Requested,
		RazorDirty,
		BindParameters,
		ParentChanged,
		StyleDirty,
		InMotion,
		EveryFrameTick,
		EveryFrameBuildHash,
		EveryFrameRazorTree,
		EveryFrameClassBinds,
		HashUnchanged,
		Count
	}

	static readonly string[] names =
	[
		"game interface (never skipped)",
		"asked for (RequestTick)",
		"razor tree dirty",
		"bind parameters changed",
		"parent changed",
		"style dirty",
		"in motion (transition/animation/scroll/events)",
		"every frame: type overrides Tick()",
		"every frame: type overrides BuildHash()",
		"every frame: razor render tree",
		"every frame: class binds",
		"BuildHash asked, unchanged - no turn taken",
	];

	static readonly long[] calls = new long[(int)Reason.Count];
	static readonly long[] ticks = new long[(int)Reason.Count];

	//Panels the walk entered at all, wanted or not.
	internal static long Visited;

	internal static void Add( Reason reason, long elapsed )
	{
		lock ( calls )
		{
			calls[(int)reason]++;
			ticks[(int)reason] += elapsed;
		}
	}

	static readonly Dictionary<string, (long Ticks, long Calls)> askers = new();

	//Who keeps asking for ticks and why: call site, whether a tree was rebuilt, what kept it moving

	static readonly Dictionary<string, long> asks = new();
	static readonly Dictionary<string, string> askStacks = new();
	static readonly Dictionary<string, long> renders = new();
	static readonly Dictionary<string, (long Transition, long Animation, long Scroll, long Events)> motion = new();

	//The ask that captures a stack: late enough that start-up is over and the panel is asking in its steady state.
	const int StackAtAsk = 500;

	internal static void NoteAsk( Panel panel )
	{
		var name = panel.TickTypeName;

		lock ( asks )
		{
			asks.TryGetValue( name, out var n );
			asks[name] = ++n;

			if ( n == StackAtAsk && !askStacks.ContainsKey( name ) )
				askStacks[name] = System.Environment.StackTrace;
		}
	}

	internal static void NoteRender( string name )
	{
		lock ( renders )
		{
			renders.TryGetValue( name, out var n );
			renders[name] = n + 1;
		}
	}

	internal static void NoteMotion( string name, bool transition, bool animation, bool scroll, bool events )
	{
		lock ( motion )
		{
			motion.TryGetValue( name, out var m );
			motion[name] = (m.Transition + ( transition ? 1 : 0 ), m.Animation + ( animation ? 1 : 0 ),
				m.Scroll + ( scroll ? 1 : 0 ), m.Events + ( events ? 1 : 0 ));
		}
	}

	internal static IEnumerable<(string Name, long Count)> Renders( int most )
	{
		lock ( renders ) return renders.OrderByDescending( x => x.Value ).Take( most ).Select( x => (x.Key, x.Value) ).ToList();
	}

	internal static IEnumerable<(string Name, long Transition, long Animation, long Scroll, long Events)> Motion( int most )
	{
		lock ( motion )
			return motion.OrderByDescending( x => x.Value.Transition + x.Value.Animation + x.Value.Scroll + x.Value.Events )
				.Take( most ).Select( x => (x.Key, x.Value.Transition, x.Value.Animation, x.Value.Scroll, x.Value.Events) ).ToList();
	}

	//Where a type was asking from at its 500th ask, trimmed to the frames above RequestTick.
	internal static string AskStack( string name )
	{
		string stack;
		lock ( asks ) { if ( !askStacks.TryGetValue( name, out stack ) ) return null; }

		var lines = stack.Split( '\n' )
			.Select( x => x.Trim() )
			.SkipWhile( x => !x.Contains( "RequestTick" ) )
			.Skip( 1 )
			.Take( 14 );

		return string.Join( "\n", lines );
	}

	//A panel that took its turn because it asked for one, by the type it calls itself.
	internal static void AddAsker( string name, long elapsed )
	{
		lock ( askers )
		{
			askers.TryGetValue( name, out var held );
			askers[name] = (held.Ticks + elapsed, held.Calls + 1);
		}
	}

	//The types that asked for turns, costliest first.
	internal static IEnumerable<(string Name, double Ms, long Calls)> Askers( int most )
	{
		var scale = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		var all = new List<(string, double, long)>();

		lock ( askers )
		{
			foreach ( var (name, held) in askers )
				all.Add( (name, held.Ticks * scale, held.Calls) );
		}

		all.Sort( static ( a, b ) => b.Item2.CompareTo( a.Item2 ) );
		if ( all.Count > most ) all.RemoveRange( most, all.Count - most );
		return all;
	}

	internal static IEnumerable<(string Name, double Ms, long Calls)> All()
	{
		var scale = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		var all = new List<(string, double, long)>();

		lock ( calls )
		{
			for ( int i = 0; i < (int)Reason.Count; i++ )
			{
				if ( calls[i] == 0 ) continue;
				all.Add( (names[i], ticks[i] * scale, calls[i]) );
			}
		}

		all.Sort( static ( a, b ) => b.Item2.CompareTo( a.Item2 ) );
		return all;
	}
}
