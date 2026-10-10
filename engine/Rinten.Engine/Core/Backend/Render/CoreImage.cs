using System.Buffers.Binary;
using NativeEngine;

namespace Rinten;

//Managed pixel format conversion via a four-float colour; block-compressed formats not handled
internal static class CoreImage
{
	//Bytes one pixel takes, or zero where the format is not one we unpack.
	internal static int PixelBytes( ImageFormat format ) => format switch
	{
		ImageFormat.RGBA32323232F => 16,
		ImageFormat.RGB323232F => 12,

		ImageFormat.RGBA16161616F or ImageFormat.RGBA16161616 or
		ImageFormat.LINEAR_RGBA16161616 or ImageFormat.RG3232F => 8,

		ImageFormat.RGBA8888 or ImageFormat.ABGR8888 or ImageFormat.ARGB8888 or
		ImageFormat.BGRA8888 or ImageFormat.BGRX8888 or ImageFormat.RGBX8888 or
		ImageFormat.LINEAR_BGRX8888 or ImageFormat.LINEAR_RGBA8888 or
		ImageFormat.LINEAR_ABGR8888 or ImageFormat.LINEAR_ARGB8888 or
		ImageFormat.LINEAR_BGRA8888 or ImageFormat.RGBA8888_LINEAR or
		ImageFormat.BGRA8888_LINEAR or ImageFormat.BGRX8888_LINEAR or
		ImageFormat.RG1616F or ImageFormat.R32F => 4,

		ImageFormat.RGB888 or ImageFormat.BGR888 or
		ImageFormat.LINEAR_RGB888 or ImageFormat.LINEAR_BGR888 => 3,

		ImageFormat.IA88 or ImageFormat.R16 or ImageFormat.R16F or ImageFormat.I16F => 2,

		ImageFormat.I8 or ImageFormat.A8 or ImageFormat.LINEAR_I8 => 1,

		_ => 0,
	};

	//Whether a pixel of this format can be read or written here.
	internal static bool Handles( ImageFormat format ) => PixelBytes( format ) > 0;

	//One pixel, as red, green, blue and alpha. The eight-bit formats come back between nought and one; the
	//float formats come back as they are, which is the only way to keep a value above one that was put there.
	internal static Vector4 Read( ReadOnlySpan<byte> pixel, ImageFormat format )
	{
		const float byteScale = 1.0f / 255.0f;
		const float shortScale = 1.0f / 65535.0f;

		switch ( format )
		{
			case ImageFormat.RGBA8888:
			case ImageFormat.LINEAR_RGBA8888:
			case ImageFormat.RGBA8888_LINEAR:
				return new Vector4( pixel[0], pixel[1], pixel[2], pixel[3] ) * byteScale;

			case ImageFormat.RGBX8888:
				return new Vector4( pixel[0] * byteScale, pixel[1] * byteScale, pixel[2] * byteScale, 1 );

			case ImageFormat.BGRA8888:
			case ImageFormat.LINEAR_BGRA8888:
			case ImageFormat.BGRA8888_LINEAR:
				return new Vector4( pixel[2], pixel[1], pixel[0], pixel[3] ) * byteScale;

			case ImageFormat.BGRX8888:
			case ImageFormat.LINEAR_BGRX8888:
			case ImageFormat.BGRX8888_LINEAR:
				return new Vector4( pixel[2] * byteScale, pixel[1] * byteScale, pixel[0] * byteScale, 1 );

			case ImageFormat.ARGB8888:
			case ImageFormat.LINEAR_ARGB8888:
				return new Vector4( pixel[1], pixel[2], pixel[3], pixel[0] ) * byteScale;

			case ImageFormat.ABGR8888:
			case ImageFormat.LINEAR_ABGR8888:
				return new Vector4( pixel[3], pixel[2], pixel[1], pixel[0] ) * byteScale;

			case ImageFormat.RGB888:
			case ImageFormat.LINEAR_RGB888:
				return new Vector4( pixel[0] * byteScale, pixel[1] * byteScale, pixel[2] * byteScale, 1 );

			case ImageFormat.BGR888:
			case ImageFormat.LINEAR_BGR888:
				return new Vector4( pixel[2] * byteScale, pixel[1] * byteScale, pixel[0] * byteScale, 1 );

			case ImageFormat.I8:
			case ImageFormat.LINEAR_I8:
			{
				var value = pixel[0] * byteScale;
				return new Vector4( value, value, value, 1 );
			}

			case ImageFormat.A8:
				return new Vector4( 0, 0, 0, pixel[0] * byteScale );

			case ImageFormat.IA88:
			{
				var value = pixel[0] * byteScale;
				return new Vector4( value, value, value, pixel[1] * byteScale );
			}

			case ImageFormat.R16:
				return new Vector4( BinaryPrimitives.ReadUInt16LittleEndian( pixel ) * shortScale, 0, 0, 1 );

			case ImageFormat.R16F:
			case ImageFormat.I16F:
				return new Vector4( (float)BitConverter.UInt16BitsToHalf( BinaryPrimitives.ReadUInt16LittleEndian( pixel ) ), 0, 0, 1 );

			case ImageFormat.RG1616F:
				return new Vector4(
					(float)BitConverter.UInt16BitsToHalf( BinaryPrimitives.ReadUInt16LittleEndian( pixel ) ),
					(float)BitConverter.UInt16BitsToHalf( BinaryPrimitives.ReadUInt16LittleEndian( pixel[2..] ) ),
					0, 1 );

			case ImageFormat.RGBA16161616:
			case ImageFormat.LINEAR_RGBA16161616:
				return new Vector4(
					BinaryPrimitives.ReadUInt16LittleEndian( pixel ) * shortScale,
					BinaryPrimitives.ReadUInt16LittleEndian( pixel[2..] ) * shortScale,
					BinaryPrimitives.ReadUInt16LittleEndian( pixel[4..] ) * shortScale,
					BinaryPrimitives.ReadUInt16LittleEndian( pixel[6..] ) * shortScale );

			case ImageFormat.RGBA16161616F:
				return new Vector4(
					(float)BitConverter.UInt16BitsToHalf( BinaryPrimitives.ReadUInt16LittleEndian( pixel ) ),
					(float)BitConverter.UInt16BitsToHalf( BinaryPrimitives.ReadUInt16LittleEndian( pixel[2..] ) ),
					(float)BitConverter.UInt16BitsToHalf( BinaryPrimitives.ReadUInt16LittleEndian( pixel[4..] ) ),
					(float)BitConverter.UInt16BitsToHalf( BinaryPrimitives.ReadUInt16LittleEndian( pixel[6..] ) ) );

			case ImageFormat.R32F:
				return new Vector4( BinaryPrimitives.ReadSingleLittleEndian( pixel ), 0, 0, 1 );

			case ImageFormat.RG3232F:
				return new Vector4(
					BinaryPrimitives.ReadSingleLittleEndian( pixel ),
					BinaryPrimitives.ReadSingleLittleEndian( pixel[4..] ), 0, 1 );

			case ImageFormat.RGB323232F:
				return new Vector4(
					BinaryPrimitives.ReadSingleLittleEndian( pixel ),
					BinaryPrimitives.ReadSingleLittleEndian( pixel[4..] ),
					BinaryPrimitives.ReadSingleLittleEndian( pixel[8..] ), 1 );

			case ImageFormat.RGBA32323232F:
				return new Vector4(
					BinaryPrimitives.ReadSingleLittleEndian( pixel ),
					BinaryPrimitives.ReadSingleLittleEndian( pixel[4..] ),
					BinaryPrimitives.ReadSingleLittleEndian( pixel[8..] ),
					BinaryPrimitives.ReadSingleLittleEndian( pixel[12..] ) );

			default:
				return Vector4.Zero;
		}
	}

