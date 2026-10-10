using Rinten;

namespace Editor;

//-perfresize: drags the window width back and forth each frame so resize cost can be measured
internal static class PerfResize
{
	//How far the edge travels, in pixels. -perfresize 240 , or the switch on its own for 240.
	private static readonly int amplitude =
		Rinten.Utility.CommandLine.HasSwitch( "-perfresize" )
			? System.Math.Max( 32, Rinten.Utility.CommandLine.GetSwitchInt( "-perfresize", 240 ) )
			: 0;

	//How many pixels a frame. Eight is about what a hand does at sixty hertz; one is the
	//worst case, where every frame is a new size and nothing can be reused between them.
	private static readonly int step =
		System.Math.Max( 1, Rinten.Utility.CommandLine.GetSwitchInt( "-perfresizestep", 8 ) );

	private static int width;
	private static int height;
	private static int travelled;
	private static int direction = -1;
	private static bool started;
	private static long frames;
	private static RealTimeSince sinceSaid;

	[EditorEvent.Frame]
	public static void Frame()
	{
		if ( amplitude <= 0 ) return;

		var editor = Rinten.Internal.GlobalToolsNamespace.EditorWindow;

		if ( !editor.IsValid() ) return;
		if ( editor.Shell is not { } shell ) return;

		if ( !started )
		{
			started = true;

			//A maximized window has no edge to drag: asking it to be another size while it is one either does
			//nothing or unmaximizes it halfway through the run, and both of those measure something else.
			if ( shell.IsMaximized ) shell.Restore();

			var placement = shell.Placement;

			width = placement.Width.CeilToInt();
			height = placement.Height.CeilToInt();

			if ( width <= 0 || height <= 0 )
			{
				started = false;
				return;
			}

			Log.Info( $"-perfresize: sweeping {System.Math.Max( MinimumWidth, width - amplitude )} to {width} wide, " +
				$"{step} px a frame, at {height} tall" );
		}

		travelled += direction * step;

		if ( travelled <= -amplitude )
		{
			travelled = -amplitude;
			direction = 1;
		}
		else if ( travelled >= 0 )
		{
			travelled = 0;
			direction = -1;
		}

		shell.Resize( System.Math.Max( MinimumWidth, width + travelled ), height );

		frames++;

		//Said now and then rather than never: a run whose window never actually changed size - because the window system refused, because it was
		//maximized, because something else is holding the size - would otherwise produce a perfectly ordinary report of an editor sitting still.
		if ( sinceSaid < 2.0f ) return;

		sinceSaid = 0;

		var (actual, _) = shell.Size;

		Log.Info( $"-perfresize: {frames} frames swept, asked for {width + travelled}, window is {actual} px wide" );
	}

	//Narrow enough to be a real change and wide enough that the editor still has its docks in it - a window squeezed to nothing measures the empty case.
	private const int MinimumWidth = 640;
}
