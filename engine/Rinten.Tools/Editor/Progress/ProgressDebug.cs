using System.Text;

namespace Editor;

//Console commands to add a test job and report the bar's state, to debug an empty bar
internal static class ProgressDebug
{
	//The made-up job, while one is running.
	static EditorProgress.Job job;

	static RealTimeSince started;

	static float length;

	[ConCmd( "progress_test", Help = "Put a made-up job on the editor's progress bar for a few seconds" )]
	static void Test( float seconds = 6f )
	{
		length = seconds > 0.5f ? seconds : 6f;

		job?.Finish( null, 0f );

		job = EditorProgress.Start( "Test job", "science" );
		job.Subtitle = "pretending to do something";
		job.Fraction = 0f;
		job.Cancelled = () => { job?.Fail( "stopped" ); job = null; };

		started = 0;
	}

	[ConCmd( "progress_test_slow", Help = "The same, with no idea how far along it is - the sliding segment" )]
	static void TestSlow( float seconds = 6f )
	{
		Test( seconds );

		job.Fraction = -1f;
	}

	[ConCmd( "progress_test_fail", Help = "A job that fails, to see the bar go red" )]
	static void TestFail()
	{
		EditorProgress.Start( "Test job", "science" ).Fail( "this is what a failure looks like", 6f );
	}

	[EditorEvent.Frame]
	static void Frame()
	{
		if ( job is null ) return;

		if ( job.Fraction >= 0f )
			job.Fraction = ( (float)started / length ).Clamp( 0f, 1f );

		if ( started < length ) return;

		job.Finish( "done" );
		job = null;
	}

	[ConCmd( "progress_status", Help = "What the editor's progress bar is doing, and why it is not on the screen if it is not" )]
	static void Status()
	{
		var log = EditorProgress.Log;

		log.Info( $"ticks: {EditorProgress.Ticks}" );

		var (shown, others) = EditorProgress.Pick();

		log.Info( shown is null
			? $"no job to show ({others} running)"
			: $"showing '{shown.Title}' - {shown.Subtitle} - {shown.Fraction:0.00} - {shown.State}, {others} others" );

		var window = EditorMainWindow.Current;

		log.Info( $"window: {(window.IsValid() ? "yes" : "no")}, root: {(window?.Shell?.Root is null ? "no" : "yes")}" );

		var bar = EditorProgress.Bar;

		if ( bar is null )
		{
			log.Info( "no bar panel has been made" );
			return;
		}

		var classes = new StringBuilder();

		foreach ( var name in bar.Class ) classes.Append( name ).Append( ' ' );

		log.Info( $"bar: parent {bar.Parent?.GetType().Name ?? "none"}, box {bar.Box.Rect}, " +
			$"visible {bar.IsVisible}, classes [{classes.ToString().Trim()}]" );
	}
}
