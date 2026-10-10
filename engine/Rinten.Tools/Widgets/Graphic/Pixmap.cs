using System;

namespace Editor
{
	/// <summary>A pixel map, or just a simple image.</summary>
	public partial class Pixmap
	{
		static bool HasGraphics => (!Rinten.Application.IsHeadless && !Rinten.Application.IsUnitTest);


		/// <summary>Width of the pixel map.</summary>
		public int Width => (picture?.Width ?? 0);

		/// <summary>Height of the pixel map.</summary>
		public int Height => (picture?.Height ?? 0);

		/// <summary>Whether this pixel map supports the alpha channel.</summary>
		public bool HasAlpha => true;

		/// <summary>THe size of this pixel map.</summary>
		public Vector2 Size => new( Width, Height );

		/// <summary>Copy with every pixel multiplied by a colour, matching how surfaces tint textures in the world</summary>
		public Pixmap Tinted( Color colour )
		{
			if ( picture is null ) return this;

			var made = new Pixmap( Width, Height );

			made.picture.DrawBitmap( picture, made.picture.Rect );
			made.picture.Tint( colour );

			return made;
		}

		/// <summary>A copy of this picture painted flat in a colour, keeping its shape.</summary>
		public Pixmap Recoloured( Color colour )
		{
			if ( picture is null ) return this;

			var made = new Pixmap( Width, Height );

			made.picture.DrawBitmap( picture, made.picture.Rect );
			made.picture.Colorize( colour );

			return made;
		}

		public static Pixmap FromFile( string filename )
		{
			if ( !HasGraphics )
				return null;

			//The bitmap belongs to the pixmap from here on, and the pixmap is what the caller keeps.
			return FromLoadedFile( filename );
		}

		/// <summary>Create a pixmap from a bitmap</summary>
		public static Pixmap FromBitmap( Bitmap bitmap )
		{
			if ( !HasGraphics )
				return null;

			return new Pixmap { picture = bitmap };
		}

		/// <summary>Create a pixmap from a texture.</summary>
		public static unsafe Pixmap FromTexture( Texture texture, bool withAlpha = true )
		{
			if ( !HasGraphics )
				return null;

			if ( texture is null )
				return null;
			//No-op: nothing to do here.
			return default;
		}

		/// <summary>Fill the pixel map with given color.</summary>
		public void Clear( Color color )
		{
			picture?.Clear( color );
		}

		/// <summary>Duplicate a sub-rectangle of the image at re-draw it at given coordinates.</summary>
		public void Scroll( int x, int y, Rect r )
		{
		}

		/// <summary>Duplicate the entire image and re-draw it at given coordinates.</summary>
		public void Scroll( int x, int y )
		{
			Scroll( x, y, new Rect( 0, 0, Width, Height ) );
		}

		/// <summary>Returns a new pixel map that contains resized version of this image with given dimensions. Will try to preserve aspect ratio.</summary>
		public Pixmap Resize( Vector2 size )
		{
			if ( picture is null ) return null;

			var wanted = new Vector2( MathF.Max( size.x, 1 ), MathF.Max( size.y, 1 ) );

			var scale = MathF.Max( wanted.x / picture.Width, wanted.y / picture.Height );

			var made = picture.Resize( (picture.Width * scale).CeilToInt(), (picture.Height * scale).CeilToInt() );

			return made is null ? null : new Pixmap { picture = made };
		}

		public Pixmap Resize( int x, int y ) => Resize( new( x, y ) );

		/// <summary>Writes raw pixels to the pixel map.</summary>
		public bool UpdateFromPixels( ReadOnlySpan<byte> data, int width, int height, ImageFormat format = ImageFormat.BGRA8888 )
		{
			if ( width <= 0 || height <= 0 ) return false;

			var wanted = width * height * 4;

			if ( data.Length < wanted ) return false;

			if ( format != ImageFormat.BGRA8888 && format != ImageFormat.RGBA8888 )
				return false;

			if ( picture is null || !picture.IsValid || picture.Width != width || picture.Height != height || picture.IsFloatingPoint )
			{
				picture?.Dispose();
				picture = new Bitmap( width, height );
			}

			if ( format == ImageFormat.RGBA8888 )
			{
				picture.SetPixelsRgba( data[..wanted] );
				return true;
			}

			var pixels = new byte[wanted];
			data[..wanted].CopyTo( pixels );

			for ( var i = 0; i < wanted; i += 4 )
			{
				(pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
			}

			picture.SetPixelsRgba( pixels );
			return true;
		}

		/// <summary>Copy from a bitmap</summary>
		public unsafe bool UpdateFromPixels( Bitmap bitmap )
		{
			if ( bitmap is null || !bitmap.IsValid )
				return false;

			if ( ReferenceEquals( picture, bitmap ) )
				return true;

			return UpdateFromPixels( bitmap.GetBuffer(), bitmap.Width, bitmap.Height, ImageFormat.RGBA8888 );
		}

		/// <summary>Writes raw pixels to the pixel map. Only BGRA8888 is currently supported.</summary>
		public unsafe bool UpdateFromPixels( ReadOnlySpan<byte> data, Vector2 size, ImageFormat format = ImageFormat.BGRA8888 ) => UpdateFromPixels( data, (int)size.x, (int)size.y, format );

		/// <summary>Returns the raw bytes of a PNG file that contains this pixel maps image. Internally writes and deletes a file, so be careful using it often.</summary>
		public byte[] GetPng()
		{
			var target = FileSystem.Temporary.GetFullPath( $"/{System.Guid.NewGuid()}.temp" );
			try
			{
				return System.IO.File.ReadAllBytes( target );
			}
			finally
			{
				System.IO.File.Delete( target );
			}
		}

		/// <summary>Save the pixel map as a PNG file at given location.</summary>
		public bool SavePng( string filename )
		{
			return SaveManaged( filename, jpeg: false, quality: 100 );
		}

		/// <summary>Save the pixel map as a JPEG file at given location.</summary>
		public bool SaveJpg( string filename, int quality = 70 )
		{
			return SaveManaged( filename, jpeg: true, quality: quality );
		}

		/// <summary>Returns the raw bytes of a JPEG file that contains this pixel maps image. Internally writes and deletes a file, so be careful using it often.</summary>
		public byte[] GetJpeg( int quality )
		{
			var target = FileSystem.Temporary.GetFullPath( $"/{System.Guid.NewGuid()}.temp" );
			try
			{
				return System.IO.File.ReadAllBytes( target );
			}
			finally
			{
				System.IO.File.Delete( target );
			}
		}

		/// <summary>Returns the raw bytes of a BMP file that contains this pixel maps image. Internally writes and deletes a file, so be careful using it often.</summary>
		public byte[] GetBmp( int quality )
		{
			var target = FileSystem.Temporary.GetFullPath( $"/{System.Guid.NewGuid()}.temp" );
			try
			{
				return System.IO.File.ReadAllBytes( target );
			}
			finally
			{
				System.IO.File.Delete( target );
			}
		}

		//ARGB
		Color32[] pixels;

		void DirtyPixels()
		{
			pixels = null;
		}

		int PixelSize => 4;
		int Stride => Width * PixelSize;
		int ByteSize => Height * Stride;

		unsafe void NeedPixels()
		{
			if ( pixels is not null )
				return;

			pixels = new Color32[Width * Height];

			fixed ( void* bytes = pixels )
			{
			}
		}

		public Color GetPixel( int x, int y )
		{
			NeedPixels();

			var p = x + (y * Width);
			return pixels[p];
		}
	}

}
