namespace Rinten;

//Decodes GPU block-compressed formats to RGBA8 with bit-exact hardware colour maths
internal static partial class CoreBlockCompression
{
	/// <summary>How many bytes one four by four block takes.</summary>
	public static int BlockSize( Kind kind ) => kind switch
	{
		Kind.Bc1 => 8,
		Kind.Bc4 => 8,
		_ => 16,
	};

	//The formats this reads.
	internal enum Kind
	{
		/// <summary>Colour, one bit of alpha. Their DXT1.</summary>
		Bc1,

		/// <summary>Colour with a full alpha block in front. Their DXT5.</summary>
		Bc3,

		/// <summary>One channel. Their ATI1N - masks, heights.</summary>
		Bc4,

		/// <summary>Two channels. Their ATI2N - normal maps.</summary>
		Bc5,

		/// <summary>Colour and alpha at higher quality, in eight modes. Only the ones that use the whole block are read - see the other half of this class.</summary>
		Bc7,
	}

	/// <summary>Unpacks a whole image into eight bit RGBA, row by row from the top.</summary>
	public static byte[] Decode( Kind kind, ReadOnlySpan<byte> data, int width, int height )
	{
		if ( width <= 0 || height <= 0 ) return null;

		var pixels = new byte[width * height * 4];

		var blocksWide = ( width + 3 ) / 4;
		var blocksHigh = ( height + 3 ) / 4;
		var stride = BlockSize( kind );

		if ( data.Length < blocksWide * blocksHigh * stride ) return null;

		Span<byte> block = stackalloc byte[64]; //sixteen pixels, four channels

		for ( var by = 0; by < blocksHigh; by++ )
		{
			for ( var bx = 0; bx < blocksWide; bx++ )
			{
				var source = data.Slice( ( by * blocksWide + bx ) * stride, stride );

				switch ( kind )
				{
					case Kind.Bc1: DecodeBc1( source, block ); break;
					case Kind.Bc3: DecodeBc3( source, block ); break;
					case Kind.Bc4: DecodeBc4( source, block ); break;
					case Kind.Bc5: DecodeBc5( source, block ); break;

					//A block in a mode that is not read leaves the picture short, and saying so once beats a wrong texture.
					case Kind.Bc7 when !DecodeBc7( source, block ): return null;
					case Kind.Bc7: break;
				}

				//Into the picture, minding the edge: an image that is not a multiple of four has blocks hanging off the right and bottom.
				for ( var y = 0; y < 4; y++ )
				{
					var row = by * 4 + y;
					if ( row >= height ) break;

					for ( var x = 0; x < 4; x++ )
					{
						var column = bx * 4 + x;
						if ( column >= width ) break;

						var from = ( y * 4 + x ) * 4;
						var to = ( row * width + column ) * 4;

						pixels[to + 0] = block[from + 0];
						pixels[to + 1] = block[from + 1];
						pixels[to + 2] = block[from + 2];
						pixels[to + 3] = block[from + 3];
					}
				}
			}
		}

		return pixels;
	}

	//The colour half of a block: two endpoints in 565 and two bits a pixel saying where between them it sits.
	private static void DecodeColour( ReadOnlySpan<byte> source, Span<byte> pixels, bool opaque )
	{
		var c0 = (ushort)( source[0] | ( source[1] << 8 ) );
		var c1 = (ushort)( source[2] | ( source[3] << 8 ) );

		Span<byte> r = stackalloc byte[4];
		Span<byte> g = stackalloc byte[4];
		Span<byte> b = stackalloc byte[4];
		Span<byte> a = stackalloc byte[4] { 255, 255, 255, 255 };

		Unpack565( c0, out r[0], out g[0], out b[0] );
		Unpack565( c1, out r[1], out g[1], out b[1] );

		//Four colours when the first endpoint is the brighter of the two, three and a
		//transparent one when it is not - which is how a BC1 block says a pixel is cut out.
		if ( c0 > c1 || opaque )
		{
			r[2] = (byte)( ( 2 * r[0] + r[1] ) / 3 );
			g[2] = (byte)( ( 2 * g[0] + g[1] ) / 3 );
			b[2] = (byte)( ( 2 * b[0] + b[1] ) / 3 );

			r[3] = (byte)( ( r[0] + 2 * r[1] ) / 3 );
			g[3] = (byte)( ( g[0] + 2 * g[1] ) / 3 );
			b[3] = (byte)( ( b[0] + 2 * b[1] ) / 3 );
		}
		else
		{
			r[2] = (byte)( ( r[0] + r[1] ) / 2 );
			g[2] = (byte)( ( g[0] + g[1] ) / 2 );
			b[2] = (byte)( ( b[0] + b[1] ) / 2 );

			r[3] = g[3] = b[3] = 0;
			a[3] = 0;
		}

		var bits = (uint)( source[4] | ( source[5] << 8 ) | ( source[6] << 16 ) | ( source[7] << 24 ) );

		for ( var i = 0; i < 16; i++ )
		{
			var index = (int)( ( bits >> ( i * 2 ) ) & 0x3 );

			pixels[i * 4 + 0] = r[index];
			pixels[i * 4 + 1] = g[index];
			pixels[i * 4 + 2] = b[index];
			pixels[i * 4 + 3] = a[index];
		}
	}

