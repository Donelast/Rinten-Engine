using System.Runtime.CompilerServices;

namespace Rinten;

//Encodes RGBA8 to BC1/BC3 (not BC7, too complex), matching the native encoder byte for byte
internal static class CoreBlockEncoder
{
	//The sixteen pixels of one block, with the edges of the picture repeated where the block runs off it. Sixty four bytes, four a pixel.
	[InlineArray( 64 )]
	private struct Block
	{
		private byte _element0;
	}

	//The palette a pair of endpoints stands for: four colours of three channels.
	[InlineArray( 12 )]
	private struct Palette4
	{
		private int _element0;
	}

	/// <summary>How many bytes the blocks of one level take.</summary>
	public static int BlockByteCount( int width, int height, bool alpha )
	{
		var across = ( width + 3 ) / 4;
		var down = ( height + 3 ) / 4;

		return across * down * ( alpha ? 16 : 8 );
	}

	/// <summary>Whether every pixel is fully opaque, which is what decides BC1 or BC3.</summary>
	public static bool AllOpaque( ReadOnlySpan<byte> rgba, int pixels )
	{
		for ( var i = 0; i < pixels; ++i )
		{
			if ( rgba[( i * 4 ) + 3] != 255 ) return false;
		}

		return true;
	}

	//Edges are clamped rather than padded with black: a block that runs off the right of a 6-pixel-wide picture repeats the last column, so the endpoints it fits are the endpoints of real colours.
	/// <summary>One level, packed. <paramref name="output"/> must have room for <see cref="BlockByteCount"/> bytes.</summary>
	public static void EncodeBc1( ReadOnlySpan<byte> rgba, int width, int height, Span<byte> output )
	{
		if ( rgba.IsEmpty || output.IsEmpty || width == 0 || height == 0 ) return;

		var at = 0;

		for ( var y = 0; y < height; y += 4 )
		for ( var x = 0; x < width; x += 4 )
		{
			Block block = default;

			Gather( rgba, width, height, x, y, ref block );
			EncodeColour( ref block, output.Slice( at, 8 ) );

			at += 8;
		}
	}

	public static void EncodeBc3( ReadOnlySpan<byte> rgba, int width, int height, Span<byte> output )
	{
		if ( rgba.IsEmpty || output.IsEmpty || width == 0 || height == 0 ) return;

		var at = 0;

		for ( var y = 0; y < height; y += 4 )
		for ( var x = 0; x < width; x += 4 )
		{
			Block block = default;

			Gather( rgba, width, height, x, y, ref block );
			EncodeAlpha( ref block, output.Slice( at, 8 ) );
			EncodeColour( ref block, output.Slice( at + 8, 8 ) );

			at += 16;
		}
	}

	//Output must have room for BlockByteCount bytes without alpha.
	/// <summary>One channel - red, or whichever <paramref name="channel"/> names - as BC4: the alpha block of BC3 on its own. A mask, a height, a roughness.</summary>
	public static void EncodeBc4( ReadOnlySpan<byte> rgba, int width, int height, Span<byte> output, int channel = 0 )
	{
		if ( rgba.IsEmpty || output.IsEmpty || width == 0 || height == 0 ) return;

		var at = 0;

		for ( var y = 0; y < height; y += 4 )
		for ( var x = 0; x < width; x += 4 )
		{
			Block block = default;

			Gather( rgba, width, height, x, y, ref block );
			EncodeAlpha( ref block, output.Slice( at, 8 ), channel );

			at += 8;
		}
	}

	//Output must have room for BlockByteCount bytes with alpha.
	/// <summary>Red and green as BC5: two BC4 blocks side by side. What a normal map is stored as, the third axis rebuilt where it is read.</summary>
	public static void EncodeBc5( ReadOnlySpan<byte> rgba, int width, int height, Span<byte> output )
	{
		if ( rgba.IsEmpty || output.IsEmpty || width == 0 || height == 0 ) return;

		var at = 0;

		for ( var y = 0; y < height; y += 4 )
		for ( var x = 0; x < width; x += 4 )
		{
			Block block = default;

			Gather( rgba, width, height, x, y, ref block );
			EncodeAlpha( ref block, output.Slice( at, 8 ), 0 );
			EncodeAlpha( ref block, output.Slice( at + 8, 8 ), 1 );

			at += 16;
		}
	}