	//Writes one pixel. Values outside nought to one are clamped where the format cannot hold them and kept where it can.
	internal static void Write( Span<byte> pixel, ImageFormat format, Vector4 value )
	{
		switch ( format )
		{
			case ImageFormat.RGBA8888:
			case ImageFormat.LINEAR_RGBA8888:
			case ImageFormat.RGBA8888_LINEAR:
				pixel[0] = Byte( value.x ); pixel[1] = Byte( value.y ); pixel[2] = Byte( value.z ); pixel[3] = Byte( value.w );
				break;

			case ImageFormat.RGBX8888:
				pixel[0] = Byte( value.x ); pixel[1] = Byte( value.y ); pixel[2] = Byte( value.z ); pixel[3] = 255;
				break;

			case ImageFormat.BGRA8888:
			case ImageFormat.LINEAR_BGRA8888:
			case ImageFormat.BGRA8888_LINEAR:
				pixel[0] = Byte( value.z ); pixel[1] = Byte( value.y ); pixel[2] = Byte( value.x ); pixel[3] = Byte( value.w );
				break;

			case ImageFormat.BGRX8888:
			case ImageFormat.LINEAR_BGRX8888:
			case ImageFormat.BGRX8888_LINEAR:
				pixel[0] = Byte( value.z ); pixel[1] = Byte( value.y ); pixel[2] = Byte( value.x ); pixel[3] = 255;
				break;

			case ImageFormat.ARGB8888:
			case ImageFormat.LINEAR_ARGB8888:
				pixel[0] = Byte( value.w ); pixel[1] = Byte( value.x ); pixel[2] = Byte( value.y ); pixel[3] = Byte( value.z );
				break;

			case ImageFormat.ABGR8888:
			case ImageFormat.LINEAR_ABGR8888:
				pixel[0] = Byte( value.w ); pixel[1] = Byte( value.z ); pixel[2] = Byte( value.y ); pixel[3] = Byte( value.x );
				break;

			case ImageFormat.RGB888:
			case ImageFormat.LINEAR_RGB888:
				pixel[0] = Byte( value.x ); pixel[1] = Byte( value.y ); pixel[2] = Byte( value.z );
				break;

			case ImageFormat.BGR888:
			case ImageFormat.LINEAR_BGR888:
				pixel[0] = Byte( value.z ); pixel[1] = Byte( value.y ); pixel[2] = Byte( value.x );
				break;

			case ImageFormat.I8:
			case ImageFormat.LINEAR_I8:
				pixel[0] = Byte( Luminance( value ) );
				break;

			case ImageFormat.A8:
				pixel[0] = Byte( value.w );
				break;

			case ImageFormat.IA88:
				pixel[0] = Byte( Luminance( value ) ); pixel[1] = Byte( value.w );
				break;

			case ImageFormat.R16:
				BinaryPrimitives.WriteUInt16LittleEndian( pixel, Short( value.x ) );
				break;

			case ImageFormat.R16F:
			case ImageFormat.I16F:
				BinaryPrimitives.WriteUInt16LittleEndian( pixel, BitConverter.HalfToUInt16Bits( (Half)value.x ) );
				break;

			case ImageFormat.RG1616F:
				BinaryPrimitives.WriteUInt16LittleEndian( pixel, BitConverter.HalfToUInt16Bits( (Half)value.x ) );
				BinaryPrimitives.WriteUInt16LittleEndian( pixel[2..], BitConverter.HalfToUInt16Bits( (Half)value.y ) );
				break;

			case ImageFormat.RGBA16161616:
			case ImageFormat.LINEAR_RGBA16161616:
				BinaryPrimitives.WriteUInt16LittleEndian( pixel, Short( value.x ) );
				BinaryPrimitives.WriteUInt16LittleEndian( pixel[2..], Short( value.y ) );
				BinaryPrimitives.WriteUInt16LittleEndian( pixel[4..], Short( value.z ) );
				BinaryPrimitives.WriteUInt16LittleEndian( pixel[6..], Short( value.w ) );
				break;

			case ImageFormat.RGBA16161616F:
				BinaryPrimitives.WriteUInt16LittleEndian( pixel, BitConverter.HalfToUInt16Bits( (Half)value.x ) );
				BinaryPrimitives.WriteUInt16LittleEndian( pixel[2..], BitConverter.HalfToUInt16Bits( (Half)value.y ) );
				BinaryPrimitives.WriteUInt16LittleEndian( pixel[4..], BitConverter.HalfToUInt16Bits( (Half)value.z ) );
				BinaryPrimitives.WriteUInt16LittleEndian( pixel[6..], BitConverter.HalfToUInt16Bits( (Half)value.w ) );
				break;

			case ImageFormat.R32F:
				BinaryPrimitives.WriteSingleLittleEndian( pixel, value.x );
				break;

			case ImageFormat.RG3232F:
				BinaryPrimitives.WriteSingleLittleEndian( pixel, value.x );
				BinaryPrimitives.WriteSingleLittleEndian( pixel[4..], value.y );
				break;

			case ImageFormat.RGB323232F:
				BinaryPrimitives.WriteSingleLittleEndian( pixel, value.x );
				BinaryPrimitives.WriteSingleLittleEndian( pixel[4..], value.y );
				BinaryPrimitives.WriteSingleLittleEndian( pixel[8..], value.z );
				break;

			case ImageFormat.RGBA32323232F:
				BinaryPrimitives.WriteSingleLittleEndian( pixel, value.x );
				BinaryPrimitives.WriteSingleLittleEndian( pixel[4..], value.y );
				BinaryPrimitives.WriteSingleLittleEndian( pixel[8..], value.z );
				BinaryPrimitives.WriteSingleLittleEndian( pixel[12..], value.w );
				break;
		}
	}

