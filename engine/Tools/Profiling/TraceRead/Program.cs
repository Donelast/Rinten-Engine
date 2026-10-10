using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace Rinten.Profiling;

//Reads a .nettrace in the terminal: which methods the JIT compiled, and what our zones cost
internal static class Program
{
	private static int Main( string[] args )
	{
		if ( args.Length == 0 )
		{
			Console.Error.WriteLine( "usage: traceread <file.nettrace|run folder> [-top N] [-tail SECONDS]" );
			return 2;
		}

		var path = args[0];

		if ( Directory.Exists( path ) )
		{
			//A run folder rather than a file: take the trace out of it.
			var found = Directory.GetFiles( path, "*.nettrace" ).OrderBy( f => f ).LastOrDefault();

			if ( found is null )
			{
				Console.Error.WriteLine( $"no .nettrace in {path}" );
				return 2;
			}

			path = found;
		}

		if ( !File.Exists( path ) )
		{
			Console.Error.WriteLine( $"no such file: {path}" );
			return 2;
		}

		var top = 25;

		//The trace covers the whole run, and the run is over a hundred seconds of booting in front of fifteen seconds of measurement. Boot stalls
		//are real and worth their own look, but averaged in with the measured window they own every total and every worst case in the table.
		var tail = 0.0;

		for ( var at = 1; at < args.Length - 1; ++at )
		{
			if ( args[at] == "-top" && int.TryParse( args[at + 1], out var parsed ) ) top = parsed;
			if ( args[at] == "-tail" && double.TryParse( args[at + 1], out var seconds ) ) tail = seconds;
		}

		Console.WriteLine();
		Console.WriteLine( $"{Path.GetFileName( path )}, {new FileInfo( path ).Length / ( 1024.0 * 1024.0 ):F0} MB" );

		Read( path, top, tail );

		return 0;
	}

	private sealed class Tally
	{
		public long Count;
		public double TotalMs;
		public double WorstMs;
	}

	private static void Read( string path, int top, double tail )
	{
		var jitted = new Dictionary<string, Tally>( StringComparer.Ordinal );
		var zones = new Dictionary<string, Tally>( StringComparer.Ordinal );

		//One open zone per thread per name. Zones nest, so the stack is per thread and a stop
		//pairs with the innermost matching start - which is what the engine's own scopes do.
		var open = new Dictionary<int, Stack<(string Name, double At)>>();

		var firstMs = double.MaxValue;
		var lastMs = 0.0;

		//Zones whose partner never arrived. A trace with buffer drops in it can still be read, but only if it says how much it lost.
		var orphaned = 0;
		var unmatched = 0;

		//The window is only known once the whole trace has been walked, so a tail costs a second pass. Cheap against the alternative of guessing.
		var endMs = 0.0;

		if ( tail > 0 )
		{
			using var scan = new EventPipeEventSource( path );

			scan.AllEvents += one =>
			{
				if ( one.TimeStampRelativeMSec > endMs ) endMs = one.TimeStampRelativeMSec;
			};

			scan.Process();
		}

		var fromMs = tail > 0 ? endMs - ( tail * 1000.0 ) : double.MinValue;

		if ( tail > 0 )
			Console.WriteLine( $"the last {tail:F0}s of it, from {fromMs / 1000.0:F1}s to {endMs / 1000.0:F1}s" );

		using ( var source = new EventPipeEventSource( path ) )
		{
			source.Clr.MethodJittingStarted += method =>
			{
				if ( method.TimeStampRelativeMSec < fromMs ) return;

				var name = $"{method.MethodNamespace}.{method.MethodName}";

				if ( !jitted.TryGetValue( name, out var tally ) )
					jitted[name] = tally = new Tally();

				tally.Count++;
			};

			//The time the compiler actually spent, which is the number that competes with the frame for a processor.
			source.Clr.MethodLoadVerbose += method =>
			{
				if ( method.TimeStampRelativeMSec < fromMs ) return;
				if ( method.TimeStampRelativeMSec < firstMs ) firstMs = method.TimeStampRelativeMSec;
				if ( method.TimeStampRelativeMSec > lastMs ) lastMs = method.TimeStampRelativeMSec;
			};

			source.Dynamic.All += data =>
			{
				if ( data.ProviderName != "Rinten-Engine" ) return;
				if ( data.TimeStampRelativeMSec < fromMs ) return;

				var name = data.PayloadByName( "name" ) as string;
				if ( name is null ) return;

				if ( !open.TryGetValue( data.ThreadID, out var stack ) )
					open[data.ThreadID] = stack = new Stack<(string, double)>();

				if ( data.EventName.EndsWith( "Start", StringComparison.Ordinal ) )
				{
					stack.Push( (name, data.TimeStampRelativeMSec) );
					return;
				}

				if ( !data.EventName.EndsWith( "Stop", StringComparison.Ordinal ) ) return;
				if ( stack.Count == 0 ) { orphaned++; return; }

				//Match zone ends by name, not depth, so a dropped event can't skew later zones; count orphans
				if ( !stack.Any( one => one.Name == name ) ) { orphaned++; return; }

				while ( stack.Peek().Name != name )
				{
					stack.Pop();
					unmatched++;
				}

				var (started, at) = stack.Pop();

				var elapsed = data.TimeStampRelativeMSec - at;

				if ( !zones.TryGetValue( started, out var tally ) )
					zones[started] = tally = new Tally();

				tally.Count++;
				tally.TotalMs += elapsed;
				if ( elapsed > tally.WorstMs ) tally.WorstMs = elapsed;
			};

			source.Process();
		}

		var seconds = lastMs > firstMs && firstMs < double.MaxValue ? ( lastMs - firstMs ) / 1000.0 : 0;

		WriteJit( jitted, seconds, top );
		WriteZones( zones, top, orphaned, unmatched );
	}

