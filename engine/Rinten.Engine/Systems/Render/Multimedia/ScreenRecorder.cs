using NativeEngine;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Rinten.Audio;

namespace Rinten;

//Records presented frames and the game mix to video; non-blocking, drops frames if encoder is busy
internal static class ScreenRecorder
{
	private static bool _isRecording;
	private static string _filename;
	private static VideoWriter _videoWriter;

	/// <summary>Gets whether a recording is currently in progress.</summary>
	public static bool IsRecording() => _isRecording;

	[ConVar( "video_bitrate", Min = 1, Max = 100, Help = "Bit rate for video recorder (in Mbps)" )]
	public static int VideoBitRate { get; set; } = 10;
	[ConVar( "video_framerate", Min = 1, Max = 1000, Help = "Frame rate for screen recording" )]
	public static int VideoFrameRate { get; set; } = 60;
	[ConVar( "video_Scale", Min = 1, Max = 800, Help = "Scale percentage for video recorder" )]
	public static float VideoScale { get; set; } = 100f;
	[ConVar( "video_codec", Help = "Video codec for screen recording" )]
	public static VideoWriter.Codec VideoCodec { get; set; } = VideoWriter.Codec.AV1;
	[ConVar( "video_audio_codec", Help = "Audio codec for screen recording" )]
	public static VideoWriter.AudioCodec AudioCodec { get; set; } = VideoWriter.AudioCodec.Opus;

	//One frame's worth of what was taken, on its way to the encoder.
	private sealed class Picture
	{
		public byte[] Window;
		public int WindowWidth;
		public int WindowHeight;
		public bool WindowBlueFirst;

		public byte[] Layer;
		public int LayerWidth;
		public int LayerHeight;
		public bool LayerBlueFirst;
		public int LayerX;
		public int LayerY;

		public TimeSpan Time;
	}

	//What the encoder thread is given: pictures from the main thread and sound from the mixer's, and the order they came in is the order they go.
	private static readonly ConcurrentQueue<Picture> pictures = new();
	private static readonly ConcurrentQueue<(float[] Samples, int Length)> sounds = new();
	private static readonly AutoResetEvent wake = new( false );

	private static Thread encoderThread;
	private static volatile bool stopping;

	private static readonly Stopwatch clock = new();
	private static double nextPicture;
	private static int dropped;
	private static ulong recordedWindow;

	//Pictures waiting for the encoder beyond which another is dropped rather than queued: three screens is two frames' grace for a slow one.
	private const int MaxWaiting = 3;

	/// <summary>Starts recording to the specified file.</summary>
	[ConCmd( "video" )]
	public static bool StartRecording()
	{
		if ( _isRecording )
		{
			StopRecording();
			return false;
		}

		if ( !FFmpeg.IsAvailable )
		{
			Log.Warning( $"video: cannot record - {FFmpeg.Unavailable}" );
			return false;
		}

		//The encoder thread of the last recording may still be finishing its file.
		if ( encoderThread is { IsAlive: true } )
		{
			Log.Warning( "video: the last recording is still being written - try again in a moment" );
			return false;
		}

		var window = CoreUiWindow.MainSwapchain;

		if ( window == 0 || Application.IsHeadless )
		{
			Log.Warning( "video: cannot record - there is no window" );
			return false;
		}

		CoreRender.KeepsWindowCaptures = true;
		CoreUiWindow.RepaintAll();

		var (width, height) = CoreRender.WindowCaptureSize( window );

		if ( width <= 0 || height <= 0 )
		{
			CoreRender.KeepsWindowCaptures = false;
			Log.Warning( "video: cannot record - the window cannot be read back" );
			return false;
		}

		var scale = Math.Clamp( VideoScale, 1.0f, 800.0f ) / 100.0f;

#pragma warning disable CS0618 //The codecs that are gone are still what somebody may have set, and map to AV1.
		var codec = VideoCodec is VideoWriter.Codec.H264 or VideoWriter.Codec.H265 or VideoWriter.Codec.VP8 ? VideoWriter.Codec.AV1 : VideoCodec;
#pragma warning restore CS0618

		var container = codec == VideoWriter.Codec.WebP ? VideoWriter.Container.WebP : VideoWriter.Container.MP4;

		var config = new VideoWriter.Config
		{
			Width = Math.Max( (int)( width * scale ) & ~1, 2 ),
			Height = Math.Max( (int)( height * scale ) & ~1, 2 ),
			FrameRate = Math.Clamp( VideoFrameRate, 1, 1000 ),
			Bitrate = Math.Clamp( VideoBitRate, 1, 100 ),
			Codec = codec,
			Container = container,
			Preset = VideoWriter.EncodingPreset.Fast,
			AudioCodec = AudioCodec,
		};

		_filename = ScreenCaptureUtility.GenerateScreenshotFilename( container == VideoWriter.Container.WebP ? "webp" : "mp4" );

		var writer = new VideoWriter( System.IO.Path.GetFullPath( _filename ), config, withSound: true );

		if ( !writer.IsWriting )
		{
			writer.Dispose();
			CoreRender.KeepsWindowCaptures = false;
			Log.Warning( $"video: cannot record to {_filename}" );
			return false;
		}

		_videoWriter = writer;
		recordedWindow = window;
		stopping = false;
		dropped = 0;
		nextPicture = 0.0;
		pictures.Clear();
		sounds.Clear();

		encoderThread = new Thread( Encode )
		{
			IsBackground = true,
			Name = "Video record",
		};

		encoderThread.Start();

		clock.Restart();
		_isRecording = true;

		Log.Info( $"Video recording started: {_filename}" );
		return true;
	}

