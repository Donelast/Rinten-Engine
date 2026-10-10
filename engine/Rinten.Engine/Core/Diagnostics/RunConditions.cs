using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Rinten.Diagnostics;

//Writes the conditions a measurement ran under (build, GC, env, process) into the perf report
internal static class RunConditions
{
	/// <summary>The conditions as report lines, ready to append.</summary>
	public static void Write( StringBuilder report, ClockWindow clocks )
	{
		report.AppendLine( "-- conditions ------------------------------------------------------------" );

		WriteBuild( report );
		WriteRuntime( report );
		WriteGc( report );
		WriteEnvironment( report );
		WriteProcess( report );
		WriteRenderer( report );
		WriteTracing( report );
		WriteClocks( report, clocks );
	}

	/// <summary>How far each of the three clocks moved over one measured window.</summary>
	public readonly record struct ClockWindow( double Game, double Loop, double Wall );

	//Whether ETW zones are being emitted; a failed EventSource silently disables itself
	private static void WriteTracing( StringBuilder report )
	{
		var source = EngineEventSource.Instance;

		if ( source.ConstructionException is { } broken )
		{
			report.AppendLine( $"  ETW PROVIDER IS DEAD - no zones leave this process: {broken.Message}" );
			return;
		}

		report.AppendLine( $"  etw provider {EngineEventSource.ProviderName}, " +
			$"{( source.IsEnabled() ? "a session is recording" : "nobody listening" )}" );
	}

	//-- the build ---------------------------------------------------------------

	//Build configuration and whether the IL was compiled with optimisation off, reported separately
	private static void WriteBuild( StringBuilder report )
	{
		var assembly = typeof( RunConditions ).Assembly;

		var configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "unknown";
		var version = assembly.GetName().Version?.ToString() ?? "?";

		var debuggable = assembly.GetCustomAttribute<DebuggableAttribute>();
		var optimised = debuggable is null || !debuggable.IsJITOptimizerDisabled;

		report.AppendLine( $"  build {configuration}, Rinten.Engine {version}, " +
			$"jit optimiser {( optimised ? "on" : "OFF - this is not a performance measurement" )}" );

		if ( Debugger.IsAttached )
			report.AppendLine( "  A DEBUGGER IS ATTACHED - nothing measured here is what a user would get" );
	}

	//-- the runtime -------------------------------------------------------------

	private static void WriteRuntime( StringBuilder report )
	{
		report.AppendLine( $"  {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}, " +
			$"tiered {Switch( "System.Runtime.TieredCompilation", true )}, " +
			$"quickjit {Switch( "System.Runtime.TieredCompilation.QuickJit", true )}, " +
			$"pgo {Switch( "System.Runtime.TieredPGO", true )}" );
	}

	//A runtime switch as it is actually set, or the runtime's own default when nobody set it - "unset" would be a third answer nobody can act on.
	private static string Switch( string name, bool fallback )
	{
		if ( AppContext.TryGetSwitch( name, out var value ) )
			return value ? "on" : "off";

		return fallback ? "on (default)" : "off (default)";
	}

	//-- the collector -----------------------------------------------------------

	//How the garbage collector is configured. Named here because in a managed editor the collector is not
	//a detail of the runtime, it is a category in the frame - GcPause has its own row in the report above.
	private static void WriteGc( StringBuilder report )
	{
		var server = System.Runtime.GCSettings.IsServerGC;
		var latency = System.Runtime.GCSettings.LatencyMode;

		var line = new StringBuilder( $"  gc {( server ? "server" : "workstation" )}, {latency}" );

		object concurrent = null;

		try
		{
			var config = GC.GetConfigurationVariables();

			//Looked up without regard for case: the runtime's own names for these have changed spelling
			//between versions, and a missing key reads as "off" rather than as "asked the wrong question".
			concurrent = Find( config, "ConcurrentGC" );

			if ( concurrent is not null )
				line.Append( $", background {Yes( concurrent )}" );

			if ( Find( config, "HeapCount" ) is { } heaps && Convert.ToInt64( heaps ) > 0 )
				line.Append( $", {heaps} heaps" );

			if ( Find( config, "GCGen0MaxBudget" ) is { } budget && Convert.ToInt64( budget ) > 0 )
				line.Append( $", gen0 budget {Convert.ToInt64( budget ) / ( 1024 * 1024 )} MB" );

			//Only meaningful under the server collector, and reporting it beside "workstation" invites the reading that it is doing something here.
			if ( server && Find( config, "GCDynamicAdaptationMode" ) is { } datas )
				line.Append( $", datas {Yes( datas )}" );
		}
		catch ( Exception )
		{
			//A runtime that will not describe its own collector is not a reason to lose the rest of the report.
		}

		line.Append( $", heap {GC.GetTotalMemory( false ) / ( 1024.0 * 1024.0 ):F0} MB" );

		report.AppendLine( line.ToString() );

		//Runtime gives Batch when background GC is off, despite SustainedLowLatency being requested
		if ( concurrent is not null && Convert.ToInt64( concurrent ) == 0 )
		{
			report.AppendLine( "  BACKGROUND GC IS OFF - every gen2 walks the whole heap with the frame " +
				"stopped, and the SustainedLowLatency the engine asks for was refused" );
		}
	}

	//One GC configuration value by name, whatever this runtime happens to capitalise it as, or nothing.
	private static object Find( IReadOnlyDictionary<string, object> config, string name )
	{
		foreach ( var pair in config )
		{
			if ( string.Equals( pair.Key, name, StringComparison.OrdinalIgnoreCase ) )
				return pair.Value;
		}

