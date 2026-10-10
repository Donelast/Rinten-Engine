namespace Rinten;

//The renderer's own profiler: GPU timestamps per zone, CPU time and counters, read back a frame late
internal static class CoreRenderProfiler
{
	[ConVar( "render_profile", ConVarFlags.Protected,
		Help = "Draw render timings over viewports our core draws: GPU/CPU per zone, draws, memory" )]
	public static bool Enabled { get; set; } = false;

	//The viewport the scene view drew last, so the dump command has somewhere to read from without asking the user for a handle.
	internal static ulong LastViewport;

	//Record GPU zones only when something will read them; they cost ~3% of the editor main thread
	internal static bool ZonesWanted => Enabled || LogEvery > 0.0f || Diagnostics.PerfReport.Measuring;

	/// <summary>How often to write the renderer's own numbers to the log, in seconds.</summary>
	[ConVar( "r_report_every", ConVarFlags.Protected, Min = 0.0f, Max = 600.0f,
		Help = "Write the renderer's numbers to the log this often, in seconds (0 off)." )]
	public static float LogEvery { get; set; } = 0.0f;

	private static RealTimeSince sinceLogged;


	private static long cullsWas, listsWas, materialsWas;

	//Logs one block of numbers at most every r_report_every seconds; cull counts are since the last block
	internal static void LogPeriodically( ulong viewport )
	{
		if ( LogEvery <= 0.0f || sinceLogged < LogEvery ) return;

		sinceLogged = 0;

		var culls = CoreMeshRender.Culls - cullsWas;
		var lists = CoreMeshRender.ListDraws - listsWas;
		var materials = CoreMeshRender.MaterialDraws - materialsWas;

		cullsWas = CoreMeshRender.Culls;
		listsWas = CoreMeshRender.ListDraws;
		materialsWas = CoreMeshRender.MaterialDraws;

		if ( CoreRenderDevice.ReadProfile( viewport ) is not { } profile ) return;

		//Triangles are CPU-counted only; indirect draws are counted on the GPU, so report them separately
		Log.Info( $"report: gpu {profile.GpuMs:F2} ms, cpu record {profile.CpuMs:F2} ms, " +
			$"{profile.Draws} draws ({profile.Indirect} decided on the card), " +
			$"{profile.Instances} copies and {FormatCount( profile.Triangles )} tris counted here" );

		if ( culls > 0 )
			Log.Info( $"report: {culls} culls, {lists} list draws, {materials} material passes since the last report" );
		else
			Log.Info( "report: the mesh passes did not run - nothing clustered in view, or they did not compile" );

		foreach ( var zone in profile.Zones )
		{
			Log.Info( $"report:   {new string( ' ', zone.Depth * 2 )}{zone.Name,-20} " +
				$"gpu {zone.GpuMs,7:F3} ms   cpu {zone.CpuMs,7:F3} ms" );
		}
	}

	//One line of the overlay: what to say, and how long a bar to draw beside it. A bar below zero means no bar - a heading or a counter line.
	internal readonly record struct Line( string Text, float BarMs, int Depth );

	//Rebuilt a few times a second: every distinct string becomes a cached text texture
	internal static Line[] OverlayLines( ulong viewport )
	{
		if ( refreshAge < RefreshInterval && cachedLines is not null && cachedViewport == viewport )
			return cachedLines;

		cachedViewport = viewport;
		refreshAge = 0;
		cachedLines = Build( CoreRenderDevice.ReadProfile( viewport ) );

		return cachedLines;
	}

	private const float RefreshInterval = 0.25f;

	private static Line[] cachedLines;
	private static ulong cachedViewport;
	private static RealTimeSince refreshAge;

	private static Line[] Build( CoreRenderDevice.FrameProfile? read )
	{
		if ( read is not { } profile )
			return new[] { new Line( "render profiler: waiting for a finished frame", -1, 0 ) };

		var lines = new System.Collections.Generic.List<Line>
		{
			new( $"gpu {profile.GpuMs:F2} ms · cpu record {profile.CpuMs:F2} ms", profile.GpuMs, 0 ),
			new( $"draws {profile.Draws} ({profile.SkinnedDraws} skinned, {profile.DynamicDraws} dynamic, {profile.Instances} instanced) · {FormatCount( profile.Triangles )} tris", -1, 0 ),
			new( $"textures {FormatBytes( profile.TextureBytes )} ({profile.Textures}) · buffers {FormatBytes( profile.BufferBytes )} ({profile.Buffers}) · scratch {FormatBytes( profile.ScratchBytes )}", -1, 0 ),
			new( $"redundant binds skipped {profile.SkippedBinds}", -1, 0 ),
		};

		foreach ( var zone in profile.Zones )
		{
			lines.Add( new Line( $"{zone.Name,-18} {zone.GpuMs,6:F2} ms gpu {zone.CpuMs,6:F2} ms cpu",
				zone.GpuMs, zone.Depth + 1 ) );
		}

		if ( profile.ZonesDropped > 0 )
			lines.Add( new Line( $"{profile.ZonesDropped} zones dropped - too many for one frame", -1, 0 ) );

		return lines.ToArray();
	}

	private static string FormatBytes( long bytes ) => bytes switch
	{
		>= 1024 * 1024 * 1024 => $"{bytes / ( 1024.0 * 1024.0 * 1024.0 ):F1} GB",
		>= 1024 * 1024 => $"{bytes / ( 1024.0 * 1024.0 ):F1} MB",
		>= 1024 => $"{bytes / 1024.0:F1} KB",
		_ => $"{bytes} B",
	};

	private static string FormatCount( long count ) => count switch
	{
		>= 1000000 => $"{count / 1000000.0:F2}M",
		>= 1000 => $"{count / 1000.0:F1}k",
		_ => count.ToString(),
	};

	[ConCmd( "render_profile_dump", ConVarFlags.Protected,
		Help = "Print one measured frame of our renderer to the log" )]
	public static void Dump()
	{
		if ( LastViewport == 0 || CoreRenderDevice.ReadProfile( LastViewport ) is not { } profile )
		{
			Log.Info( "render profiler: no finished frame to read - is our renderer drawing anything?" );
			return;
		}

		if ( !ZonesWanted )
			Log.Info( "render profiler: zones are recorded only while render_profile or r_report_every is on - the frame's totals follow" );

		Log.Info( $"frame: gpu {profile.GpuMs:F2} ms, cpu record {profile.CpuMs:F2} ms" );
		Log.Info( $"draws: {profile.Draws} ({profile.SkinnedDraws} skinned, {profile.DynamicDraws} dynamic, {profile.Instances} copies from instanced calls), {FormatCount( profile.Triangles )} triangles, {profile.SkippedBinds} redundant binds skipped" );
		Log.Info( $"memory: textures {FormatBytes( profile.TextureBytes )} in {profile.Textures}, buffers {FormatBytes( profile.BufferBytes )} in {profile.Buffers}, frame scratch {FormatBytes( profile.ScratchBytes )}" );

		foreach ( var zone in profile.Zones )
		{
			var indent = new string( ' ', zone.Depth * 2 );
			Log.Info( $"  {indent}{zone.Name,-20} gpu {zone.GpuMs,7:F3} ms   cpu {zone.CpuMs,7:F3} ms" );
		}

		if ( profile.ZonesDropped > 0 )
			Log.Info( $"  ({profile.ZonesDropped} zones dropped)" );
	}
}
