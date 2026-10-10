using System.Runtime.InteropServices;

namespace Editor.ProjectSettingPages.Categories;

static class CursorSystemPixmap
{
	static readonly Dictionary<string, int> CursorNameToId = new()
	{
		["arrow"] = 32512,
		["ibeam"] = 32513,
		["text"] = 32513,
		["crosshair"] = 32515,
		["hand"] = 32649,
		["pointer"] = 32649,
		["wait"] = 32514,
		["hourglass"] = 32514,
		["progress"] = 32650,
		["move"] = 32646,
		["not-allowed"] = 32648,
		["ew-resize"] = 32644,
		["sizewe"] = 32644,
		["ns-resize"] = 32645,
		["sizens"] = 32645,
		["nesw-resize"] = 32643,
		["sizenesw"] = 32643,
		["nwse-resize"] = 32642,
		["sizenwse"] = 32642,
	};

	[SkipHotload]
	static readonly Dictionary<string, Pixmap> Cache = [];

	public static Pixmap Get( string name )
	{
		if ( Cache.TryGetValue( name, out var cached ) )
			return cached;

		cached = Extract( name );
		if ( cached is not null )
			Cache[name] = cached;

		return cached;
	}

	static Pixmap Extract( string name )
	{
		if ( !CursorNameToId.TryGetValue( name.ToLower(), out var cursorId ) ) return null;
		return Extract( cursorId );
	}

	static Pixmap Extract( int cursorId )
	{
		if ( !OperatingSystem.IsWindows() ) return null;

		var hCursor = Win32.LoadCursorW( IntPtr.Zero, (IntPtr)cursorId );
		if ( hCursor == IntPtr.Zero ) return null;

		int w = Win32.GetSystemMetrics( 13 ), h = Win32.GetSystemMetrics( 14 );
		if ( w <= 0 || h <= 0 ) return null;

		var onBlack = Win32.RenderIcon( hCursor, w, h, 0x00 );
		var onWhite = Win32.RenderIcon( hCursor, w, h, 0xFF );
		if ( onBlack is null || onWhite is null ) return null;

		var bgra = ComputeAlpha( onBlack, onWhite, w * h );
		CenterContent( bgra, w, h );

		var pixmap = new Pixmap( w, h );
		pixmap.UpdateFromPixels( bgra, w, h, ImageFormat.BGRA8888 );
		return pixmap;
	}

	static byte[] ComputeAlpha( byte[] onBlack, byte[] onWhite, int pixelCount )
	{
		var bgra = new byte[pixelCount * 4];

		for ( int i = 0; i < pixelCount; i++ )
		{
			int idx = i * 4;

			int diff = Math.Max( onWhite[idx + 2] - onBlack[idx + 2],
				Math.Max( onWhite[idx + 1] - onBlack[idx + 1], onWhite[idx] - onBlack[idx] ) );
			int a = Math.Clamp( 255 - diff, 0, 255 );

			if ( a > 0 )
			{
				bgra[idx] = (byte)Math.Clamp( onBlack[idx] * 255 / a, 0, 255 );
				bgra[idx + 1] = (byte)Math.Clamp( onBlack[idx + 1] * 255 / a, 0, 255 );
				bgra[idx + 2] = (byte)Math.Clamp( onBlack[idx + 2] * 255 / a, 0, 255 );
			}
			bgra[idx + 3] = (byte)a;
		}

		return bgra;
	}

	static void CenterContent( byte[] data, int w, int h )
	{
		int minX = w, minY = h, maxX = 0, maxY = 0;
		for ( int i = 3; i < data.Length; i += 4 )
		{
			if ( data[i] == 0 ) continue;
			int x = (i / 4) % w, y = (i / 4) / w;
			if ( x < minX ) minX = x; if ( y < minY ) minY = y;
			if ( x > maxX ) maxX = x; if ( y > maxY ) maxY = y;
		}

		if ( maxX < minX ) return;

		int ox = (w - (maxX - minX + 1)) / 2 - minX;
		int oy = (h - (maxY - minY + 1)) / 2 - minY;
		if ( ox == 0 && oy == 0 ) return;

		int rowBytes = (maxX - minX + 1) * 4;
		var result = new byte[data.Length];
		for ( int y = minY; y <= maxY; y++ )
			Buffer.BlockCopy( data, (y * w + minX) * 4, result, ((y + oy) * w + minX + ox) * 4, rowBytes );
		Array.Copy( result, data, data.Length );
	}

	static class Win32
	{
		[DllImport( "user32.dll" )] public static extern IntPtr LoadCursorW( IntPtr hInstance, IntPtr lpCursorName );
		[DllImport( "user32.dll" )] public static extern bool DrawIconEx( IntPtr hdc, int x, int y, IntPtr hIcon, int w, int h, int frame, IntPtr hbrFlickerFree, int flags );
		[DllImport( "user32.dll" )] public static extern int GetSystemMetrics( int index );
		[DllImport( "gdi32.dll" )] public static extern IntPtr CreateCompatibleDC( IntPtr hdc );
		[DllImport( "gdi32.dll" )] public static extern IntPtr CreateDIBSection( IntPtr hdc, ref BITMAPINFO bmi, int usage, out IntPtr bits, IntPtr hSection, int offset );
		[DllImport( "gdi32.dll" )] public static extern IntPtr SelectObject( IntPtr hdc, IntPtr obj );
		[DllImport( "gdi32.dll" )] public static extern bool DeleteObject( IntPtr obj );
		[DllImport( "gdi32.dll" )] public static extern bool DeleteDC( IntPtr hdc );

		[StructLayout( LayoutKind.Sequential )]
		public struct BITMAPINFO
		{
			public int Size, Width, Height;
			public short Planes, BitCount;
			public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
		}

		public static byte[] RenderIcon( IntPtr hIcon, int w, int h, byte bg )
		{
			var bmi = new BITMAPINFO { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
			var hdc = CreateCompatibleDC( IntPtr.Zero );
			var hBmp = CreateDIBSection( hdc, ref bmi, 0, out var bits, IntPtr.Zero, 0 );
			if ( hBmp == IntPtr.Zero ) { DeleteDC( hdc ); return null; }

			var old = SelectObject( hdc, hBmp );
			unsafe { new Span<byte>( (void*)bits, w * h * 4 ).Fill( bg ); }
			DrawIconEx( hdc, 0, 0, hIcon, w, h, 0, IntPtr.Zero, 3 );
			SelectObject( hdc, old );

			var pixels = new byte[w * h * 4];
			Marshal.Copy( bits, pixels, 0, pixels.Length );
			DeleteObject( hBmp );
			DeleteDC( hdc );
			return pixels;
		}
	}
}