	private static void Unpack565( ushort colour, out byte r, out byte g, out byte b )
	{
		var red = ( colour >> 11 ) & 0x1F;
		var green = ( colour >> 5 ) & 0x3F;
		var blue = colour & 0x1F;

		//Repeat the top bits into the bottom ones, so full scale stays full.
		r = (byte)( ( red << 3 ) | ( red >> 2 ) );
		g = (byte)( ( green << 2 ) | ( green >> 4 ) );
		b = (byte)( ( blue << 3 ) | ( blue >> 2 ) );
	}

	private static void DecodeBc1( ReadOnlySpan<byte> source, Span<byte> pixels )
		=> DecodeColour( source, pixels, opaque: false );

	private static void DecodeBc3( ReadOnlySpan<byte> source, Span<byte> pixels )
	{
		DecodeColour( source[8..], pixels, opaque: true );

		Span<byte> alpha = stackalloc byte[16];
		DecodeAlpha( source, alpha );

		for ( var i = 0; i < 16; i++ )
		{
			pixels[i * 4 + 3] = alpha[i];
		}
	}

	private static void DecodeBc4( ReadOnlySpan<byte> source, Span<byte> pixels )
	{
		Span<byte> value = stackalloc byte[16];
		DecodeAlpha( source, value );

		//One channel, shown as grey - which is what a mask looks like.
		for ( var i = 0; i < 16; i++ )
		{
			pixels[i * 4 + 0] = value[i];
			pixels[i * 4 + 1] = value[i];
			pixels[i * 4 + 2] = value[i];
			pixels[i * 4 + 3] = 255;
		}
	}

	private static void DecodeBc5( ReadOnlySpan<byte> source, Span<byte> pixels )
	{
		Span<byte> red = stackalloc byte[16];
		Span<byte> green = stackalloc byte[16];

		DecodeAlpha( source, red );
		DecodeAlpha( source[8..], green );

		for ( var i = 0; i < 16; i++ )
		{
			//A normal map: x and y are stored, z is what is left of the unit vector. Rebuilt here so what comes out is a normal map again.
			var x = red[i] / 255.0f * 2.0f - 1.0f;
			var y = green[i] / 255.0f * 2.0f - 1.0f;
			var z = MathF.Sqrt( MathF.Max( 0.0f, 1.0f - x * x - y * y ) );

			pixels[i * 4 + 0] = red[i];
			pixels[i * 4 + 1] = green[i];
			pixels[i * 4 + 2] = (byte)( ( z * 0.5f + 0.5f ) * 255.0f );
			pixels[i * 4 + 3] = 255;
		}
	}

	//The eight byte single channel block BC3, BC4 and BC5 are all built from: two endpoints and three bits a pixel.
	private static void DecodeAlpha( ReadOnlySpan<byte> source, Span<byte> values )
	{
		Span<byte> table = stackalloc byte[8];

		table[0] = source[0];
		table[1] = source[1];

		if ( table[0] > table[1] )
		{
			for ( var i = 1; i < 7; i++ )
				table[i + 1] = (byte)( ( ( 7 - i ) * table[0] + i * table[1] ) / 7 );
		}
		else
		{
			for ( var i = 1; i < 5; i++ )
				table[i + 1] = (byte)( ( ( 5 - i ) * table[0] + i * table[1] ) / 5 );

			table[6] = 0;
			table[7] = 255;
		}

		//Six bytes of three bit indices, read as one number a half block.
		var low = (ulong)source[2] | ( (ulong)source[3] << 8 ) | ( (ulong)source[4] << 16 );
		var high = (ulong)source[5] | ( (ulong)source[6] << 8 ) | ( (ulong)source[7] << 16 );

		for ( var i = 0; i < 8; i++ )
		{
			values[i] = table[(int)( ( low >> ( i * 3 ) ) & 0x7 )];
			values[i + 8] = table[(int)( ( high >> ( i * 3 ) ) & 0x7 )];
		}
	}
}