	private static void Gather( ReadOnlySpan<byte> rgba, int width, int height, int bx, int by, ref Block block )
	{
		Span<byte> pixels = block;

		for ( var y = 0; y < 4; ++y )
		for ( var x = 0; x < 4; ++x )
		{
			var sx = Math.Min( bx + x, width - 1 );
			var sy = Math.Min( by + y, height - 1 );

			rgba.Slice( ( ( sy * width ) + sx ) * 4, 4 ).CopyTo( pixels.Slice( ( ( y * 4 ) + x ) * 4, 4 ) );
		}
	}

	//Five, six and five bits, which is what a BC1 endpoint is.
	private static ushort To565( int r, int g, int b )
	{
		r = Math.Clamp( r, 0, 255 );
		g = Math.Clamp( g, 0, 255 );
		b = Math.Clamp( b, 0, 255 );

		return (ushort)( ( ( r >> 3 ) << 11 ) | ( ( g >> 2 ) << 5 ) | ( b >> 3 ) );
	}

	//And back, the way the hardware does it: the high bits are repeated into the low ones, so that white stays white.
	private static void From565( ushort value, out int r, out int g, out int b )
	{
		var r5 = ( value >> 11 ) & 31;
		var g6 = ( value >> 5 ) & 63;
		var b5 = value & 31;

		r = ( r5 << 3 ) | ( r5 >> 2 );
		g = ( g6 << 2 ) | ( g6 >> 4 );
		b = ( b5 << 3 ) | ( b5 >> 2 );
	}

	//Bounding box of a block's colours, inset by 1/16 at each end so outlier pixels do not waste range
	private static void BoundingLine( ref Block block, Span<int> low, Span<int> high )
	{
		ReadOnlySpan<byte> pixels = block;

		low[0] = low[1] = low[2] = 255;
		high[0] = high[1] = high[2] = 0;

		for ( var i = 0; i < 16; ++i )
		for ( var c = 0; c < 3; ++c )
		{
			int value = pixels[( i * 4 ) + c];

			low[c] = Math.Min( low[c], value );
			high[c] = Math.Max( high[c], value );
		}

		for ( var c = 0; c < 3; ++c )
		{
			var inset = ( high[c] - low[c] ) >> 4;

			low[c] = Math.Min( low[c] + inset, 255 );
			high[c] = Math.Max( high[c] - inset, 0 );
		}
	}

	//The four colours a pair of endpoints stands for, in eight bits.
	private static void MakePalette( ushort c0, ushort c1, ref Palette4 palette )
	{
		Span<int> o = palette;

		From565( c0, out o[0], out o[1], out o[2] );
		From565( c1, out o[3], out o[4], out o[5] );

		for ( var c = 0; c < 3; ++c )
		{
			o[6 + c] = ( ( 2 * o[c] ) + o[3 + c] ) / 3;
			o[9 + c] = ( o[c] + ( 2 * o[3 + c] ) ) / 3;
		}
	}

	//Which of the four each pixel is nearest, and how wrong that is.
	private static uint FitIndices( ref Block block, ref Palette4 palette, out uint indices )
	{
		ReadOnlySpan<byte> pixels = block;
		ReadOnlySpan<int> colours = palette;

		uint error = 0;

		indices = 0;

		for ( var i = 0; i < 16; ++i )
		{
			uint best = 0;
			var least = -1;

			for ( var p = 0; p < 4; ++p )
			{
				var distance = 0;

				for ( var c = 0; c < 3; ++c )
				{
					var difference = pixels[( i * 4 ) + c] - colours[( p * 3 ) + c];

					distance += difference * difference;
				}

				if ( least < 0 || distance < least )
				{
					least = distance;
					best = (uint)p;
				}
			}

			indices |= best << ( i * 2 );
			error += (uint)least;
		}

		return error;
	}

	private static ReadOnlySpan<float> ToFirst => [1.0f, 0.0f, 2.0f / 3.0f, 1.0f / 3.0f];
	private static ReadOnlySpan<float> ToSecond => [0.0f, 1.0f, 1.0f / 3.0f, 2.0f / 3.0f];

