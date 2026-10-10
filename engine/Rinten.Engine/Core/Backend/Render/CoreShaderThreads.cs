using System.Collections.Concurrent;
using System.Threading;

namespace Rinten;

//A few compile threads, each with its own Slang session (~220 MB), which exit when idle
internal static class CoreShaderThreads
{
	//How many compiles run at once. Four is ~4x on the 780M's sixteen threads and under a gigabyte of sessions.
	private static readonly int Most = Math.Clamp( Environment.ProcessorCount / 2, 1, 4 );

	//How long a thread waits for more before it lets its session go.
	private static readonly TimeSpan Idle = TimeSpan.FromSeconds( 15 );

	private static readonly BlockingCollection<Action> queue = new();
	private static readonly object gate = new();
	private static int running;
	private static int waiting;

	//Whether this thread is one of these - a compile asked for from one runs where it is.
	[ThreadStatic]
	private static bool isWorker;

	/// <summary>Runs a compile on one of the threads and hands back what it made. Asked from one of them it runs there and then, so a compile that asks for another can never wait on a thread that is waiting on it.</summary>
	public static Task<T> Run<T>( Func<T> compile )
	{
		if ( isWorker ) return Task.FromResult( compile() );

		//Browser: compile inline, since a page can neither start threads nor wait on them
		if ( OperatingSystem.IsBrowser() ) return Task.FromResult( compile() );

		var done = new TaskCompletionSource<T>( TaskCreationOptions.RunContinuationsAsynchronously );

		queue.Add( () =>
		{
			try { done.SetResult( compile() ); }
			catch ( Exception e ) { done.SetException( e ); }
		} );

		lock ( gate )
		{
			//Another thread only where every one there is has something to do.
			if ( waiting == 0 && running < Most )
			{
				running++;
				new Thread( Work ) { IsBackground = true, Name = "Shader compiles" }.Start();
			}
		}

		return done.Task;
	}

	private static void Work()
	{
		isWorker = true;

		try
		{
			while ( true )
			{
				Interlocked.Increment( ref waiting );
				var took = queue.TryTake( out var job, Idle );
				Interlocked.Decrement( ref waiting );

				if ( took )
				{
					job();
					continue;
				}

				//Idle. Checked under the lock Run starts threads under, so a job added while this one was deciding is not left with nobody.
				lock ( gate )
				{
					if ( queue.Count > 0 ) continue;
					running--;
					return;
				}
			}
		}
		finally
		{
			CoreShaderCompiler.ReleaseThread();

			//The last of them gone, and with it every session: what they held is free, but the C runtime keeps a freed arena resident
			//until asked - measured, the process stood 470 MB larger after the threads had ended than before they began. See NativeHeap.
			bool last;
			lock ( gate ) last = running == 0;
			if ( last ) NativeHeap.Trim();
		}
	}
}
