namespace Rinten;

/// <summary>Provides utilities for working with threads, particularly for identifying and asserting code is running on the main thread.</summary>
public static class ThreadSafe
{
	[ThreadStatic]
	static bool isMainThread;

	/// <summary>Gets the current thread's managed thread ID.</summary>
	public static int CurrentThreadId => System.Threading.Thread.CurrentThread.ManagedThreadId;

	/// <summary>Gets the current thread's name, or null if unnamed.</summary>
	public static string CurrentThreadName => System.Threading.Thread.CurrentThread.Name;

	/// <summary>Returns true if currently executing on the main thread.</summary>
	public static bool IsMainThread => isMainThread;

	//Marks the current thread as the main thread. This is called internally during engine initialization.
	internal static void MarkMainThread()
	{
		isMainThread = true;
	}

	/// <summary>Throws an exception if not called from the main thread. Useful for enforcing thread safety on main-thread-only APIs.</summary>
	public static void AssertIsMainThread( [System.Runtime.CompilerServices.CallerMemberName] string memberName = "" )
	{
		if ( IsMainThread ) return;

		throw new System.Exception( $"{memberName} must be called on the main thread!" );
	}

	/// <summary>Throws an exception if called from the main thread. Useful for enforcing that blocking operations don't run on the main thread.</summary>
	public static void AssertIsNotMainThread()
	{
		if ( !IsMainThread ) return;

		throw new System.Exception( "This function must not be called on the main thread!" );
	}

}
