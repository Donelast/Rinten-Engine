using System.Diagnostics.Tracing;

namespace Rinten.Diagnostics;

//Emits engine profiler zones as ETW events for WPA/PerfView; free unless a session is recording
[System.Diagnostics.Tracing.EventSource( Name = ProviderName )]
[SkipHotload]
internal sealed class EngineEventSource : EventSource
{
	/// <summary>What the provider is called to every tool outside this process. An EventSource's GUID is the hash of this string, so it is the whole of the contract - changing it silently breaks every saved profile and script.</summary>
	public const string ProviderName = "Rinten-Engine";

	public static readonly EngineEventSource Instance = new();

	private EngineEventSource() { }

	/// <summary>Which parts a session asked for, so a trace can take the frames without the several hundred zones a frame that come with them.</summary>
	public static class Keywords
	{
		/// <summary>One start/stop pair per instrumented span.</summary>
		public const EventKeywords Zones = (EventKeywords)0x1;

		/// <summary>One event per frame, and nothing else.</summary>
		public const EventKeywords Frames = (EventKeywords)0x2;
	}

	//-- zones ------------------------------------------------------------------

	/// <summary>A span opening, named the same as it is named in our own report and in the renderer's debug labels - one name for one thing, in every tool.</summary>
	[System.Diagnostics.Tracing.Event( 1, Level = EventLevel.Informational, Keywords = Keywords.Zones,
		Task = (EventTask)1, Opcode = EventOpcode.Start )]
	public void ZoneStart( string name, int frame )
	{
		if ( !IsEnabled( EventLevel.Informational, Keywords.Zones ) ) return;

		WriteEvent( 1, name, frame );
	}

	/// <summary>The same span closing, carrying what it cost so a reader does not have to subtract two timestamps to find out.</summary>
	[System.Diagnostics.Tracing.Event( 2, Level = EventLevel.Informational, Keywords = Keywords.Zones,
		Task = (EventTask)1, Opcode = EventOpcode.Stop )]
	public void ZoneStop( string name, long elapsedTicks )
	{
		if ( !IsEnabled( EventLevel.Informational, Keywords.Zones ) ) return;

		WriteEvent( 2, name, elapsedTicks );
	}

	//-- frames -----------------------------------------------------------------

	/// <summary>One finished frame, with the GPU frame number beside it since the two don't advance together</summary>
	[System.Diagnostics.Tracing.Event( 3, Level = EventLevel.Informational, Keywords = Keywords.Frames )]
	public unsafe void Frame( int frame, double frameMs, long bytesAllocated, int gpuFrame )
	{
		if ( !IsEnabled( EventLevel.Informational, Keywords.Frames ) ) return;

		EventData* data = stackalloc EventData[4];

		data[0].DataPointer = (IntPtr)( &frame );
		data[0].Size = sizeof( int );
		data[1].DataPointer = (IntPtr)( &frameMs );
		data[1].Size = sizeof( double );
		data[2].DataPointer = (IntPtr)( &bytesAllocated );
		data[2].Size = sizeof( long );
		data[3].DataPointer = (IntPtr)( &gpuFrame );
		data[3].Size = sizeof( int );

		WriteEventCore( 3, 4, data );
	}

	/// <summary>The clocks, together, once a second. Part III's last row, and the one that invalidates every other number in a run when it does not hold: a simulation clock that has drifted from the wall means the frames being measured are not the frames being lived.</summary>
	[System.Diagnostics.Tracing.Event( 4, Level = EventLevel.Informational, Keywords = Keywords.Frames )]
	public unsafe void Clocks( double gameNow, double realNow, double wallNow )
	{
		if ( !IsEnabled( EventLevel.Informational, Keywords.Frames ) ) return;

		EventData* data = stackalloc EventData[3];

		data[0].DataPointer = (IntPtr)( &gameNow );
		data[0].Size = sizeof( double );
		data[1].DataPointer = (IntPtr)( &realNow );
		data[1].Size = sizeof( double );
		data[2].DataPointer = (IntPtr)( &wallNow );
		data[2].Size = sizeof( double );

		WriteEventCore( 4, 3, data );
	}
}