	//Least-squares refit of endpoints for the current index assignment (a 2x2 solve)
	private static bool Refine( ref Block block, uint indices, ref ushort c0, ref ushort c1 )
	{
		ReadOnlySpan<byte> pixels = block;

		var a = 0.0f;
		var b = 0.0f;
		var c = 0.0f;
		Span<float> first = stackalloc float[3];
		Span<float> second = stackalloc float[3];

		first.Clear();
		second.Clear();

		for ( var i = 0; i < 16; ++i )
		{
			var at = (int)( ( indices >> ( i * 2 ) ) & 3 );

			var f = ToFirst[at];
			var s = ToSecond[at];

			a += f * f;
			b += f * s;
			c += s * s;

			for ( var channel = 0; channel < 3; ++channel )
			{
				first[channel] += f * pixels[( i * 4 ) + channel];
				second[channel] += s * pixels[( i * 4 ) + channel];
			}
		}

		var determinant = ( a * c ) - ( b * b );

		//Every pixel landed on one endpoint: there is no line to fit.
		if ( determinant > -0.0001f && determinant < 0.0001f ) return false;

		Span<int> high = stackalloc int[3];
		Span<int> low = stackalloc int[3];

		for ( var channel = 0; channel < 3; ++channel )
		{
			high[channel] = (int)( ( ( ( c * first[channel] ) - ( b * second[channel] ) ) / determinant ) + 0.5f );
			low[channel] = (int)( ( ( ( a * second[channel] ) - ( b * first[channel] ) ) / determinant ) + 0.5f );
		}

		c0 = To565( high[0], high[1], high[2] );
		c1 = To565( low[0], low[1], low[2] );

		return true;
	}

	//Encodes one BC1 block, always four-colour mode, since three-colour mode would punch alpha holes
	private static void EncodeColour( ref Block block, Span<byte> output )
	{
		Span<int> low = stackalloc int[3];
		Span<int> high = stackalloc int[3];

		BoundingLine( ref block, low, high );

		var c0 = To565( high[0], high[1], high[2] );
		var c1 = To565( low[0], low[1], low[2] );

		if ( c0 == c1 )
		{
			//One colour: the pair decodes to it either way, and every index points at the first.
			Write16( output, c0 );
			Write16( output[2..], c1 );
			output.Slice( 4, 4 ).Clear();

			return;
		}

		if ( c0 < c1 ) ( c0, c1 ) = ( c1, c0 );

		Palette4 palette = default;

		MakePalette( c0, c1, ref palette );

		var error = FitIndices( ref block, ref palette, out var indices );

		//And once more with endpoints fitted to that assignment. Kept only if it is actually
		//better - a refinement that quantises back onto the same 565 grid can come out worse.
		var r0 = c0;
		var r1 = c1;

		if ( Refine( ref block, indices, ref r0, ref r1 ) && r0 != r1 )
		{
			if ( r0 < r1 ) ( r0, r1 ) = ( r1, r0 );

			Palette4 refined = default;

			MakePalette( r0, r1, ref refined );

			var after = FitIndices( ref block, ref refined, out var candidate );

			if ( after < error )
			{
				c0 = r0;
				c1 = r1;
				indices = candidate;
			}
		}

		Write16( output, c0 );
		Write16( output[2..], c1 );
		Write32( output[4..], indices );
	}

	//One alpha block - or one channel of BC4 and BC5: two ends and sixteen three-bit indices.
	private static void EncodeAlpha( ref Block block, Span<byte> output, int channel = 3 )
	{
		ReadOnlySpan<byte> pixels = block;

		var high = 0;
		var low = 255;

		for ( var i = 0; i < 16; ++i )
		{
			high = Math.Max( high, pixels[( i * 4 ) + channel] );
			low = Math.Min( low, pixels[( i * 4 ) + channel] );
		}

		output[0] = (byte)high;
		output[1] = (byte)low;

		if ( high == low )
		{
			output.Slice( 2, 6 ).Clear();
			return;
		}

		Span<int> palette = stackalloc int[8];

		palette[0] = high;
		palette[1] = low;

		for ( var i = 0; i < 6; ++i )
		{
			palette[2 + i] = ( ( ( 6 - i ) * high ) + ( ( 1 + i ) * low ) ) / 7;
		}

		ulong indices = 0;

		for ( var i = 0; i < 16; ++i )
		{
			int value = pixels[( i * 4 ) + channel];

			ulong best = 0;
			var least = -1;

			for ( var p = 0; p < 8; ++p )
			{
				var difference = value > palette[p] ? value - palette[p] : palette[p] - value;

				if ( least < 0 || difference < least )
				{
					least = difference;
					best = (ulong)p;
				}
			}

			indices |= best << ( i * 3 );
		}

		for ( var i = 0; i < 6; ++i )
			output[2 + i] = (byte)( ( indices >> ( i * 8 ) ) & 0xFF );
	}

	private static void Write16( Span<byte> into, ushort value )
	{
		into[0] = (byte)( value & 0xFF );
		into[1] = (byte)( value >> 8 );
	}

	private static void Write32( Span<byte> into, uint value )
	{
		into[0] = (byte)( value & 0xFF );
		into[1] = (byte)( ( value >> 8 ) & 0xFF );
		into[2] = (byte)( ( value >> 16 ) & 0xFF );
		into[3] = (byte)( value >> 24 );
	}
}