		return null;
	}

	//DOTNET_/COMPlus_ env vars inherited from the launcher, which override all runtime config
	private static void WriteEnvironment( StringBuilder report )
	{
		var overrides = new List<string>();

		foreach ( System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables() )
		{
			var name = entry.Key?.ToString();
			if ( name is null ) continue;

			if ( !name.StartsWith( "DOTNET_", StringComparison.OrdinalIgnoreCase ) &&
				!name.StartsWith( "COMPlus_", StringComparison.OrdinalIgnoreCase ) )
				continue;

			//The ones that only say where things are, rather than how they run.
			if ( name.Equals( "DOTNET_ROOT", StringComparison.OrdinalIgnoreCase ) ) continue;
			if ( name.Contains( "CLI_TELEMETRY", StringComparison.OrdinalIgnoreCase ) ) continue;
			if ( name.Contains( "NOLOGO", StringComparison.OrdinalIgnoreCase ) ) continue;

			overrides.Add( $"{name}={entry.Value}" );
		}

		if ( overrides.Count == 0 )
		{
			report.AppendLine( "  no runtime overrides in the environment" );
			return;
		}

		overrides.Sort( StringComparer.OrdinalIgnoreCase );

		report.AppendLine( $"  RUNTIME OVERRIDDEN BY THE ENVIRONMENT ({overrides.Count}) - " +
			"these beat the project and the runtime config, and were inherited from whatever launched this:" );

		foreach ( var one in overrides )
			report.AppendLine( $"    {one}" );
	}

	//OS-level process limits such as priority, inherited from the launcher and invisible in the frame
	private static void WriteProcess( StringBuilder report )
	{
		if ( OperatingSystem.IsBrowser() ) return;

		try
		{
			using var self = Process.GetCurrentProcess();

			var priority = self.PriorityClass;
			var cores = Environment.ProcessorCount;

			//The affinity mask as a count, because "0xFF of 16" is a sentence and 0xFF is not.
			var affinity = (long)self.ProcessorAffinity;
			var allowed = System.Numerics.BitOperations.PopCount( (ulong)affinity );

			report.AppendLine( $"  process {priority} priority, {allowed} of {cores} cores, " +
				$"{self.Threads.Count} threads, {self.HandleCount} handles" );

			report.AppendLine( $"  memory working set {self.WorkingSet64 / ( 1024.0 * 1024.0 ):N0} MB, " +
				$"private {self.PrivateMemorySize64 / ( 1024.0 * 1024.0 ):N0} MB" );

			//Named, not compared: the enum is Win32 flags, and BelowNormal's value is larger than Normal's.
			if ( priority is ProcessPriorityClass.Idle or ProcessPriorityClass.BelowNormal )
			{
				report.AppendLine( "  PRIORITY IS BELOW NORMAL - this process is losing the processor to " +
					"everything else on the machine, and nothing in the engine asks for that" );
			}

			if ( allowed < cores )
				report.AppendLine( $"  ONLY {allowed} CORES ALLOWED - something has pinned this process" );
		}
		catch ( Exception )
		{
			//A process that will not describe itself is not worth losing the rest of the report over.
		}
	}

	private static string Yes( object value )
	{
		try
		{
			return Convert.ToInt64( value ) != 0 ? "on" : "off";
		}
		catch ( Exception )
		{
			return value?.ToString() ?? "?";
		}
	}

	//-- the renderer ------------------------------------------------------------

	//The device, and whether the validation layers are in the frame. Layers cost tens of percent, and a run that had them on has measured them.
	private static void WriteRenderer( StringBuilder report )
	{
		if ( !CoreNative.Available )
		{
			report.AppendLine( $"  core not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var validation = CoreRenderDevice.ValidationEnabled;

		report.AppendLine( $"  core {CoreNative.BuildInfo}" );
		report.AppendLine( $"  device {CoreRenderDevice.DeviceName}, vulkan {CoreRenderDevice.ApiVersion}, " +
			$"validation {( validation ? "ON - this run measured the validation layers" : "off" )}" );
	}

	//-- the clocks --------------------------------------------------------------

	//The three clocks together. The cross-check that has to pass before any other number in the file is worth reading, and the one that took three
	//weeks the time it was not made: the simulation ran at twelve percent of real speed and both clocks looked perfectly normal on their own.
	private static void WriteClocks( StringBuilder report, ClockWindow clocks )
	{
		//Against the wall, which is the only one of the three that nothing in this process can wind.
		var loopRatio = clocks.Wall > 0 ? clocks.Loop / clocks.Wall : 0;
		var gameRatio = clocks.Wall > 0 ? clocks.Game / clocks.Wall : 0;

		report.AppendLine( $"  clocks over the window: game {clocks.Game:F2} s, loop {clocks.Loop:F2} s, " +
			$"wall {clocks.Wall:F2} s (loop/wall {loopRatio:F3}, game/wall {gameRatio:F3})" );

		//The loop clock and the wall are the same seconds counted two ways and have no excuse for differing.
		if ( Math.Abs( loopRatio - 1.0 ) > 0.02 )
			report.AppendLine( "  THE LOOP CLOCK HAS DRIFTED FROM THE WALL - everything above is suspect" );

		//The game clock is allowed to be paused or scaled, so it is only worth a word where it
		//ran and ran at the wrong speed - which is the shape the twelve-percent simulation had.
		else if ( clocks.Game > 0.1 && Math.Abs( gameRatio - 1.0 ) > 0.02 )
			report.AppendLine( $"  the simulation clock ran at {gameRatio * 100:F0} % of real time" );
	}
}
