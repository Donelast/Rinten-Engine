using NativeEngine;

namespace Rinten.Utility;

/// <summary>Image as four floats per pixel, lossless for any read/write format including BC1/3/4/5</summary>
public class FloatBitmap : IDisposable
{
	private float[] pixels;
	private readonly bool sourceSrgb;

	internal unsafe FloatBitmap( int width, int height, ImageFormat format, IntPtr data, int dataSize, bool srgb = false )
	{
		if ( width <= 0 || height <= 0 ) throw new ArgumentOutOfRangeException( nameof( width ), "A picture needs a size." );

		Width = width;
		Height = height;
		sourceSrgb = srgb;
		pixels = new float[width * height * 4];

		if ( data == IntPtr.Zero || dataSize <= 0 ) return;

		var source = new ReadOnlySpan<byte>( (void*)data, dataSize );
		Read( source, format, srgb );
	}

	//A copy of an eight bit or half float Bitmap.
	internal static FloatBitmap From( Bitmap bitmap, bool srgb = false )
	{
		//Through ToFormat, which knows the order Skia keeps the bytes in.
		var format = bitmap.IsFloatingPoint ? ImageFormat.RGBA16161616F : ImageFormat.RGBA8888;
		var buffer = bitmap.ToFormat( format );

		unsafe
		{
			fixed ( byte* at = buffer )
				return new FloatBitmap( bitmap.Width, bitmap.Height, format, (IntPtr)at, buffer.Length, srgb );
		}
	}

	~FloatBitmap()
	{
		Dispose();
	}

	public void Dispose()
	{
		pixels = null;
		GC.SuppressFinalize( this );
	}

	public int Width { get; private set; }
	public int Height { get; private set; }

	/// <summary>One: this holds a flat picture, not a volume.</summary>
	public int Depth => 1;

	/// <summary>Scales the picture to a new size. Shrinking averages every pixel that falls under each new one rather than picking a few, so fine detail fades instead of shimmering; growing interpolates between neighbours.</summary>
	public void Resize( int width, int height, bool clamp = true )
	{
		if ( pixels is null || width <= 0 || height <= 0 ) return;
		if ( width == Width && height == Height ) return;

		var across = new float[width * Height * 4];
		Resample( pixels, Width, Height, across, width, horizontal: true, clamp );

		var result = new float[width * height * 4];
		Resample( across, width, Height, result, height, horizontal: false, clamp );

		pixels = result;
		Width = width;
		Height = height;
	}

	/// <summary>The picture in <paramref name="format"/>, encoded the way it was read.</summary>
	public byte[] EncodeTo( ImageFormat format ) => EncodeTo( format, sourceSrgb );

	/// <summary>The picture in <paramref name="format"/>, or null for a format this does not write - BC6H, BC7 and the phone formats.</summary>
	public byte[] EncodeTo( ImageFormat format, bool srgb )
	{
		if ( pixels is null ) return null;

		var count = Width * Height;

		switch ( format )
		{
			case ImageFormat.RGBA8888:
			case ImageFormat.LINEAR_RGBA8888:
			case ImageFormat.BGRA8888:
			case ImageFormat.LINEAR_BGRA8888:
			case ImageFormat.BGRA8888_LINEAR:
			{
				var bytes = Bytes8( srgb );
				if ( format is ImageFormat.BGRA8888 or ImageFormat.LINEAR_BGRA8888 or ImageFormat.BGRA8888_LINEAR )
				{
					for ( var i = 0; i < count; i++ )
						(bytes[i * 4], bytes[( i * 4 ) + 2]) = (bytes[( i * 4 ) + 2], bytes[i * 4]);
				}

				return bytes;
			}

			case ImageFormat.I8:
			case ImageFormat.LINEAR_I8:
				return Channels8( srgb, 0 );

			case ImageFormat.A8:
				return Channels8( false, 3 );

			case ImageFormat.IA88:
				return Channels8( srgb, 0, 3 );

			case ImageFormat.R16: return Unorm16( 1 );
			case ImageFormat.RG1616: return Unorm16( 2 );
			case ImageFormat.RGBA16161616:
			case ImageFormat.LINEAR_RGBA16161616:
				return Unorm16( 4 );

			case ImageFormat.R16F: return Halves( 1 );
			case ImageFormat.RG1616F: return Halves( 2 );
			case ImageFormat.RGBA16161616F: return Halves( 4 );

			case ImageFormat.R32F: return Floats( 1 );
			case ImageFormat.RG3232F: return Floats( 2 );
			case ImageFormat.RGB323232F: return Floats( 3 );
			case ImageFormat.RGBA32323232F: return Floats( 4 );

			case ImageFormat.DXT1:
			case ImageFormat.DXT1_ONEBITALPHA:
			{
				var output = new byte[CoreBlockEncoder.BlockByteCount( Width, Height, alpha: false )];
				CoreBlockEncoder.EncodeBc1( Bytes8( srgb ), Width, Height, output );
				return output;
			}

			case ImageFormat.DXT5:
			case ImageFormat.DXT5_NM:
			{
				var output = new byte[CoreBlockEncoder.BlockByteCount( Width, Height, alpha: true )];
				CoreBlockEncoder.EncodeBc3( Bytes8( srgb ), Width, Height, output );
				return output;
			}

			//One and two channels hold data rather than colour: never sRGB.
			case ImageFormat.ATI1N:
			{
				var output = new byte[CoreBlockEncoder.BlockByteCount( Width, Height, alpha: false )];
				CoreBlockEncoder.EncodeBc4( Bytes8( false ), Width, Height, output );
				return output;
			}

			case ImageFormat.ATI2N:
			{
				var output = new byte[CoreBlockEncoder.BlockByteCount( Width, Height, alpha: true )];
				CoreBlockEncoder.EncodeBc5( Bytes8( false ), Width, Height, output );
				return output;
			}

			default:
				return null;
		}
	}

