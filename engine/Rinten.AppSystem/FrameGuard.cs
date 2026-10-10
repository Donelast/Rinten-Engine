using Rinten.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Rinten;

//Catches a throwing frame: logs it (rate-limited), ends any open device frame, and keeps looping
internal static class FrameGuard
{
	private static readonly Logger log = new( "AppSystem" );

	//How many times each place has thrown, by what threw and where.
	private static readonly Dictionary<string, long> said = new();

	//After the first, one line every this many.
	private const long Every = 100;

	//Runs one frame. True to keep going - which a frame that threw is, since the next one may well draw.
	internal static bool Survive( Func<bool> frame )
	{
		try
		{
			return frame();
		}
		catch ( Exception e )
		{
			Said( e );
			CloseOpenFrame();

			return true;
		}
	}

	//The same, for a frame whose answer nobody reads.
	internal static void Survive( Action frame )
		=> Survive( () => { frame(); return true; } );

	private static void Said( Exception e )
	{
		var where = e.StackTrace?.Split( '\n' ).FirstOrDefault()?.Trim() ?? "";
		var key = $"{e.GetType().FullName}|{where}";

		said.TryGetValue( key, out var times );
		said[key] = ++times;

		if ( times == 1 )
		{
			log.Error( e, "the frame threw - the engine carried on" );
			return;
		}

		if ( times % Every == 0 )
			log.Warning( $"the same frame fault {times} times now: {e.GetType().Name} at {where}" );
	}

	//Ends a frame the exception left open, so the next one can begin. Anything this throws is on the
	//way out of a failure already; there is nowhere to report it that is not this same log line again.
	private static void CloseOpenFrame()
	{
		try
		{
			if ( CoreRenderDevice.Recording ) CoreRenderDevice.EndFrame();
		}
		catch { }
	}
}
