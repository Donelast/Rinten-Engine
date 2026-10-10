using SkiaSharp;

namespace Rinten;

public sealed partial class Bitmap : IDisposable, IValid
{
	private SKBitmap _bitmap;
	private SKCanvas _canvas;

	//Zero once released rather than a null reference. Dispose drops the SKBitmap and IsValid is there to say so, but everything reading a size went
	//straight through to it - so a picture whose bitmap had gone threw once a frame from inside a paint, which is a widget that never draws again.
	public int Width => _bitmap?.Width ?? 0;
	public int Height => _bitmap?.Height ?? 0;
	public int BytesPerPixel => _bitmap?.BytesPerPixel ?? 0;
	public int ByteCount => _bitmap?.ByteCount ?? 0;
	public Rect Rect => new( 0, 0, Width, Height );


	/// <summary>The width and height of the bitmap</summary>
	public Vector2Int Size => new( Width, Height );


	public Vector2 Center => new Vector2( Width, Height ) * 0.5f;

	public bool IsFloatingPoint { get; init; }

	public bool IsValid => _bitmap is not null && _canvas is not null;

	private const int MaxDimension = 16384;

	public Bitmap( int width, int height, bool floatingPoint = false )
	{
		if ( width <= 0 || height <= 0 )
			throw new ArgumentOutOfRangeException( "Dimensions must be positive." );

		if ( width > MaxDimension || height > MaxDimension )
			throw new ArgumentOutOfRangeException( $"Dimensions cannot exceed {MaxDimension}." );

		IsFloatingPoint = floatingPoint;
		var colorType = IsFloatingPoint ? SKColorType.RgbaF16 : SKColorType.Rgba8888;
		var info = new SKImageInfo( width, height, colorType, SKAlphaType.Unpremul );

		_bitmap = new SKBitmap( info );
		_canvas = new SKCanvas( _bitmap );
	}

	//Used internally for resizing operations
	internal Bitmap( SKBitmap bitmap )
	{
		IsFloatingPoint = bitmap.ColorType == SKColorType.RgbaF16;
		_bitmap = bitmap;
		_canvas = new SKCanvas( _bitmap );
	}

	public void Dispose()
	{
		_canvas?.Dispose();
		_canvas = default;

		_bitmap?.Dispose();
		_bitmap = default;
	}

	/// <summary>Clears the bitmap to the specified color.</summary>
	public void Clear( Color color )
	{
		_canvas.Clear( color.ToSkF() );
	}

	/// <summary>Retrieves the pixel data of the bitmap as an array of colors.</summary>
	public Color[] GetPixels()
	{
		if ( IsFloatingPoint )
		{
			unsafe
			{
				int pixelCount = _bitmap.Width * _bitmap.Height;
				var raw = new Span<Color.Rgba16>( (void*)_bitmap.GetPixels(), pixelCount );

				//Allocate the final Color array
				var colors = new Color[pixelCount];

				//Convert each HalfColor to Color
				for ( int i = 0; i < pixelCount; i++ )
				{
					colors[i] = raw[i].ToColor();
				}

				return colors;
			}
		}
		else
		{
			return _bitmap.Pixels.Select( p => p.FromSk() ).ToArray();
		}
	}

	/// <summary>Retrieves the pixel data of the bitmap as an array of colors.</summary>
	public Color.Rgba16[] GetPixels16()
	{
		if ( IsFloatingPoint )
		{
			unsafe
			{
				int pixelCount = _bitmap.Width * _bitmap.Height;
				var raw = new Span<Color.Rgba16>( (void*)_bitmap.GetPixels(), pixelCount );
				return raw.ToArray();
			}
		}
		else
		{
			return _bitmap.Pixels.Select( p => (Color.Rgba16)p.FromSk() ).ToArray();
		}
	}

	/// <summary>Retrieves the pixel data of the bitmap as an array of colors.</summary>
	public Color32[] GetPixels32()
	{
		if ( IsFloatingPoint )
		{
			unsafe
			{
				int pixelCount = _bitmap.Width * _bitmap.Height;
				var raw = new Span<Color.Rgba16>( (void*)_bitmap.GetPixels(), pixelCount );

				//Allocate the final Color array
				var colors = new Color32[pixelCount];

				//Convert each HalfColor to Color
				for ( int i = 0; i < pixelCount; i++ )
				{
					colors[i] = raw[i].ToColor();
				}

				return colors;
			}
		}
		else
		{
			return _bitmap.Pixels.Select( p => (Color32)p.FromSk() ).ToArray();
		}
	}

