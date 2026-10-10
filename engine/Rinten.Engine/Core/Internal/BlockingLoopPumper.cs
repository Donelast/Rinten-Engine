
using System;

namespace Rinten.Internal;

//Sometimes stuff is blocking that doesn't need to be. We want to keep running the main loop while the function
//is blocking. This should allow the main loop to still be pumped while waiting for that blocking function.
internal static class BlockingLoopPumper
{
	//Used to call things outside of the main frame
	internal static Action PendingFunction { get; set; }

	static Action loopPump;

	/// <summary>Called outside the main game loop.</summary>
	public static void Run( Action pumper )
	{
		if ( PendingFunction == null )
			return;

		loopPump = pumper;

		try
		{
			var f = PendingFunction;
			PendingFunction = null;
			f?.Invoke();
		}
		finally
		{
			loopPump = null;
		}
	}

	/// <summary>Should be called regularly, on the main thread. This should do total nothing if we're not actually in a blocking loop - because loopPump will be null.</summary>
	public static void Pump()
	{
		loopPump?.Invoke();
	}
}
