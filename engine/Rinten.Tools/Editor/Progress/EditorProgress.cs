using System;

namespace Editor;

/// <summary>The editor's single progress bar (bottom left), showing the newest running job and a count</summary>
public static class EditorProgress
{
	/// <summary>How a job is going, which is the bar's colour.</summary>
	public enum State
	{
		/// <summary>Still going.</summary>
		Running,

		/// <summary>Finished, and it worked.</summary>
		Done,

		/// <summary>Finished, and it did not.</summary>
		Failed,
	}

	/// <summary>Something long enough to be worth showing. Started with <see cref="Start"/>, kept up to date while it runs, and finished - or disposed - when it is over.</summary>
	public sealed class Job : IDisposable
	{
		/// <summary>What it is, in two or three words.</summary>
		public string Title { get; set; }

		/// <summary>What it is doing right now, under the title.</summary>
		public string Subtitle { get; set; }

		/// <summary>The icon on the left - a material icon's name, as everywhere else in the editor.</summary>
		public string Icon { get; set; } = "hourglass_bottom";

		/// <summary>How far along, nought to one - or negative where nobody knows, which the bar draws as a segment sliding back and forth rather than a lie.</summary>
		public float Fraction { get; set; } = -1f;

		/// <summary>How it is going.</summary>
		public State State { get; private set; }

		/// <summary>What a press on the bar does, where it does anything.</summary>
		public Action Pressed { get; set; }

		/// <summary>Asks it to stop. The bar has a cross on it while this is set, and nothing else does - a job nobody can stop should not offer.</summary>
		public Action Cancelled { get; set; }

		/// <summary>Whether it has finished, one way or the other.</summary>
		public bool IsDone => State != State.Running;

		//Right-clicked away, or never wanted on the screen at all.
		internal bool Muted;

		//When it leaves the bar, once it is done.
		internal RealTimeUntil Leaves;

		//Which order they arrived in - the newest is the one that is shown.
		internal long Sequence;

		/// <summary>It is over and it worked. The bar holds what it says for a moment and slides away.</summary>
		public void Finish( string message = null, float linger = 1.2f )
		{
			End( State.Done, message, linger );
		}

		/// <summary>It went wrong. Red, and on the screen long enough to be read.</summary>
		public void Fail( string message, float linger = 0f )
		{
			End( State.Failed, message, linger > 0f ? linger : EditorPreferences.ErrorNotificationTimeout );
		}

		void End( State state, string message, float linger )
		{
			if ( message is not null ) Subtitle = message;

			//A job that finished says so: a bar fading out two thirds full reads as one that gave up.
			if ( Fraction >= 0f ) Fraction = state == State.Done ? 1f : Fraction;

			State = state;
			Leaves = linger;

			//Said when it ends as well as when it starts: a bar that stays up is otherwise indistinguishable from a job nobody finished.
			Log.Info( $"job ended: {Title} - {state}, leaves in {linger:0.#} s{( string.IsNullOrEmpty( Subtitle ) ? "" : $" ({Subtitle})" )}" );
		}

		/// <summary>The end of a <c>using</c>: the same as finishing it quietly.</summary>
		public void Dispose()
		{
			if ( IsDone ) return;

			Finish( null, 0.8f );
		}

		//Takes it off the bar now, without waiting for it to linger.
		internal void Dismiss()
		{
			Muted = true;
			Leaves = 0;
		}
	}

	//Every job, running or still lingering. Only ever touched under padlock.
	static readonly List<Job> jobs = new();

	//A job is started from whatever thread wanted one - an import, a scatter, a compile - and read on the main thread by the bar.
	static readonly object padlock = new();

	static long sequence;

	//The bar itself, made the first time there is anything to show.
	static EditorProgressBar bar;

	//The bar, for whoever is asking what state it is in - see ProgressDebug.
	internal static EditorProgressBar Bar => bar;

	//How many times the tick has run, which is how to tell it runs at all.
	internal static long Ticks;

	public static readonly Logger Log = new( "Progress" );

	//The last thing that stopped the bar being made, so it is said once rather than every frame.
	static string blocked;

	/// <summary>Starts a job and hands it back. Keep it up to date while it runs and finish it - or dispose it - when it is over; a job nobody finishes stays on the bar.</summary>
	public static Job Start( string title, string icon = null )
	{
		var job = new Job
		{
			Title = title,
			Sequence = System.Threading.Interlocked.Increment( ref sequence ),
		};

		if ( icon is not null ) job.Icon = icon;

		lock ( padlock ) jobs.Add( job );

		Log.Info( $"job started: {title}" );

		if ( Rinten.ThreadSafe.IsMainThread ) Tick();

		return job;
	}

	//What the bar should be showing, and how many other jobs are running behind it. Clears out what has finished on the way past.
	internal static (Job Job, int Others) Pick()
	{
		Job shown = null;
		int running = 0;

		lock ( padlock )
		{
			for ( int i = jobs.Count - 1; i >= 0; i-- )
			{
				var job = jobs[i];

				if ( job.IsDone && job.Leaves < 0 )
				{
					jobs.RemoveAt( i );
					continue;
				}

				if ( !job.IsDone ) running++;

				if ( job.Muted ) continue;

				//The newest thing to happen is the one worth looking at, and one still running outranks one that has finished and is only waiting to leave.
				if ( shown is null
					|| (shown.IsDone && !job.IsDone)
					|| (shown.IsDone == job.IsDone && job.Sequence > shown.Sequence) )
				{
					shown = job;
				}
			}
		}

		if ( shown is { IsDone: false } ) running--;

		return (shown, Math.Max( running, 0 ));
	}

	/// <summary>Makes sure the bar exists while there is anything to show.</summary>
	[EditorEvent.Frame]
	public static void Tick()
	{
		Ticks++;

		CompileProgress.Tick();

		if ( bar is { IsValid: false } ) bar = null;

		var (job, _) = Pick();

		//Its own window, where the desktop gives us one: a progress bar is for somebody who has gone to do
		//something else, so it belongs on the screen rather than inside a window they are not looking at.
		if ( EditorProgressWindow.Possible )
		{
			bar ??= EditorProgressWindow.Open();

			if ( bar is not null )
			{
				EditorProgressWindow.Shown( job is not null );
				return;
			}
		}

		if ( job is null ) return;

		if ( bar is not null ) return;

		var window = EditorMainWindow.Current;

		if ( !window.IsValid() )
		{
			Blocked( "there is no editor window yet" );
			return;
		}

		var root = window.Shell?.Root;

		if ( root is null )
		{
			Blocked( "the editor window has no panel root yet" );
			return;
		}

		var layer = EditorOverlay.For( root );

		if ( layer is null )
		{
			Blocked( "the window has no overlay layer" );
			return;
		}

		bar = new EditorProgressBar();

		layer.AddChild( bar );

		blocked = null;

		//Trace: it is said on every start and only matters to somebody asking where the bar went.
		Log.Trace( $"the progress bar is on the editor window's overlay (job: {job.Title}) - " +
			$"{CoreWindow.WindowSystem} has no window that floats above the screen" );
	}

	//Why there is nothing on the screen although there is something to show - once per reason, not once a frame.
	static void Blocked( string why )
	{
		if ( blocked == why ) return;

		blocked = why;

		Log.Warning( $"a job is running and the bar cannot be made: {why}" );
	}

	//Takes a job off the bar without waiting for it - the right click on it.
	internal static void Dismiss( Job job ) => job?.Dismiss();
}