	//Converts a whole image from one format to another, row by row.
	internal static void Convert( ReadOnlySpan<byte> source, ImageFormat sourceFormat, int sourceStride,
		Span<byte> destination, ImageFormat destinationFormat, int destinationStride,
		int width, int height )
	{
		var sourcePixel = PixelBytes( sourceFormat );
		var destinationPixel = PixelBytes( destinationFormat );

		if ( sourcePixel == 0 || destinationPixel == 0 ) return;

		//The same format is a copy, and a copy of a row at a time is the same answer without unpacking every pixel into floats and back.
		var same = sourceFormat == destinationFormat;

		for ( var y = 0; y < height; y++ )
		{
			var sourceRow = source[(y * sourceStride)..];
			var destinationRow = destination[(y * destinationStride)..];

			if ( same )
			{
				sourceRow[..(width * sourcePixel)].CopyTo( destinationRow );
				continue;
			}

			for ( var x = 0; x < width; x++ )
			{
				var value = Read( sourceRow[(x * sourcePixel)..], sourceFormat );
				Write( destinationRow[(x * destinationPixel)..], destinationFormat, value );
			}
		}
	}

	private static byte Byte( float value ) => (byte)Math.Clamp( (int)MathF.Round( value * 255.0f ), 0, 255 );
	private static ushort Short( float value ) => (ushort)Math.Clamp( (int)MathF.Round( value * 65535.0f ), 0, 65535 );

	//The one channel a luminance format keeps. The older weights, from television rather than from high definition. They are
	//the ones the legacy image loader uses, and a texture written by one and read by the other has to come back the same shade.
	private static float Luminance( Vector4 value )
		=> (value.x * 0.299f) + (value.y * 0.587f) + (value.z * 0.114f);
}
