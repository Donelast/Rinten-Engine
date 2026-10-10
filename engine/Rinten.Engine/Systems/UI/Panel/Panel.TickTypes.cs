using System.Collections.Generic;

namespace Rinten.UI;

//Per-type tick timing, only with -ticktypes; cheap enough to find which panel types cost the frame
internal static class TickTypes
{
	internal static readonly bool Counting =
		Rinten.Utility.CommandLine.HasSwitch( "-ticktypes" );

	internal struct Spent
	{
		public long Ticks;
		public long Calls;
	}

	private static readonly Dictionary<string, Spent> spent = new();

	//Locked because panels tick on several threads; a racing Dictionary can hang a reader forever
	internal static void Add( string name, long ticks )
	{
		lock ( spent )
		{
			spent.TryGetValue( name, out var held );

			held.Ticks += ticks;
			held.Calls++;

			spent[name] = held;
		}
	}

	//The types that spent the most, worst first, with milliseconds and calls.
	internal static IEnumerable<(string Name, double Ms, long Calls)> Worst( int most )
	{
		var scale = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		List<(string, double, long)> all;

		lock ( spent )
		{
			all = new List<(string, double, long)>( spent.Count );

			foreach ( var pair in spent )
				all.Add( (pair.Key, pair.Value.Ticks * scale, pair.Value.Calls) );
		}

		all.Sort( static ( a, b ) => b.Item2.CompareTo( a.Item2 ) );

		if ( all.Count > most ) all.RemoveRange( most, all.Count - most );

		return all;
	}

	internal static void Clear() { lock ( spent ) spent.Clear(); }
}
