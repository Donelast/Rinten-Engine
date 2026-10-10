namespace Rinten;

//Computes image memory size: bits per pixel for plain formats, 4x4 blocks for compressed ones
internal static class ImageFormatSize
{
	//A compressed format stores four-by-four blocks, so its smallest mips are a whole block however few pixels they hold - which is why the width and height are rounded up rather than multiplied out.
	/// <summary>The size of one mip of an image, in bytes.</summary>
	public static int Level( int width, int height, int depth, ImageFormat format )
	{
		if ( width <= 0 || height <= 0 || depth <= 0 ) return 0;

		var block = BlockBytes( format );

		if ( block > 0 )
		{
			var across = ( width + 3 ) / 4;
			var down = ( height + 3 ) / 4;

			return across * down * depth * block;
		}

		var bits = Bits( format );

		//Rounded up to the byte: the packed sixteen-bit formats are two bytes and nothing here is narrower than a byte per pixel.
		return ( ( width * bits ) + 7 ) / 8 * height * depth;
	}

	/// <summary>The size of an image with this many mips, in bytes. Zero or fewer mips means the whole chain down to one pixel.</summary>
	public static int Total( int width, int height, int depth, int mips, ImageFormat format )
	{
		if ( width <= 0 || height <= 0 || depth <= 0 ) return 0;

		var total = 0;

		for ( var level = 0; mips <= 0 || level < mips; level++ )
		{
			total += Level( width, height, depth, format );

			if ( width == 1 && height == 1 && depth == 1 ) break;

			width = System.Math.Max( 1, width / 2 );
			height = System.Math.Max( 1, height / 2 );
			depth = System.Math.Max( 1, depth / 2 );
		}

		return total;
	}

	//Bytes per four-by-four block, or zero where the format is not one that works in blocks.
	private static int BlockBytes( ImageFormat format ) => format switch
	{
		ImageFormat.DXT1 or ImageFormat.DXT1_ONEBITALPHA => 8,
		ImageFormat.ATI1N or ImageFormat.R11_EAC => 8,
		ImageFormat.R8G8B8_ETC2 => 8,

		ImageFormat.DXT3 or ImageFormat.DXT5 or ImageFormat.DXT5_NM => 16,
		ImageFormat.ATI2N or ImageFormat.RG11_EAC => 16,
		ImageFormat.BC6H or ImageFormat.BC7 => 16,
		ImageFormat.R8G8B8A8_ETC2_EAC => 16,

		_ => 0,
	};

	//Bits per pixel, for the formats that store pixels.
	private static int Bits( ImageFormat format ) => format switch
	{
		ImageFormat.RGBA32323232F or ImageFormat.RGBA32323232 => 128,

		ImageFormat.RGB323232F => 96,

		ImageFormat.RGBA16161616F or ImageFormat.RGBA16161616 or
		ImageFormat.LINEAR_RGBA16161616 or ImageFormat.RG3232F or ImageFormat.RG3232 => 64,

		ImageFormat.RGBA8888 or ImageFormat.ABGR8888 or ImageFormat.ARGB8888 or
		ImageFormat.BGRA8888 or ImageFormat.BGRX8888 or ImageFormat.RGBX8888 or
		ImageFormat.LINEAR_BGRX8888 or ImageFormat.LINEAR_RGBA8888 or
		ImageFormat.LINEAR_ABGR8888 or ImageFormat.LINEAR_ARGB8888 or
		ImageFormat.LINEAR_BGRA8888 or ImageFormat.LE_BGRX8888 or ImageFormat.LE_BGRA8888 or
		ImageFormat.RGBA8888_LINEAR or ImageFormat.BGRA8888_LINEAR or ImageFormat.BGRX8888_LINEAR or
		ImageFormat.RGBA1010102 or ImageFormat.BGRA1010102 or
		ImageFormat.RG1616F or ImageFormat.RG1616 or
		ImageFormat.R32F or ImageFormat.R32_UINT or
		ImageFormat.D32 or ImageFormat.D24S8 or ImageFormat.LINEAR_D24S8 or
		ImageFormat.D24X8 or ImageFormat.D24X4S4 or ImageFormat.D24FS8 or ImageFormat.D32FS8 => 32,

		ImageFormat.RGB888 or ImageFormat.BGR888 or
		ImageFormat.LINEAR_RGB888 or ImageFormat.LINEAR_BGR888 => 24,

		ImageFormat.RGB565 or ImageFormat.BGR565 or ImageFormat.BGRX5551 or
		ImageFormat.BGRA4444 or ImageFormat.BGRA5551 or ImageFormat.LINEAR_BGRX5551 or
		ImageFormat.RGBX555 or ImageFormat.IA88 or
		ImageFormat.R16 or ImageFormat.R16F or ImageFormat.I16F or
		ImageFormat.D16 or ImageFormat.D15S1 => 16,

		ImageFormat.I8 or ImageFormat.A8 or ImageFormat.LINEAR_I8 => 8,

		//Nothing else is a picture: None, Default, and whatever is added later and not described here.
		//Nought rather than a guess - a wrong size is a buffer overrun, and nought is a visible mistake.
		_ => 0,
	};
}