	/// <summary>Stops the current recording.</summary>
	public static void StopRecording()
	{
		if ( !_isRecording ) return;

		_isRecording = false;
		CoreRender.KeepsWindowCaptures = false;

		//The encoder thread writes what is still queued, finishes the file and lets go of the writer; the main thread does not wait for it.
		stopping = true;
		wake.Set();

		if ( dropped > 0 )
			Log.Info( $"video: {dropped} pictures were dropped - the encoder could not keep up at {VideoFrameRate} fps" );
	}

	//Once a frame on the main thread, from the start of the loop: the newest finished copy of the window
	//- and of the world's layer, where there is one - handed to the encoder, when a picture is due.
	internal static unsafe void Frame()
	{
		if ( !_isRecording ) return;

		var now = clock.Elapsed.TotalSeconds;
		if ( now < nextPicture ) return;

		var step = 1.0 / Math.Clamp( VideoFrameRate, 1, 1000 );

		//On time or late, the next one is due a step from now: a recording that fell behind does not try to catch up with a burst.
		nextPicture = Math.Max( nextPicture + step, now + ( step * 0.5 ) );

		if ( pictures.Count >= MaxWaiting )
		{
			dropped++;
			return;
		}

		var picture = new Picture { Time = clock.Elapsed };

		picture.Window = Take( recordedWindow, out picture.WindowWidth, out picture.WindowHeight, out picture.WindowBlueFirst );

		var (layer, x, y) = CoreUiWindow.MainLayer;

		if ( layer != 0 )
		{
			picture.Layer = Take( layer, out picture.LayerWidth, out picture.LayerHeight, out picture.LayerBlueFirst );
			picture.LayerX = x;
			picture.LayerY = y;
		}

		//Nothing new from either: the screen has not changed since the last picture, and the last picture stands for this one too.
		if ( picture.Window is null && picture.Layer is null ) return;

		pictures.Enqueue( picture );
		wake.Set();
	}

	//A window's newest finished copy into memory of ours, or null where there is none newer.
	private static unsafe byte[] Take( ulong swapchain, out int width, out int height, out bool blueFirst )
	{
		width = height = 0;
		blueFirst = false;

		var (wide, tall) = CoreRender.WindowCaptureSize( swapchain );
		if ( wide <= 0 || tall <= 0 ) return null;

		var bytes = wide * tall * 4;
		var pixels = ArrayPool<byte>.Shared.Rent( bytes );

		var info = stackalloc int[4];
		long taken;

		fixed ( byte* into = pixels )
			taken = CoreRender.WindowCaptureTake( swapchain, into, pixels.Length, info );

		//A surface of ten bits a channel, or one of floats, is not a picture this reads - the copy is there but its bytes are not RGBA.
		if ( taken == 0 || info[3] == 0 )
		{
			ArrayPool<byte>.Shared.Return( pixels );
			return null;
		}

		width = info[0];
		height = info[1];
		blueFirst = info[2] != 0;

		return pixels;
	}