	private void Read( ReadOnlySpan<byte> source, ImageFormat format, bool srgb )
	{
		var count = Width * Height;

		switch ( format )
		{
			case ImageFormat.RGBA8888:
			case ImageFormat.LINEAR_RGBA8888:
			case ImageFormat.BGRA8888:
			case ImageFormat.LINEAR_BGRA8888:
			case ImageFormat.BGRA8888_LINEAR:
			{
				var swap = format is ImageFormat.BGRA8888 or ImageFormat.LINEAR_BGRA8888 or ImageFormat.BGRA8888_LINEAR;
				count = Math.Min( count, source.Length / 4 );

				for ( var i = 0; i < count; i++ )
				{
					var r = source[( i * 4 ) + ( swap ? 2 : 0 )];
					var b = source[( i * 4 ) + ( swap ? 0 : 2 )];

					pixels[i * 4] = FromByte( r, srgb );
					pixels[( i * 4 ) + 1] = FromByte( source[( i * 4 ) + 1], srgb );
					pixels[( i * 4 ) + 2] = FromByte( b, srgb );
					pixels[( i * 4 ) + 3] = source[( i * 4 ) + 3] / 255.0f;
				}

				return;
			}

			case ImageFormat.I8:
			case ImageFormat.LINEAR_I8:
			{
				count = Math.Min( count, source.Length );
				for ( var i = 0; i < count; i++ )
				{
					var v = FromByte( source[i], srgb );
					pixels[i * 4] = pixels[( i * 4 ) + 1] = pixels[( i * 4 ) + 2] = v;
					pixels[( i * 4 ) + 3] = 1.0f;
				}

				return;
			}

			case ImageFormat.A8:
			{
				count = Math.Min( count, source.Length );
				for ( var i = 0; i < count; i++ )
				{
					pixels[i * 4] = pixels[( i * 4 ) + 1] = pixels[( i * 4 ) + 2] = 1.0f;
					pixels[( i * 4 ) + 3] = source[i] / 255.0f;
				}

				return;
			}

			case ImageFormat.R16F: ReadHalves( source, 1 ); return;
			case ImageFormat.RG1616F: ReadHalves( source, 2 ); return;
			case ImageFormat.RGBA16161616F: ReadHalves( source, 4 ); return;

			case ImageFormat.R32F: ReadFloats( source, 1 ); return;
			case ImageFormat.RG3232F: ReadFloats( source, 2 ); return;
			case ImageFormat.RGB323232F: ReadFloats( source, 3 ); return;
			case ImageFormat.RGBA32323232F: ReadFloats( source, 4 ); return;

			default:
				throw new NotSupportedException( $"FloatBitmap does not read {format}." );
		}
	}

	private void ReadHalves( ReadOnlySpan<byte> source, int channels )
	{
		var halves = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>( source );
		var count = Math.Min( Width * Height, halves.Length / channels );

		for ( var i = 0; i < count; i++ )
		{
			for ( var c = 0; c < 4; c++ )
				pixels[( i * 4 ) + c] = c < channels ? (float)halves[( i * channels ) + c] : ( c == 3 ? 1.0f : 0.0f );
		}
	}

	private void ReadFloats( ReadOnlySpan<byte> source, int channels )
	{
		var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( source );
		var count = Math.Min( Width * Height, floats.Length / channels );

		for ( var i = 0; i < count; i++ )
		{
			for ( var c = 0; c < 4; c++ )
				pixels[( i * 4 ) + c] = c < channels ? floats[( i * channels ) + c] : ( c == 3 ? 1.0f : 0.0f );
		}
	}

