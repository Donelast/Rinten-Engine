using System.Runtime.InteropServices;

namespace Rinten;

//Click-through layered window showing the UI with per-pixel alpha, since a Vulkan swapchain is opaque
//The UI is read back each frame and passed to UpdateLayeredWindow; costly, so windows opt in
internal sealed unsafe class CoreSheetOverlay : IDisposable
{
	private IntPtr window;
	private IntPtr memoryDc;
	private IntPtr bitmap;
	private IntPtr previousBitmap;
	private uint* bits;
	private int width;
	private int height;

	//The window this lies on, whose client area it follows.
	private readonly IntPtr owner;

	public CoreSheetOverlay( IntPtr ownerWindow )
	{
		owner = ownerWindow;

		Follow( this );

		//Top-level window owned by the covered one: UpdateLayeredWindow refuses child windows
		window = CreateWindowExW(
			WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
			"STATIC", null, WS_POPUP,
			0, 0, 1, 1,
			owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero );
	}

	public bool IsValid => window != IntPtr.Zero;

	/// <summary>Puts one frame on the screen, over the owner's client area.</summary>
	public Span<byte> Frame( int pixelsWide, int pixelsTall )
	{
		if ( window == IntPtr.Zero ) return default;

		if ( !EnsureSurface( pixelsWide, pixelsTall ) )
		{
			Said( $"refused {pixelsWide}x{pixelsTall}: no surface" );
			return default;
		}

		return new Span<byte>( bits, pixelsWide * pixelsTall * 4 );
	}

	/// <summary>Puts what was written into that span on the screen.</summary>
	public void Present()
	{
		if ( window == IntPtr.Zero || width <= 0 || height <= 0 ) return;

		var pixelsWide = width;
		var pixelsTall = height;

		//Where the owner's client area is now, in screen coordinates. Asked every frame rather than remembered:
		//the window moves, and the frame that notices is the frame that has to put the picture in the new place.
		var origin = new Point();
		ClientToScreen( owner, ref origin );

		var destination = new Point { X = origin.X, Y = origin.Y };
		var size = new Size { Width = pixelsWide, Height = pixelsTall };
		var from = new Point();

		var blend = new BlendFunction
		{
			BlendOp = AC_SRC_OVER,
			SourceConstantAlpha = 255,
			AlphaFormat = AC_SRC_ALPHA,
		};

		var screen = GetDC( IntPtr.Zero );

		var took = UpdateLayeredWindow( window, screen, ref destination, ref size,
			memoryDc, ref from, 0, ref blend, ULW_ALPHA );

		ReleaseDC( IntPtr.Zero, screen );

		Said( $"{pixelsWide}x{pixelsTall} at {destination.X},{destination.Y}" +
			$" {( took ? "shown" : "refused by the window system" )}" );
	}

	//What was last said, so the same thing is not said every frame.
	private string lastSaid;

	//Logs what the overlay did when it differs from last time, to tell overlay bugs from paint bugs
	private void Said( string what )
	{
		if ( what == lastSaid ) return;

		lastSaid = what;

		new Rinten.Diagnostics.Logger( "Ui" ).Info( $"overlay {window:X}: {what}" );
	}

	/// <summary>On the screen, above its owner, without taking anything from it.</summary>
	public void Show()
	{
		if ( window == IntPtr.Zero ) return;

		ShowWindow( window, SW_SHOWNOACTIVATE );
	}

	public void Hide()
	{
		if ( window == IntPtr.Zero ) return;

		ShowWindow( window, SW_HIDE );
	}

