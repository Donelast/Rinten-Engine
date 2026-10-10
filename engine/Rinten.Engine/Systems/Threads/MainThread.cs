using System.Threading;
using System.Threading.Channels;

namespace Rinten;

/// <summary>Utility functions that revolve around the main thread</summary>
public static class MainThread
{
	static Channel<IDisposable> Disposables = Channel.CreateUnbounded<IDisposable>();
	static Channel<Action> Actions = Channel.CreateUnbounded<Action>();

	//What finalizers handed over - see InFinalizer.
	static readonly System.Collections.Concurrent.ConcurrentQueue<Action> Finalized = new();

	[ThreadStatic] static int finalizing;

	//Defers a finalizer's main-thread work to the main loop, since on web it runs mid-allocation
	internal static FinalizerScope InFinalizer()
	{
		finalizing++;
		return default;
	}

	internal readonly struct FinalizerScope : IDisposable
	{
		public void Dispose() => finalizing--;
	}

	/// <summary>Wait to execute on the main thread</summary>
	public static SyncTask Wait()
	{
		return new SyncTask( SyncContext.MainThread, allowSynchronous: true );
	}


	internal static void QueueDispose( IDisposable disposable )
	{
		if ( disposable is null )
			return;

		if ( finalizing > 0 )
		{
			Finalized.Enqueue( disposable.Dispose );
			return;
		}

		if ( ThreadSafe.IsMainThread )
		{
			disposable.Dispose();
			return;
		}

		Disposables.Writer.TryWrite( disposable );
	}

	//Run a function on the main thread and wait for the result.
	internal static T Run<T>( int millisecondsTimeout, Func<T> func )
	{
		if ( ThreadSafe.IsMainThread )
			return func();

		T r = default;
		using var reset = new ManualResetEvent( false );

		Queue( () =>
		{
			try
			{
				r = func();
			}
			finally
			{
				reset.Set();
			}
		} );

		if ( !reset.WaitOne( millisecondsTimeout ) )
		{
			return default;
		}
		return r;
	}

	internal static void RunQueues()
	{
		ThreadSafe.AssertIsMainThread();

		while ( Disposables.Reader.TryRead( out var disposable ) )
		{
			disposable.Dispose();
		}

		RunMainThreadQueues();
	}

	/// <summary>When running in another thread you can queue a method to run in the main thread. If you are on the main thread we will execute the method immediately and return.</summary>
	public static void Queue( Action method )
	{
		if ( finalizing > 0 )
		{
			Finalized.Enqueue( method );
			return;
		}

		if ( ThreadSafe.IsMainThread )
		{
			method();
			return;
		}

		Actions.Writer.TryWrite( method );
	}

	//Run queued actions on the main thread
	internal static void RunMainThreadQueues()
	{
		ThreadSafe.AssertIsMainThread();

		while ( Finalized.TryDequeue( out var finalized ) )
		{
			try
			{
				finalized();
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, e.Message );
			}
		}

		while ( Actions.Reader.TryRead( out var action ) )
		{
			try
			{
				action();
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, e.Message );
			}
		}
	}
}