	//Four bytes a pixel, red first.
	private byte[] Bytes8( bool srgb )
	{
		var count = Width * Height;
		var bytes = new byte[count * 4];

		for ( var i = 0; i < count; i++ )
		{
			bytes[i * 4] = ToByte( pixels[i * 4], srgb );
			bytes[( i * 4 ) + 1] = ToByte( pixels[( i * 4 ) + 1], srgb );
			bytes[( i * 4 ) + 2] = ToByte( pixels[( i * 4 ) + 2], srgb );
			bytes[( i * 4 ) + 3] = ToByte( pixels[( i * 4 ) + 3], false );
		}

		return bytes;
	}

	//A byte a pixel for each named channel - alpha is never sRGB.
	private byte[] Channels8( bool srgb, params int[] channels )
	{
		var count = Width * Height;
		var bytes = new byte[count * channels.Length];

		for ( var i = 0; i < count; i++ )
		{
			for ( var c = 0; c < channels.Length; c++ )
				bytes[( i * channels.Length ) + c] = ToByte( pixels[( i * 4 ) + channels[c]], srgb && channels[c] != 3 );
		}

		return bytes;
	}

	private byte[] Unorm16( int channels )
	{
		var count = Width * Height;
		var bytes = new byte[count * channels * 2];
		var shorts = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>( bytes.AsSpan() );

		for ( var i = 0; i < count; i++ )
		{
			for ( var c = 0; c < channels; c++ )
				shorts[( i * channels ) + c] = (ushort)MathF.Round( Math.Clamp( pixels[( i * 4 ) + c], 0.0f, 1.0f ) * 65535.0f );
		}

		return bytes;
	}

	private byte[] Halves( int channels )
	{
		var count = Width * Height;
		var bytes = new byte[count * channels * 2];
		var halves = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>( bytes.AsSpan() );

		for ( var i = 0; i < count; i++ )
		{
			for ( var c = 0; c < channels; c++ )
				halves[( i * channels ) + c] = (Half)pixels[( i * 4 ) + c];
		}

		return bytes;
	}

	private byte[] Floats( int channels )
	{
		var count = Width * Height;
		var bytes = new byte[count * channels * 4];
		var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( bytes.AsSpan() );

		for ( var i = 0; i < count; i++ )
		{
			for ( var c = 0; c < channels; c++ )
				floats[( i * channels ) + c] = pixels[( i * 4 ) + c];
		}

		return bytes;
	}

	private static float FromByte( byte value, bool srgb )
	{
		var v = value / 255.0f;
		if ( !srgb ) return v;

		return v <= 0.04045f ? v / 12.92f : MathF.Pow( ( v + 0.055f ) / 1.055f, 2.4f );
	}

	private static byte ToByte( float value, bool srgb )
	{
		var v = Math.Clamp( float.IsNaN( value ) ? 0.0f : value, 0.0f, 1.0f );
		if ( srgb ) v = v <= 0.0031308f ? v * 12.92f : ( 1.055f * MathF.Pow( v, 1.0f / 2.4f ) ) - 0.055f;

		return (byte)( ( v * 255.0f ) + 0.5f );
	}

	//One axis of a resize: each new pixel weighs the old ones under a tent as wide as the step between new pixels, or one old pixel when growing.
	private static void Resample( float[] from, int width, int height, float[] into, int size, bool horizontal, bool clamp )
	{
		var old = horizontal ? width : height;
		var lines = horizontal ? height : width;
		var outWidth = horizontal ? size : width;

		var scale = (float)old / size;
		var radius = Math.Max( 1.0f, scale );

		for ( var n = 0; n < size; n++ )
		{
			var centre = ( ( n + 0.5f ) * scale ) - 0.5f;
			var first = (int)MathF.Floor( centre - radius ) + 1;
			var last = (int)MathF.Ceiling( centre + radius ) - 1;

			for ( var line = 0; line < lines; line++ )
			{
				float r = 0, g = 0, b = 0, a = 0, total = 0;

				for ( var o = first; o <= last; o++ )
				{
					var weight = 1.0f - ( MathF.Abs( o - centre ) / radius );
					if ( weight <= 0.0f ) continue;

					var at = clamp ? Math.Clamp( o, 0, old - 1 ) : ( ( o % old ) + old ) % old;
					var index = horizontal ? ( ( line * width ) + at ) * 4 : ( ( at * width ) + line ) * 4;

					r += from[index] * weight;
					g += from[index + 1] * weight;
					b += from[index + 2] * weight;
					a += from[index + 3] * weight;
					total += weight;
				}

				var target = horizontal ? ( ( line * outWidth ) + n ) * 4 : ( ( n * outWidth ) + line ) * 4;
				var inverse = total > 0.0f ? 1.0f / total : 0.0f;

				into[target] = r * inverse;
				into[target + 1] = g * inverse;
				into[target + 2] = b * inverse;
				into[target + 3] = a * inverse;
			}
		}
	}
}