	//Copies RGBA8 pixels straight in, top row first; non-float bitmaps already use this layout
	internal void SetPixelsRgba( ReadOnlySpan<byte> pixels )
	{
		if ( IsFloatingPoint ) throw new InvalidOperationException( "This bitmap holds more than eight bits a channel." );

		var bytes = _bitmap.Width * _bitmap.Height * 4;

		if ( pixels.Length < bytes )
			throw new ArgumentException( "There are fewer pixels here than the bitmap has." );

		unsafe
		{
			var destination = new Span<byte>( (void*)_bitmap.GetPixels(), bytes );
			pixels[..bytes].CopyTo( destination );
		}

		_bitmap.NotifyPixelsChanged();
	}

	public void SetPixels( Color[] colors )
	{
		if ( colors is null || colors.Length != _bitmap.Width * _bitmap.Height )
		{
			throw new ArgumentException( "Colors array must match the size of the bitmap." );
		}

		if ( IsFloatingPoint )
		{
			unsafe
			{
				int pixelCount = _bitmap.Width * _bitmap.Height;
				var rawPixels = new Span<Color.Rgba16>( (void*)_bitmap.GetPixels(), pixelCount );
				for ( int i = 0; i < pixelCount; i++ )
				{
					rawPixels[i] = new Color.Rgba16( colors[i] );
				}
			}
		}
		else
		{
			var skColors = new SKColor[colors.Length];
			for ( int i = 0; i < colors.Length; i++ )
			{
				skColors[i] = colors[i].ToSk();
			}
			_bitmap.Pixels = skColors;
		}
	}

	/// <summary>Retrieves the color of a specific pixel in the bitmap.</summary>
	public Color GetPixel( int x, int y )
	{
		AssertBounds( x, y, 1, 1 );

		return _bitmap.GetPixel( x, y ).FromSk();
	}

	/// <summary>Sets the color of a specific pixel in the bitmap.</summary>
	public void SetPixel( int x, int y, Color color )
	{
		AssertBounds( x, y, 1, 1 );

		if ( _bitmap.ColorType == SKColorType.RgbaF16 )
		{
			unsafe
			{
				int index = y * _bitmap.Width + x;
				var rawPixels = new Span<Color.Rgba16>( (void*)_bitmap.GetPixels(), _bitmap.Width * _bitmap.Height );
				rawPixels[index] = new Color.Rgba16( color );
			}
		}
		else
		{
			_bitmap.SetPixel( x, y, color.ToSk() );
		}
	}

	//Low level, get a span of the bitmap data.
	internal unsafe Span<byte> GetBuffer()
	{
		return new Span<byte>( (void*)_bitmap.GetPixels(), ByteCount );
	}

	//Super low level, get a pointer to the bitmap data.
	internal unsafe void* GetPointer()
	{
		return (void*)_bitmap.GetPixels();
	}

	//Asserts that the specified region is within the bounds of the bitmap. Throws an exception if the bounds are out of range.
	private void AssertBounds( int x, int y, int width, int height )
	{
		if ( x < 0 || y < 0 || x + width > _bitmap.Width || y + height > _bitmap.Height )
		{
			throw new ArgumentOutOfRangeException( nameof( x ), "Specified region is out of bounds." );
		}
	}

	/// <summary>Copy the bitmap to a new one without any changes.</summary>
	public Bitmap Clone()
	{
		var newBitmap = _bitmap.Copy();
		return new Bitmap( newBitmap );
	}


	/// <summary>Returns true if this bitmap is completely opaque (no alpha) This does a pixel by pixel search, so it's not the fastest.</summary>
	public unsafe bool IsOpaque()
	{
		if ( _bitmap.AlphaType == SKAlphaType.Opaque )
			return true;

		int height = _bitmap.Height;
		int width = _bitmap.Width;
		int rowBytes = _bitmap.RowBytes;
		IntPtr pixels = _bitmap.GetPixels();

		if ( _bitmap.ColorType == SKColorType.RgbaF16 )
		{
			float* ptr = (float*)pixels;
			bool opaque = true;

			Parallel.For( 0, height, ( y, state ) =>
			{
				float* row = ptr + (y * (rowBytes / sizeof( float )));

				for ( int x = 0; x < width; x++ )
				{
					float alpha = row[x * 4 + 3]; //Alpha is the 4th channel
					if ( alpha < 1.0f ) //Check if alpha is not fully opaque
					{
						opaque = false;
						state.Stop(); //Stop all threads
						return;
					}
				}
			} );

			return opaque;

		}

		if ( _bitmap.ColorType == SKColorType.Rgba8888 )
		{
			bool opaque = true;

			Parallel.For( 0, height, ( y, state ) =>
			{
				byte* ptr = (byte*)pixels + (y * rowBytes);

				for ( int x = 0; x < width; x++ )
				{
					if ( ptr[(x * 4) + 3] != 255 )
					{
						opaque = false;
						state.Stop(); //Stop all threads
						return;
					}
				}
			} );

			return opaque;
		}

		//we can't determin that it's opaque so say it's not
		return false;
	}
}
