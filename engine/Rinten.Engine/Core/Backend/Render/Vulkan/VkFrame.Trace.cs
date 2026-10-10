namespace Rinten;

//Per-draw recording cost under -drawtrace: call time and render lock wait. Free when off
internal static class DrawTrace
{
	//Which entry point a measurement belongs to.
	internal enum Kind
	{
		Draw,
		DrawObjects,
		DrawPulledIndirect,
		DrawPulledIndexedIndirect,
		DrawMeshIndirect,
		DrawDynamic,
		DrawIndexed,
		DrawPulled,
		DrawSkinned,
		DrawSkinnedAt,
		DrawMorphedIndexed,
		DrawMorphedAt,
		Dispatch,
		DrawBuffers,

		//Phases inside the entries above, also counted in whichever draw hit them
		MainPass,
		ObjectSet,
		ScratchCopy,
		Count,
	}

	//The first of the phases - everything from here is inside a draw above.
	private const Kind FirstPhase = Kind.MainPass;

	internal static readonly bool On = Rinten.Utility.CommandLine.HasSwitch( "-drawtrace" );

	private static readonly long[] calls = new long[(int)Kind.Count];
	private static readonly long[] ticks = new long[(int)Kind.Count];
	private static readonly long[] waited = new long[(int)Kind.Count];

	private static readonly Rinten.Diagnostics.Logger log = new( "Perf" );

	private static long frames;
	private static long saidAt;
	private static bool announced;

	//When the call began, or nought when nothing is being measured.
	internal static long Begin()
		=> On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

	//When the lock was taken - the difference is the wait.
	internal static long Locked( long began )
		=> began == 0 ? 0L : System.Diagnostics.Stopwatch.GetTimestamp();

	//Charges a phase inside a draw - no lock of its own, so no wait.
	internal static void Phase( Kind kind, long began )
	{
		if ( began == 0 ) return;

		ticks[(int)kind] += System.Diagnostics.Stopwatch.GetTimestamp() - began;
		calls[(int)kind]++;
	}

	//Charges the whole call and the wait inside it, and counts it. Said only where a draw
	//was actually recorded: a call that fell out of a guard cost nothing worth averaging in.
	internal static void End( Kind kind, long began, long inside )
	{
		if ( began == 0 ) return;

		ticks[(int)kind] += System.Diagnostics.Stopwatch.GetTimestamp() - began;
		waited[(int)kind] += inside - began;
		calls[(int)kind]++;
	}

	//Says what the last few seconds of recording went on, and starts again. Called where a recording begins, so "a frame" means a frame that drew.
	internal static void Frame()
	{
		if ( !On ) return;

		if ( !announced )
		{
			announced = true;
			saidAt = System.Diagnostics.Stopwatch.GetTimestamp();

			log.Info( "-drawtrace: timing every draw's recording, and the render lock under it" );
			return;
		}

		frames++;

		var now = System.Diagnostics.Stopwatch.GetTimestamp();
		var over = now - saidAt;

		if ( over < System.Diagnostics.Stopwatch.Frequency * 5 ) return;

		var toNs = 1.0e9 / System.Diagnostics.Stopwatch.Frequency;
		var perFrame = System.Math.Max( 1, frames );

		long allCalls = 0, allTicks = 0, allWaited = 0;

		for ( var i = 0; i < (int)FirstPhase; i++ )
		{
			allCalls += calls[i];
			allTicks += ticks[i];
			allWaited += waited[i];
		}

		log.Info( $"-drawtrace: over {perFrame} recordings - " +
			$"{allCalls / (double)perFrame:F1} calls a frame, " +
			$"{allTicks * toNs / 1.0e6 / perFrame:F4} ms a frame, " +
			$"{( allCalls > 0 ? allTicks * toNs / allCalls : 0.0 ):F0} ns a call, " +
			$"{( allTicks > 0 ? 100.0 * allWaited / allTicks : 0.0 ):F0} % of it waiting for the lock" );

		for ( var i = 0; i < (int)Kind.Count; i++ )
		{
			if ( calls[i] == 0 ) continue;

			log.Info( $"  {( i >= (int)FirstPhase ? "of which " : "" )}{(Kind)i,-18} {calls[i] / (double)perFrame,7:F1} a frame  " +
				$"{ticks[i] * toNs / calls[i],7:F0} ns  " +
				$"{waited[i] * toNs / calls[i],7:F0} ns waiting  " +
				$"{ticks[i] * toNs / 1.0e6 / perFrame,7:F4} ms a frame" );

			calls[i] = 0;
			ticks[i] = 0;
			waited[i] = 0;
		}

		frames = 0;
		saidAt = now;
	}
}