	//The bitmap the frames go through, remade when the window changes size.
	private bool EnsureSurface( int pixelsWide, int pixelsTall )
	{
		if ( bitmap != IntPtr.Zero && width == pixelsWide && height == pixelsTall ) return true;

		DestroySurface();

		var info = new BitmapInfoHeader
		{
			Size = (uint)sizeof( BitmapInfoHeader ),
			Width = pixelsWide,

			//Negative height is how a DIB says "top row first", which is the order the renderer reads out in.
			Height = -pixelsTall,

			Planes = 1,
			BitCount = 32,
		};

		void* surface = null;

		memoryDc = CreateCompatibleDC( IntPtr.Zero );
		bitmap = CreateDIBSection( memoryDc, &info, 0, &surface, IntPtr.Zero, 0 );

		if ( bitmap == IntPtr.Zero || surface == null )
		{
			DestroySurface();
			return false;
		}

		previousBitmap = SelectObject( memoryDc, bitmap );
		bits = (uint*)surface;
		width = pixelsWide;
		height = pixelsTall;

		return true;
	}

	private void DestroySurface()
	{
		if ( memoryDc != IntPtr.Zero && previousBitmap != IntPtr.Zero )
			SelectObject( memoryDc, previousBitmap );

		if ( bitmap != IntPtr.Zero ) DeleteObject( bitmap );
		if ( memoryDc != IntPtr.Zero ) DeleteDC( memoryDc );

		bitmap = IntPtr.Zero;
		previousBitmap = IntPtr.Zero;
		memoryDc = IntPtr.Zero;
		bits = null;
		width = 0;
		height = 0;
	}

	//-- following the owner while it is dragged ------------------------------

	//Every overlay that is currently on a window. Static, because the hook below is one for the
	//process rather than one per window: it is handed a message and has to find whose it is.
	private static readonly List<CoreSheetOverlay> living = new();

	//Kept alive by hand - the window system holds only the pointer.
	private static MessageHook hook;

	//Keeps the overlay on its owner during a drag, when Windows' modal move loop starves our frame loop
	private static void Follow( CoreSheetOverlay overlay )
	{
		lock ( living )
		{
			living.Add( overlay );

			if ( hook is not null ) return;

			hook = OnMessage;
			SDL_SetWindowsMessageHook( hook, IntPtr.Zero );
		}
	}

	private static void Unfollow( CoreSheetOverlay overlay )
	{
		lock ( living ) living.Remove( overlay );
	}

	//Whether the window system should carry on with this message. Always: this only listens.
	private static bool OnMessage( IntPtr userData, Message* message )
	{
		if ( message is null ) return true;

		if ( message->Value is not ( WM_WINDOWPOSCHANGED or WM_MOVE or WM_SIZE ) ) return true;

		var moved = message->Window;

		lock ( living )
		{
			foreach ( var overlay in living )
			{
				if ( overlay.owner != moved ) continue;
				if ( overlay.window == IntPtr.Zero ) continue;

				overlay.MoveToOwner();
				break;
			}
		}

		return true;
	}

	//Puts the overlay back over its owner's client area.
	private void MoveToOwner()
	{
		if ( window == IntPtr.Zero || width <= 0 || height <= 0 ) return;

		var origin = new Point();
		ClientToScreen( owner, ref origin );

		SetWindowPos( window, IntPtr.Zero, origin.X, origin.Y, width, height,
			SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOSIZE );
	}

	public void Dispose()
	{
		Unfollow( this );

		DestroySurface();

		if ( window != IntPtr.Zero )
		{
			DestroyWindow( window );
			window = IntPtr.Zero;
		}
	}

	//-- the window system ----------------------------------------------------

	private const int WS_POPUP = unchecked( (int)0x80000000 );
	private const int WS_EX_LAYERED = 0x00080000;
	private const int WS_EX_TRANSPARENT = 0x00000020;
	private const int WS_EX_NOACTIVATE = 0x08000000;
	private const int WS_EX_TOOLWINDOW = 0x00000080;
	private const int SW_HIDE = 0;
	private const int SW_SHOWNOACTIVATE = 4;
	private const uint SWP_NOSIZE = 0x0001;
	private const uint SWP_NOZORDER = 0x0004;
	private const uint SWP_NOACTIVATE = 0x0010;
	private const uint SWP_NOOWNERZORDER = 0x0200;
	private const uint WM_MOVE = 0x0003;
	private const uint WM_SIZE = 0x0005;
	private const uint WM_WINDOWPOSCHANGED = 0x0047;

