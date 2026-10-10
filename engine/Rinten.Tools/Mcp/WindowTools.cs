using Rinten.UI;
using System;
using System.Text;

namespace Editor.Mcp;

/// <summary>The editor's windows: which are open, what they look like, what a frame of one costs.</summary>
[McpToolset( "window", "The editor's windows - list them, see one as a picture, read what a frame of one costs" )]
internal static class WindowTools
{
	//The widest picture handed back unasked. A full editor window is a few megabytes
	//of png that an agent pays for in context; at this width it still reads.
	const int DefaultMaxWidth = 1600;

	[McpTool.ReadOnly( "window_list" )]
	[Description( "Every window the editor has open - the main window, tool windows like the model editor, popups - with its index and title (either names it to the other window tools), size in pixels, whether it has the keyboard or the pointer, and whether it shows the world." )]
	public static WindowInfo[] WindowList()
	{
		var focused = CoreUiWindow.Focused;
		var pointed = CoreUiWindow.Pointed;

		return CoreUiWindow.AllOpen.Select( ( window, index ) => new WindowInfo
		{
			Index = index,
			Title = window.Title,
			Width = window.Size.Width,
			Height = window.Size.Height,
			Focused = ReferenceEquals( window, focused ),
			Pointed = ReferenceEquals( window, pointed ),
			Minimized = window.IsMinimized,
			ShowsWorld = window.WorldRect is not null,
			Panels = window.Root is { IsValid: true } root ? Count( root ) : 0,
			FramesDrawn = window.FramesDrawn,
		} ).ToArray();
	}

	[McpTool.ReadOnly( "window_screenshot" )]
	[Description( "A picture of an editor window as it stands - its panels, docks, inspectors, text, and the world where a viewport shows one - or of one panel in it. Drawn fresh off the screen, so it works for a window that's covered. The editor's UI, not the game's: for the game or the scene, camera_screenshot and editor_camera_screenshot render those. Pictures wider than maxWidth are scaled down; give a panel id to crop to it and see it at full size." )]
	public static object WindowScreenshot(
		[Description( "Which window: a title substring, or its index from window_list. Empty is the main editor window." )] string window = "",
		[Description( "A panel id from panel_tree or panel_find to crop to. Its window is used, whatever 'window' says." )] string panel = "",
		[Description( "Scale the picture down to at most this wide. Default 1600, max 4096; 0 keeps full size." ), Range( 0, 4096 )] int maxWidth = DefaultMaxWidth )
	{
		CoreUiWindow shell;
		Rect? crop = null;

		if ( !string.IsNullOrWhiteSpace( panel ) )
		{
			var target = PanelHandles.Resolve( panel );

			shell = CoreUiWindow.Holding( target )
				?? throw new Exception( $"Panel {panel} isn't in an editor window - it's the game's. camera_screenshot shows the game's UI." );

			if ( !target.IsVisible )
				throw new Exception( $"Panel {panel} isn't visible - there's nothing to show. panel_inspect says why." );

			crop = target.Box.Rect;
		}
		else
		{
			shell = Resolve( window );
		}

		var bitmap = shell.Shoot()
			?? throw new Exception( $"Window '{shell.Title}' couldn't be drawn - it may be minimized or not yet shown." );

		try
		{
			if ( crop is { } area )
			{
				var (width, height) = shell.Size;
				var bounded = new Rect(
					Math.Clamp( area.Left, 0, width ), Math.Clamp( area.Top, 0, height ),
					Math.Clamp( area.Width, 0, width - Math.Clamp( area.Left, 0, width ) ),
					Math.Clamp( area.Height, 0, height - Math.Clamp( area.Top, 0, height ) ) );

				if ( bounded.Width < 1 || bounded.Height < 1 )
					throw new Exception( $"Panel {panel} lies outside the window ({PanelTools.RectText( area )})." );

				var cropped = bitmap.Crop( bounded );
				bitmap.Dispose();
				bitmap = cropped;
			}

			if ( maxWidth > 0 && bitmap.Width > maxWidth )
			{
				var scaled = bitmap.Resize( maxWidth, Math.Max( 1, bitmap.Height * maxWidth / bitmap.Width ) );
				bitmap.Dispose();
				bitmap = scaled;
			}

			var caption = crop is { } shown
				? $"{shell.Title}, panel {panel} at {PanelTools.RectText( shown )}, {bitmap.Width}x{bitmap.Height}"
				: $"{shell.Title}, {shell.Size.Width}x{shell.Size.Height}" + (bitmap.Width != shell.Size.Width ? $" shown at {bitmap.Width}x{bitmap.Height}" : "");

			return McpResult.Image( bitmap ).WithText( caption );
		}
		finally
		{
			bitmap.Dispose();
		}
	}