	private static void WriteJit( Dictionary<string, Tally> jitted, double seconds, int top )
	{
		var total = jitted.Values.Sum( t => t.Count );

		Console.WriteLine();
		Console.WriteLine( "-- what the runtime compiled --------------------------------------------" );

		if ( total == 0 )
		{
			Console.WriteLine( "  nothing - the trace has no JIT events (collect with Microsoft-Windows-DotNETRuntime:0x10:4)" );
			return;
		}

		Console.WriteLine( seconds > 0
			? $"  {total:N0} methods over {seconds:F1}s, {total / seconds:N0} a second"
			: $"  {total:N0} methods" );

		Console.WriteLine();

		//Grouped by the type rather than the method: a hundred methods on one type is one cause, and a hundred causes is a different problem.
		var byType = jitted
			.GroupBy( pair => Namespace( pair.Key ) )
			.Select( group => (Type: group.Key, Count: group.Sum( pair => pair.Value.Count ),
				Distinct: group.Count()) )
			.OrderByDescending( one => one.Count )
			.Take( top );

		Console.WriteLine( $"  {"compiled",10}  {"distinct",8}  where" );

		foreach ( var one in byType )
			Console.WriteLine( $"  {one.Count,10:N0}  {one.Distinct,8:N0}  {one.Type}" );

		Console.WriteLine();
		Console.WriteLine( "  the same method compiled more than once is tiering; a method compiled once" );
		Console.WriteLine( "  and never again, thousands a second, is code being generated at run time." );

		var repeated = jitted.Where( pair => pair.Value.Count > 1 )
			.OrderByDescending( pair => pair.Value.Count ).Take( 10 ).ToList();

		if ( repeated.Count > 0 )
		{
			Console.WriteLine();
			Console.WriteLine( "  compiled more than once:" );

			foreach ( var pair in repeated )
				Console.WriteLine( $"  {pair.Value.Count,10:N0}  {pair.Key}" );
		}
	}

	private static string Namespace( string name )
	{
		var cut = name.LastIndexOf( '.' );

		return cut > 0 ? name[..cut] : name;
	}

	private static void WriteZones( Dictionary<string, Tally> zones, int top, int orphaned, int unmatched )
	{
		Console.WriteLine();
		Console.WriteLine( "-- our own zones, out of the trace --------------------------------------" );

		if ( zones.Count == 0 )
		{
			Console.WriteLine( "  none - the trace has no Rinten-Engine events (collect with Rinten-Engine:0x3:5)" );
			return;
		}

		Console.WriteLine( $"  {"total ms",10}  {"calls",9}  {"mean ms",9}  {"worst ms",9}  zone" );

		foreach ( var pair in zones.OrderByDescending( p => p.Value.TotalMs ).Take( top ) )
		{
			var mean = pair.Value.Count > 0 ? pair.Value.TotalMs / pair.Value.Count : 0;

			Console.WriteLine( $"  {pair.Value.TotalMs,10:F1}  {pair.Value.Count,9:N0}  {mean,9:F3}  " +
				$"{pair.Value.WorstMs,9:F2}  {pair.Key}" );
		}

		if ( orphaned > 0 || unmatched > 0 )
		{
			Console.WriteLine();
			Console.WriteLine( $"  {orphaned:N0} stops with no start and {unmatched:N0} starts closed by " +
				"somebody else's stop - the trace dropped events, so treat the counts as a floor" );
		}
	}
}