	//A window message, as the window system lays one out.
	[StructLayout( LayoutKind.Sequential )]
	private struct Message
	{
		public IntPtr Window;
		public uint Value;
		public IntPtr WParam;
		public IntPtr LParam;
		public uint Time;
		public Point Where;
	}

	//The return is one byte, not four. SDL's is a C bool and .NET's default for one of those is a
	//Win32 BOOL - four bytes - which is three bytes of whatever happened to be next on the way back.
	[UnmanagedFunctionPointer( CallingConvention.Cdecl )]
	[return: MarshalAs( UnmanagedType.U1 )]
	private delegate bool MessageHook( IntPtr userData, Message* message );

	[DllImport( "SDL3", EntryPoint = "SDL_SetWindowsMessageHook" )]
	private static extern void SDL_SetWindowsMessageHook( MessageHook callback, IntPtr userData );

	[DllImport( "user32.dll" )]
	private static extern bool SetWindowPos( IntPtr window, IntPtr after,
		int x, int y, int width, int height, uint flags );
	private const uint ULW_ALPHA = 0x00000002;
	private const byte AC_SRC_OVER = 0x00;
	private const byte AC_SRC_ALPHA = 0x01;

	[StructLayout( LayoutKind.Sequential )]
	private struct Point { public int X, Y; }

	[StructLayout( LayoutKind.Sequential )]
	private struct Size { public int Width, Height; }

	[StructLayout( LayoutKind.Sequential )]
	private struct BlendFunction
	{
		public byte BlendOp;
		public byte BlendFlags;
		public byte SourceConstantAlpha;
		public byte AlphaFormat;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct BitmapInfoHeader
	{
		public uint Size;
		public int Width;
		public int Height;
		public ushort Planes;
		public ushort BitCount;
		public uint Compression;
		public uint SizeImage;
		public int XPelsPerMeter;
		public int YPelsPerMeter;
		public uint ClrUsed;
		public uint ClrImportant;
	}

	[DllImport( "user32.dll", CharSet = CharSet.Unicode )]
	private static extern IntPtr CreateWindowExW( int exStyle, string className, string windowName,
		int style, int x, int y, int width, int height,
		IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param );

	[DllImport( "user32.dll" )]
	private static extern bool DestroyWindow( IntPtr window );

	[DllImport( "user32.dll" )]
	private static extern bool ShowWindow( IntPtr window, int command );

	[DllImport( "user32.dll" )]
	private static extern bool ClientToScreen( IntPtr window, ref Point point );

	[DllImport( "user32.dll" )]
	private static extern IntPtr GetDC( IntPtr window );

	[DllImport( "user32.dll" )]
	private static extern int ReleaseDC( IntPtr window, IntPtr dc );

	[DllImport( "user32.dll" )]
	private static extern bool UpdateLayeredWindow( IntPtr window, IntPtr screenDc,
		ref Point destination, ref Size size, IntPtr sourceDc, ref Point source,
		uint colourKey, ref BlendFunction blend, uint flags );

	[DllImport( "gdi32.dll" )]
	private static extern IntPtr CreateCompatibleDC( IntPtr dc );

	[DllImport( "gdi32.dll" )]
	private static extern bool DeleteDC( IntPtr dc );

	[DllImport( "gdi32.dll" )]
	private static extern IntPtr CreateDIBSection( IntPtr dc, BitmapInfoHeader* info,
		uint usage, void** bits, IntPtr section, uint offset );

	[DllImport( "gdi32.dll" )]
	private static extern bool DeleteObject( IntPtr handle );

	[DllImport( "gdi32.dll" )]
	private static extern IntPtr SelectObject( IntPtr dc, IntPtr handle );
}