	//The game's mix as it goes to the device, from the mixing thread. Copied and queued
	//rather than encoded here: the mixer is a sound card's deadline and waits for nobody.
	internal static void AddSound( MultiChannelBuffer mix )
	{
		if ( !_isRecording || mix is null || mix.ChannelCount < 2 ) return;

		var left = mix.Get( 0 ).Buffer;
		var right = mix.Get( 1 ).Buffer;
		var frames = Math.Min( AudioEngine.MixBufferSize, Math.Min( left.Length, right.Length ) );

		var samples = ArrayPool<float>.Shared.Rent( frames * 2 );

		for ( var i = 0; i < frames; i++ )
		{
			samples[i * 2] = left[i];
			samples[( i * 2 ) + 1] = right[i];
		}

		sounds.Enqueue( (samples, frames * 2) );
	}

	//The encoder's thread: pictures composed and written, sound written, until told to stop and empty.
	private static void Encode()
	{
		var writer = _videoWriter;

		byte[] window = null;
		int windowWidth = 0, windowHeight = 0;
		var windowBlueFirst = false;

		byte[] composed = null;

		try
		{
			while ( true )
			{
				var finishing = stopping;

				while ( sounds.TryDequeue( out var sound ) )
				{
					writer.AddSound( sound.Samples.AsSpan( 0, sound.Length ), 2, (int)AudioEngine.SamplingRate );
					ArrayPool<float>.Shared.Return( sound.Samples );
				}

				if ( pictures.TryDequeue( out var picture ) )
				{
					//The window is kept, because on a layered window it is only presented when it changes and the next picture may have none.
					if ( picture.Window is not null )
					{
						if ( window is not null ) ArrayPool<byte>.Shared.Return( window );

						window = picture.Window;
						windowWidth = picture.WindowWidth;
						windowHeight = picture.WindowHeight;
						windowBlueFirst = picture.WindowBlueFirst;
					}

					if ( window is not null )
					{
						var bytes = windowWidth * windowHeight * 4;
						if ( composed is null || composed.Length < bytes ) composed = new byte[bytes];

						window.AsSpan( 0, bytes ).CopyTo( composed );

						if ( picture.Layer is not null )
						{
							Paste( composed, windowWidth, windowHeight, windowBlueFirst, picture );
						}

#if WIN
						unsafe
						{
							fixed ( byte* target = composed )
								BlitCursor( target, windowWidth, windowHeight, windowWidth * 4, windowBlueFirst );
						}
#endif

						var span = new ReadOnlySpan<byte>( composed, 0, bytes );

						if ( windowBlueFirst ) writer.AddFrameBgra( span, windowWidth, windowHeight, picture.Time );
						else writer.AddFrame( span, picture.Time );
					}

					if ( picture.Layer is not null ) ArrayPool<byte>.Shared.Return( picture.Layer );

					continue;
				}

				if ( finishing ) break;

				wake.WaitOne( 10 );
			}

			//What the mixer sent after the last picture, then the end of the file.
			while ( sounds.TryDequeue( out var sound ) )
			{
				writer.AddSound( sound.Samples.AsSpan( 0, sound.Length ), 2, (int)AudioEngine.SamplingRate );
				ArrayPool<float>.Shared.Return( sound.Samples );
			}

			writer.Dispose();

			Log.Info( $"Video recording finished: <a href=\"{_filename}\">{_filename}</a>" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"video: the recording failed - {e.Message}" );

			try
			{
				writer.Dispose();
			}
			catch ( Exception )
			{
				//Already said.
			}
		}
		finally
		{
			if ( window is not null ) ArrayPool<byte>.Shared.Return( window );

			while ( pictures.TryDequeue( out var left ) )
			{
				if ( left.Window is not null ) ArrayPool<byte>.Shared.Return( left.Window );
				if ( left.Layer is not null ) ArrayPool<byte>.Shared.Return( left.Layer );
			}

			if ( ReferenceEquals( _videoWriter, writer ) ) _videoWriter = null;
		}
	}

	//The world's layer into the window's picture where it lies, turned round where the two disagree on which colour comes first.
	private static void Paste( byte[] into, int width, int height, bool blueFirst, Picture picture )
	{
		var layer = picture.Layer;
		var swap = picture.LayerBlueFirst != blueFirst;

		for ( var y = 0; y < picture.LayerHeight; y++ )
		{
			var row = picture.LayerY + y;
			if ( row < 0 || row >= height ) continue;

			var left = Math.Max( picture.LayerX, 0 );
			var right = Math.Min( picture.LayerX + picture.LayerWidth, width );
			if ( right <= left ) continue;

			var from = layer.AsSpan( ( ( y * picture.LayerWidth ) + ( left - picture.LayerX ) ) * 4, ( right - left ) * 4 );
			var to = into.AsSpan( ( ( row * width ) + left ) * 4, ( right - left ) * 4 );

			from.CopyTo( to );

			if ( !swap ) continue;

			for ( var i = 0; i < to.Length; i += 4 )
				( to[i], to[i + 2] ) = ( to[i + 2], to[i] );
		}
	}

#if WIN
	//Windows-specific cursor blitting implementation
	[DllImport( "user32.dll" )]
	private static extern bool GetCursorInfo( ref CURSORINFO pci );

