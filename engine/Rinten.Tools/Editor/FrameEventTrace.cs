using System.Collections.Generic;

namespace Editor;

//-frametrace: times each [EditorEvent.Frame] handler by declaring type and writes it to the log
internal static class FrameEventTrace
{
	private static readonly bool wanted = Rinten.Utility.CommandLine.HasSwitch( "-frametrace" );

	private static readonly Dictionary<System.Type, (long Ticks, long Calls)> spent = new();
	private static readonly Rinten.Diagnostics.Logger log = new( "Perf" );

	private static RealTimeSince sinceSaid;
	private static long frames;
	private static bool armed;

	//Turns the measurement on, once, and says where it goes.
	internal static void Arm()
	{
		if ( !wanted || armed ) return;

		armed = true;
		sinceSaid = 0;

		Rinten.Internal.EventSystem.Timing = Took;

		log.Info( "-frametrace: timing every editor frame event by the type it is declared on" );
	}

	private static void Took( System.Type type, long ticks )
	{
		if ( type is null ) return;

		//One thread runs the editor's frame events, so no lock - unlike the panel tick's, which is walked
		//from more than one and hung a process for twelve minutes when it was written without one.
		spent.TryGetValue( type, out var held );

		spent[type] = ( held.Ticks + ticks, held.Calls + 1 );
	}

	//Says what the last few seconds went on, and starts again.
	internal static void Frame()
	{
		if ( !armed ) return;

		frames++;

		if ( sinceSaid < 5.0f ) return;

		var scale = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		var over = System.Math.Max( 1, frames );

		var worst = new List<(string Name, double Ms, long Calls)>( spent.Count );

		foreach ( var pair in spent )
			worst.Add( (pair.Key.Name, pair.Value.Ticks * scale / over, (long)( pair.Value.Calls / over )) );

		worst.Sort( static ( a, b ) => b.Ms.CompareTo( a.Ms ) );

		log.Info( $"-frametrace: over {over} frames, ms a frame -" );

		for ( var i = 0; i < worst.Count && i < 20; i++ )
			log.Info( $"  {worst[i].Ms:F4} ms  {worst[i].Calls} calls  {worst[i].Name}" );

		spent.Clear();
		frames = 0;
		sinceSaid = 0;
	}
}
