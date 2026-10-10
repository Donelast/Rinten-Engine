using System;
using System.Diagnostics;

namespace Editor.Mcp;

//Waiting for the editor's frames, from a tool. A tool starts on the main thread and is off it from its first await until it asks to come
//back; the engine's state - a compiler's, a window's, the game's - is the main thread's, so every look at it in between is taken from there.
internal static class Frame
{
	/// <summary>A moment on, then the main thread again: one poll of whatever is being waited for.</summary>
	public static async Task Next()
	{
		await Task.Delay( 4 );
		await MainThread.Wait();
	}

	/// <summary>So many polls, each a frame or so apart.</summary>
	public static async Task Next( int count )
	{
		for ( var i = 0; i < count; i++ ) await Next();
	}

	/// <summary>Polls until <paramref name="done"/> says so, checked on the main thread, and gives up after <paramref name="timeout"/> with <paramref name="gaveUp"/>'s words - written for the agent, saying what was waited for and what to do about it.</summary>
	public static async Task Until( Func<bool> done, TimeSpan timeout, Func<string> gaveUp )
	{
		var started = Stopwatch.StartNew();

		while ( !done() )
		{
			if ( started.Elapsed > timeout )
				throw new TimeoutException( gaveUp() );

			await Next();
		}
	}

	/// <summary>Polls for a length of real time, whatever happens meanwhile.</summary>
	public static async Task For( TimeSpan span )
	{
		var started = Stopwatch.StartNew();

		while ( started.Elapsed < span )
		{
			await Next();
		}
	}
}