	[DllImport( "user32.dll" )]
	private static extern bool GetIconInfo( IntPtr hIcon, ref ICONINFO piconinfo );

	[DllImport( "gdi32.dll" )]
	private static extern bool GetObjectA( IntPtr hObject, int nCount, ref BITMAP lpObject );

	[DllImport( "user32.dll" )]
	private static extern IntPtr GetDC( IntPtr hWnd );

	[DllImport( "user32.dll" )]
	private static extern int ReleaseDC( IntPtr hWnd, IntPtr hDC );

	[DllImport( "gdi32.dll" )]
	private static extern bool GetDIBits( IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, IntPtr lpvBits, ref BITMAPINFOHEADER lpbmi, uint uUsage );

	[DllImport( "gdi32.dll" )]
	private static extern bool DeleteObject( IntPtr hObject );

	[StructLayout( LayoutKind.Sequential )]
	private struct CURSORINFO
	{
		public int cbSize;
		public int flags;
		public IntPtr hCursor;
		public POINT ptScreenPos;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct ICONINFO
	{
		public bool fIcon;
		public int xHotspot;
		public int yHotspot;
		public IntPtr hbmMask;
		public IntPtr hbmColor;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct BITMAP
	{
		public int bmType;
		public int bmWidth;
		public int bmHeight;
		public int bmWidthBytes;
		public short bmPlanes;
		public short bmBitsPixel;
		public IntPtr bmBits;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct POINT
	{
		public int x;
		public int y;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct BITMAPINFOHEADER
	{
		public uint biSize;
		public int biWidth;
		public int biHeight;
		public ushort biPlanes;
		public ushort biBitCount;
		public uint biCompression;
		public uint biSizeImage;
		public int biXPelsPerMeter;
		public int biYPelsPerMeter;
		public uint biClrUsed;
		public uint biClrImportant;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct RGBQUAD
	{
		public byte rgbBlue;
		public byte rgbGreen;
		public byte rgbRed;
		public byte rgbReserved;
	}

	private const int CURSOR_SHOWING = 0x00000001;
	private const uint BI_RGB = 0;
	private const uint DIB_RGB_COLORS = 0;

	private struct CachedCursor
	{
		public IntPtr Handle;
		public byte[] BitmapData;
		public int Width;
		public int Height;
		public int HotspotX;
		public int HotspotY;
		public bool HasColor;

		public bool IsValid => Handle != IntPtr.Zero && BitmapData != null;
	}
	private static CachedCursor _cachedCursor;

	private static unsafe void BlitCursor( byte* targetData, int targetWidth, int targetHeight, int targetStride, bool blueFirst )
	{
		if ( !EnsureCachedCursor() )
			return;

		var mousePos = Mouse.Position;
		int x = (int)mousePos.x - _cachedCursor.HotspotX;
		int y = (int)mousePos.y - _cachedCursor.HotspotY;

		DrawCachedCursor( targetData, targetWidth, targetHeight, targetStride, x, y, blueFirst );
	}

	private static unsafe bool EnsureCachedCursor()
	{
		//The cursor is read out of user32 and gdi32, which exist on Windows and nowhere else. False here is already the "no cursor this frame"
		//answer BlitCursor takes, so a recording elsewhere is the same recording without a pointer drawn into it rather than a DllNotFoundException.
		if ( !OperatingSystem.IsWindows() ) return false;

		CURSORINFO cursorInfo = new() { cbSize = Marshal.SizeOf<CURSORINFO>() };
		if ( !GetCursorInfo( ref cursorInfo ) || cursorInfo.hCursor == IntPtr.Zero || cursorInfo.flags != CURSOR_SHOWING )
			return false;

		//If cursor hasn't changed, we're done
		if ( cursorInfo.hCursor == _cachedCursor.Handle && _cachedCursor.IsValid )
			return true;

		//Cache the new cursor
		_cachedCursor.Handle = cursorInfo.hCursor;

		ICONINFO iconInfo = new();
		if ( !GetIconInfo( cursorInfo.hCursor, ref iconInfo ) )
			return false;

		IntPtr hbmColorOriginal = iconInfo.hbmColor;
		IntPtr hbmMaskOriginal = iconInfo.hbmMask;

		try
		{
			BITMAP bm = new();
			if ( !GetObjectA( iconInfo.hbmMask, Marshal.SizeOf<BITMAP>(), ref bm ) )
				return false;

			BITMAPINFOHEADER bmiHeader = new()
			{
				biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
				biWidth = bm.bmWidth,
				biHeight = bm.bmHeight,
				biPlanes = 1,
				biBitCount = 32,
				biCompression = BI_RGB
			};

			IntPtr hdc = GetDC( IntPtr.Zero );
			if ( hdc == IntPtr.Zero )
				return false;

			try
			{
				int cursorDisplayHeight = bm.bmHeight;
				if ( iconInfo.hbmColor == IntPtr.Zero && bm.bmHeight == bm.bmWidth * 2 )
					cursorDisplayHeight /= 2;

				//Save cursor properties
				_cachedCursor.HotspotX = iconInfo.xHotspot;
				_cachedCursor.HotspotY = iconInfo.yHotspot;
				_cachedCursor.Width = bm.bmWidth;
				_cachedCursor.Height = cursorDisplayHeight;
				_cachedCursor.HasColor = iconInfo.hbmColor != IntPtr.Zero;

				//Create cursor cache bitmap (RGBA format)
				_cachedCursor.BitmapData = new byte[bm.bmWidth * cursorDisplayHeight * 4];

				//Create a byte buffer for GetDIBits
				int rgbQuadSize = Marshal.SizeOf<RGBQUAD>();
				int bufferSize = bm.bmWidth * bm.bmHeight * rgbQuadSize;

				//Process color data if available
				if ( iconInfo.hbmColor != IntPtr.Zero )
				{
					using PooledSpan<byte> colorBuffer = new( bufferSize );
					var colorBufferSpan = colorBuffer.Span;
					IntPtr colorsPtr = Marshal.AllocHGlobal( bufferSize );

					try
					{
						if ( !GetDIBits( hdc, iconInfo.hbmColor, 0, (uint)bm.bmHeight, colorsPtr, ref bmiHeader, DIB_RGB_COLORS ) )
							return false;

						//Copy raw bytes
						fixed ( byte* bufferPtr = colorBuffer.Span )
						{
							Buffer.MemoryCopy( colorsPtr.ToPointer(), bufferPtr, bufferSize, bufferSize );
						}

						//Process bytes into RGBA values in our cache
						for ( int y = 0; y < cursorDisplayHeight; y++ )
						{
							for ( int x = 0; x < bm.bmWidth; x++ )
							{
								int srcIndex = (y * bm.bmWidth + x) * rgbQuadSize;
								int dstIndex = (y * bm.bmWidth + x) * 4;

								_cachedCursor.BitmapData[dstIndex + 0] = colorBufferSpan[srcIndex + 2]; //R <- B
								_cachedCursor.BitmapData[dstIndex + 1] = colorBufferSpan[srcIndex + 1]; //G
								_cachedCursor.BitmapData[dstIndex + 2] = colorBufferSpan[srcIndex + 0]; //B <- R
								_cachedCursor.BitmapData[dstIndex + 3] = colorBufferSpan[srcIndex + 3]; //A
							}
						}
					}
					finally
					{
						Marshal.FreeHGlobal( colorsPtr );
					}
				}

				//Process mask data
				using PooledSpan<byte> maskBuffer = new( bufferSize );
				var maskBufferSpan = maskBuffer.Span;
				IntPtr maskPtr = Marshal.AllocHGlobal( bufferSize );

				try
				{
					if ( !GetDIBits( hdc, iconInfo.hbmMask, 0, (uint)bm.bmHeight, maskPtr, ref bmiHeader, DIB_RGB_COLORS ) )
						return false;

					//Copy raw bytes
					fixed ( byte* bufferPtr = maskBuffer.Span )
					{
						Buffer.MemoryCopy( maskPtr.ToPointer(), bufferPtr, bufferSize, bufferSize );
					}

					//If we don't have color data, process mask data for monochrome cursor
					if ( iconInfo.hbmColor == IntPtr.Zero )
					{
						for ( int y = 0; y < cursorDisplayHeight; y++ )
						{
							for ( int x = 0; x < bm.bmWidth; x++ )
							{
								int pixelIndex = y * bm.bmWidth + x;
								int srcIndex = pixelIndex * rgbQuadSize;
								int dstIndex = pixelIndex * 4;

								//Get AND mask (transparency mask)
								bool isTransparent = maskBufferSpan[srcIndex] == 0 && maskBufferSpan[srcIndex + 1] == 0 && maskBufferSpan[srcIndex + 2] == 0;

								//Get XOR mask (color mask)
								int xorIndex = (y + cursorDisplayHeight) * bm.bmWidth + x;
								int xorSrcIndex = xorIndex * rgbQuadSize;
								bool xorBitIsWhite = maskBufferSpan[xorSrcIndex] != 0 || maskBufferSpan[xorSrcIndex + 1] != 0 || maskBufferSpan[xorSrcIndex + 2] != 0;

								if ( isTransparent )
								{
									//Transparent pixel
									_cachedCursor.BitmapData[dstIndex + 0] = 0;
									_cachedCursor.BitmapData[dstIndex + 1] = 0;
									_cachedCursor.BitmapData[dstIndex + 2] = 0;
									_cachedCursor.BitmapData[dstIndex + 3] = 0; //Transparent
								}
								else
								{
									//XOR cursor pixel
									byte colorValue = xorBitIsWhite ? (byte)255 : (byte)0;
									_cachedCursor.BitmapData[dstIndex + 0] = colorValue;
									_cachedCursor.BitmapData[dstIndex + 1] = colorValue;
									_cachedCursor.BitmapData[dstIndex + 2] = colorValue;
									_cachedCursor.BitmapData[dstIndex + 3] = 255; //Opaque
								}
							}
						}
					}
				}
				finally
				{
					Marshal.FreeHGlobal( maskPtr );
				}

				return true;
			}
			finally
			{
				ReleaseDC( IntPtr.Zero, hdc );
			}
		}
		finally
		{
			if ( hbmColorOriginal != IntPtr.Zero )
				DeleteObject( hbmColorOriginal );
			if ( hbmMaskOriginal != IntPtr.Zero )
				DeleteObject( hbmMaskOriginal );
		}
	}

	private static unsafe void DrawCachedCursor( byte* targetData, int targetWidth, int targetHeight, int targetStride, int x, int y, bool blueFirst )
	{
		if ( !_cachedCursor.IsValid )
			return;

		//Draw the cursor bitmap at the given position
		for ( int cursorY = 0; cursorY < _cachedCursor.Height; cursorY++ )
		{
			for ( int cursorX = 0; cursorX < _cachedCursor.Width; cursorX++ )
			{
				int targetX = x + cursorX;
				int targetY = y + cursorY;

				if ( targetX < 0 || targetY < 0 || targetX >= targetWidth || targetY >= targetHeight )
					continue;

				//Invert Y-coordinate in the cache to fix upside-down cursor
				int cacheOffset = ((_cachedCursor.Height - 1 - cursorY) * _cachedCursor.Width + cursorX) * 4;
				int targetOffset = targetY * targetStride + targetX * 4;

				//Skip fully transparent pixels
				if ( _cachedCursor.BitmapData[cacheOffset + 3] == 0 )
					continue;

				if ( _cachedCursor.HasColor )
				{
					//Color cursor - blend using alpha. The cache is red first; a window that is blue first takes it the other way round.
					float alpha = _cachedCursor.BitmapData[cacheOffset + 3] / 255.0f;
					int red = blueFirst ? 2 : 0;
					int blue = blueFirst ? 0 : 2;

					targetData[targetOffset + red] = (byte)((_cachedCursor.BitmapData[cacheOffset + 0] * alpha) + (targetData[targetOffset + red] * (1.0f - alpha)));
					targetData[targetOffset + 1] = (byte)((_cachedCursor.BitmapData[cacheOffset + 1] * alpha) + (targetData[targetOffset + 1] * (1.0f - alpha)));
					targetData[targetOffset + blue] = (byte)((_cachedCursor.BitmapData[cacheOffset + 2] * alpha) + (targetData[targetOffset + blue] * (1.0f - alpha)));
					targetData[targetOffset + 3] = 255;
				}
				else
				{
					//Monochrome cursor - XOR
					if ( _cachedCursor.BitmapData[cacheOffset + 0] != 0 ) //White XOR bit
					{
						targetData[targetOffset + 0] ^= 255;
						targetData[targetOffset + 1] ^= 255;
						targetData[targetOffset + 2] ^= 255;
					}
					targetData[targetOffset + 3] = 255;
				}
			}
		}
	}
#endif
}