	[McpTool.ReadOnly( "frame_stats" )]
	[Description( "What a frame costs, in numbers: the engine's frame time and main thread time, GC and allocation counters, what the GPU holds, and for each window the device's own measure of its last frame - GPU and CPU milliseconds, draw calls, triangles, and - when the renderer is recording them - the zones the time went to. A window that draws the world on a layer of its own reports the layer separately; that's where the scene's draws are. For a rendering change, read these before and after instead of judging a screenshot." )]
	public static FrameStats FrameStats(
		[Description( "Which window's frame to detail: a title substring or index from window_list. Empty details every window." )] string window = "" )
	{
		var windows = string.IsNullOrWhiteSpace( window ) ? CoreUiWindow.AllOpen.ToArray() : [Resolve( window )];
		var memory = CoreRenderDevice.ReadMemory();

		return new FrameStats
		{
			FrameMs = (float)(PerformanceStats.FrameTime * 1000.0),
			Fps = PerformanceStats.FrameTime > 0 ? (float)(1.0 / PerformanceStats.FrameTime) : 0,
			MainThreadMs = PerformanceStats.MainThreadTime,
			GpuFrameMs = PerformanceStats.GpuFrametime,
			FrameRateCap = PerformanceStats.FrameRateCap,
			FrameRateCapSource = PerformanceStats.FrameRateCapSource,
			BytesAllocated = PerformanceStats.BytesAllocated,
			Gen0Collections = PerformanceStats.Gen0Collections,
			Gen1Collections = PerformanceStats.Gen1Collections,
			Gpu = new GpuMemoryStats
			{
				TexturesMb = memory.Textures / 1048576f,
				BuffersMb = memory.Buffers / 1048576f,
				DriverUsedMb = memory.DriverAnswered ? memory.DriverUsed / 1048576f : null,
				DriverBudgetMb = memory.DriverAnswered ? memory.DriverBudget / 1048576f : null,
			},
			Windows = windows.Select( shell =>
			{
				var (frame, layer) = shell.Profiles();

				return new WindowFrameStats
				{
					Title = shell.Title,
					Width = shell.Size.Width,
					Height = shell.Size.Height,
					Frame = Describe( frame ),
					WorldLayer = Describe( layer ),
				};
			} ).ToArray()
		};
	}

	static FrameProfileStats Describe( CoreRenderDevice.FrameProfile? profile )
	{
		if ( profile is not { } p ) return null;

		return new FrameProfileStats
		{
			GpuMs = p.GpuMs,
			CpuMs = p.CpuMs,
			Draws = p.Draws,
			Triangles = p.Triangles,
			DynamicDraws = p.DynamicDraws,
			SkinnedDraws = p.SkinnedDraws,
			Instances = p.Instances,
			Dispatches = p.Dispatches,
			Textures = p.Textures,
			Buffers = p.Buffers,
			Zones = p.Zones.Select( z => $"{new string( ' ', z.Depth * 2 )}{z.Name}: gpu {z.GpuMs:0.###} ms, cpu {z.CpuMs:0.###} ms" ).ToArray()
		};
	}

	//The window a name means: an index from window_list, a title substring, or with nothing the main editor window.
	internal static CoreUiWindow Resolve( string window )
	{
		var open = CoreUiWindow.AllOpen;

		if ( open.Count == 0 )
			throw new Exception( "No windows are open." );

		var wanted = window?.Trim() ?? "";

		if ( wanted.Length == 0 )
		{
			//The editor's own window, or the first open one - the launcher's - without it.
			return EditorWindow?.Shell ?? open[0];
		}

		if ( int.TryParse( wanted, out var index ) )
		{
			return index >= 0 && index < open.Count
				? open[index]
				: throw new Exception( $"There is no window {index} - window_list shows {open.Count}." );
		}

		var matches = open.Where( x => x.Title?.Contains( wanted, StringComparison.OrdinalIgnoreCase ) ?? false ).ToArray();

		return matches.Length switch
		{
			1 => matches[0],
			0 => throw new Exception( $"No window's title contains '{wanted}'. window_list shows them." ),
			_ => matches.FirstOrDefault( x => string.Equals( x.Title, wanted, StringComparison.OrdinalIgnoreCase ) )
				?? throw new Exception( $"'{wanted}' matches {matches.Length} windows: {string.Join( ", ", matches.Select( x => $"'{x.Title}'" ) )}. Give the index from window_list." )
		};
	}

	static int Count( Panel panel )
	{
		var count = 1;

		foreach ( var child in panel.Children )
			count += Count( child );

		return count;
	}
}

//One open window, as window_list reports it.
internal class WindowInfo
{
	public int Index { get; set; }
	public string Title { get; set; }
	public int Width { get; set; }
	public int Height { get; set; }
	public bool Focused { get; set; }
	public bool Pointed { get; set; }
	public bool Minimized { get; set; }

	[Description( "Whether a viewport in it shows the world - a scene view, a model preview." )]
	public bool ShowsWorld { get; set; }
	public int Panels { get; set; }
	public long FramesDrawn { get; set; }
}

//What frame_stats reports.
internal class FrameStats
{
	public float FrameMs { get; set; }
	public float Fps { get; set; }
	public float MainThreadMs { get; set; }
	public float GpuFrameMs { get; set; }
	public float FrameRateCap { get; set; }
	public string FrameRateCapSource { get; set; }
	public long BytesAllocated { get; set; }
	public int Gen0Collections { get; set; }
	public int Gen1Collections { get; set; }
	public GpuMemoryStats Gpu { get; set; }
	public WindowFrameStats[] Windows { get; set; }
}

internal class GpuMemoryStats
{
	public float TexturesMb { get; set; }
	public float BuffersMb { get; set; }
	public float? DriverUsedMb { get; set; }
	public float? DriverBudgetMb { get; set; }
}

internal class WindowFrameStats
{
	public string Title { get; set; }
	public int Width { get; set; }
	public int Height { get; set; }

	[Description( "The window's own frame, as the device measured it. Null before one has finished." )]
	public FrameProfileStats Frame { get; set; }

	[Description( "The world's layer over the window, where the world is drawn on one of its own." )]
	public FrameProfileStats WorldLayer { get; set; }
}

internal class FrameProfileStats
{
	public float GpuMs { get; set; }
	public float CpuMs { get; set; }
	public int Draws { get; set; }
	public long Triangles { get; set; }
	public int DynamicDraws { get; set; }
	public int SkinnedDraws { get; set; }
	public int Instances { get; set; }
	public int Dispatches { get; set; }
	public int Textures { get; set; }
	public int Buffers { get; set; }

	[Description( "Where the time went, in the order the frame drew them, indented by nesting." )]
	public string[] Zones { get; set; }
}
