namespace Rinten;

/// <summary>
/// BC7, every mode.
/// </summary>
/// <remarks>
/// BC7 has eight modes. Four of them cut the block into two or three pieces
/// along one of sixty-four fixed patterns; the other three - four, five and six -
/// use the whole block. The awkward part of the format is data rather than
/// arithmetic: the patterns are tables out of the specification, and written
/// out by hand they are a place to make a mistake that nothing catches - the
/// picture comes out subtly wrong in a way no test tells apart from art.
/// <para>
/// So the tables were not written here. They are bcdec's, by Sergii Kudlai
/// under the MIT licence, carried over number for number along with the
/// decoder's order of operations - which is what a character's skin, stored in
/// one of the cut modes, decodes through. The native decoder this replaces was
/// bcdec too, so what comes out is what came out.
/// </para>
/// </remarks>
internal static partial class CoreBlockCompression
{
	//Reads bits out of a sixteen byte block, lowest first, the way the format is written.
	private ref struct BitReader
	{
		private ulong low;
		private ulong high;

		public BitReader( ReadOnlySpan<byte> bytes )
		{
			low = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian( bytes );
			high = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian( bytes[8..] );
		}

		public int Read( int count )
		{
			var mask = ( 1u << count ) - 1;
			var bits = (int)( low & mask );

			//The low N bits go out; the low N bits of "high" come in at the top.
			low >>= count;
			low |= ( high & mask ) << ( 64 - count );
			high >>= count;

			return bits;
		}

		public int ReadBit() => Read( 1 );
	}

	//The weights an index stands for, for two, three and four bit indices. Straight from the format: what the hardware interpolates with.
	private static ReadOnlySpan<byte> Weights2 => [0, 21, 43, 64];
	private static ReadOnlySpan<byte> Weights3 => [0, 9, 18, 27, 37, 46, 55, 64];
	private static ReadOnlySpan<byte> Weights4 => [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

	//How many bits an endpoint's colour channels have in each mode, and its alpha.
	private static ReadOnlySpan<byte> ColourBits => [4, 6, 5, 7, 5, 7, 7, 5];
	private static ReadOnlySpan<byte> AlphaBits => [0, 0, 0, 0, 6, 8, 7, 5];

	//The modes whose endpoints carry a low bit of their own, one bit a mode.
	private const int ModesWithPBits = 0b11001011;

	//The sixty four shapes a two subset block can be cut along, sixteen pixels a shape, row by row.
	//The top bit marks the fix-up pixel of a subset, whose index is written with one bit fewer.
	private static ReadOnlySpan<byte> Partitions2 => [
		128,   0,   1,   1,   0,   0,   1,   1,   0,   0,   1,   1,   0,   0,   1, 129, //0
		128,   0,   0,   1,   0,   0,   0,   1,   0,   0,   0,   1,   0,   0,   0, 129, //1
		128,   1,   1,   1,   0,   1,   1,   1,   0,   1,   1,   1,   0,   1,   1, 129, //2
		128,   0,   0,   1,   0,   0,   1,   1,   0,   0,   1,   1,   0,   1,   1, 129, //3
		128,   0,   0,   0,   0,   0,   0,   1,   0,   0,   0,   1,   0,   0,   1, 129, //4
		128,   0,   1,   1,   0,   1,   1,   1,   0,   1,   1,   1,   1,   1,   1, 129, //5
		128,   0,   0,   1,   0,   0,   1,   1,   0,   1,   1,   1,   1,   1,   1, 129, //6
		128,   0,   0,   0,   0,   0,   0,   1,   0,   0,   1,   1,   0,   1,   1, 129, //7
		128,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   1,   0,   0,   1, 129, //8
		128,   0,   1,   1,   0,   1,   1,   1,   1,   1,   1,   1,   1,   1,   1, 129, //9
		128,   0,   0,   0,   0,   0,   0,   1,   0,   1,   1,   1,   1,   1,   1, 129, //10
		128,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   1,   0,   1,   1, 129, //11
		128,   0,   0,   1,   0,   1,   1,   1,   1,   1,   1,   1,   1,   1,   1, 129, //12
		128,   0,   0,   0,   0,   0,   0,   0,   1,   1,   1,   1,   1,   1,   1, 129, //13
		128,   0,   0,   0,   1,   1,   1,   1,   1,   1,   1,   1,   1,   1,   1, 129, //14
		128,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   1,   1,   1, 129, //15
		128,   0,   0,   0,   1,   0,   0,   0,   1,   1,   1,   0,   1,   1,   1, 129, //16
		128,   1, 129,   1,   0,   0,   0,   1,   0,   0,   0,   0,   0,   0,   0,   0, //17
		128,   0,   0,   0,   0,   0,   0,   0, 129,   0,   0,   0,   1,   1,   1,   0, //18
		128,   1, 129,   1,   0,   0,   1,   1,   0,   0,   0,   1,   0,   0,   0,   0, //19
		128,   0, 129,   1,   0,   0,   0,   1,   0,   0,   0,   0,   0,   0,   0,   0, //20
		128,   0,   0,   0,   1,   0,   0,   0, 129,   1,   0,   0,   1,   1,   1,   0, //21
		128,   0,   0,   0,   0,   0,   0,   0, 129,   0,   0,   0,   1,   1,   0,   0, //22
		128,   1,   1,   1,   0,   0,   1,   1,   0,   0,   1,   1,   0,   0,   0, 129, //23
		128,   0, 129,   1,   0,   0,   0,   1,   0,   0,   0,   1,   0,   0,   0,   0, //24
		128,   0,   0,   0,   1,   0,   0,   0, 129,   0,   0,   0,   1,   1,   0,   0, //25
		128,   1, 129,   0,   0,   1,   1,   0,   0,   1,   1,   0,   0,   1,   1,   0, //26
		128,   0, 129,   1,   0,   1,   1,   0,   0,   1,   1,   0,   1,   1,   0,   0, //27
		128,   0,   0,   1,   0,   1,   1,   1, 129,   1,   1,   0,   1,   0,   0,   0, //28
		128,   0,   0,   0,   1,   1,   1,   1, 129,   1,   1,   1,   0,   0,   0,   0, //29
		128,   1, 129,   1,   0,   0,   0,   1,   1,   0,   0,   0,   1,   1,   1,   0, //30
		128,   0, 129,   1,   1,   0,   0,   1,   1,   0,   0,   1,   1,   1,   0,   0, //31
		128,   1,   0,   1,   0,   1,   0,   1,   0,   1,   0,   1,   0,   1,   0, 129, //32
		128,   0,   0,   0,   1,   1,   1,   1,   0,   0,   0,   0,   1,   1,   1, 129, //33
		128,   1,   0,   1,   1,   0, 129,   0,   0,   1,   0,   1,   1,   0,   1,   0, //34
		128,   0,   1,   1,   0,   0,   1,   1, 129,   1,   0,   0,   1,   1,   0,   0, //35
		128,   0, 129,   1,   1,   1,   0,   0,   0,   0,   1,   1,   1,   1,   0,   0, //36
		128,   1,   0,   1,   0,   1,   0,   1, 129,   0,   1,   0,   1,   0,   1,   0, //37
		128,   1,   1,   0,   1,   0,   0,   1,   0,   1,   1,   0,   1,   0,   0, 129, //38
		128,   1,   0,   1,   1,   0,   1,   0,   1,   0,   1,   0,   0,   1,   0, 129, //39
		128,   1, 129,   1,   0,   0,   1,   1,   1,   1,   0,   0,   1,   1,   1,   0, //40
		128,   0,   0,   1,   0,   0,   1,   1, 129,   1,   0,   0,   1,   0,   0,   0, //41
		128,   0, 129,   1,   0,   0,   1,   0,   0,   1,   0,   0,   1,   1,   0,   0, //42
		128,   0, 129,   1,   1,   0,   1,   1,   1,   1,   0,   1,   1,   1,   0,   0, //43
		128,   1, 129,   0,   1,   0,   0,   1,   1,   0,   0,   1,   0,   1,   1,   0, //44
		128,   0,   1,   1,   1,   1,   0,   0,   1,   1,   0,   0,   0,   0,   1, 129, //45
		128,   1,   1,   0,   0,   1,   1,   0,   1,   0,   0,   1,   1,   0,   0, 129, //46
		128,   0,   0,   0,   0,   1, 129,   0,   0,   1,   1,   0,   0,   0,   0,   0, //47
		128,   1,   0,   0,   1,   1, 129,   0,   0,   1,   0,   0,   0,   0,   0,   0, //48
		128,   0, 129,   0,   0,   1,   1,   1,   0,   0,   1,   0,   0,   0,   0,   0, //49
		128,   0,   0,   0,   0,   0, 129,   0,   0,   1,   1,   1,   0,   0,   1,   0, //50
		128,   0,   0,   0,   0,   1,   0,   0, 129,   1,   1,   0,   0,   1,   0,   0, //51
		128,   1,   1,   0,   1,   1,   0,   0,   1,   0,   0,   1,   0,   0,   1, 129, //52
		128,   0,   1,   1,   0,   1,   1,   0,   1,   1,   0,   0,   1,   0,   0, 129, //53
		128,   1, 129,   0,   0,   0,   1,   1,   1,   0,   0,   1,   1,   1,   0,   0, //54
		128,   0, 129,   1,   1,   0,   0,   1,   1,   1,   0,   0,   0,   1,   1,   0, //55
		128,   1,   1,   0,   1,   1,   0,   0,   1,   1,   0,   0,   1,   0,   0, 129, //56
		128,   1,   1,   0,   0,   0,   1,   1,   0,   0,   1,   1,   1,   0,   0, 129, //57
		128,   1,   1,   1,   1,   1,   1,   0,   1,   0,   0,   0,   0,   0,   0, 129, //58
		128,   0,   0,   1,   1,   0,   0,   0,   1,   1,   1,   0,   0,   1,   1, 129, //59
		128,   0,   0,   0,   1,   1,   1,   1,   0,   0,   1,   1,   0,   0,   1, 129, //60
		128,   0, 129,   1,   0,   0,   1,   1,   1,   1,   1,   1,   0,   0,   0,   0, //61
		128,   0, 129,   0,   0,   0,   1,   0,   1,   1,   1,   0,   1,   1,   1,   0, //62
		128,   1,   0,   0,   0,   1,   0,   0,   0,   1,   1,   1,   0,   1,   1, 129, //63
	];

	//The same for three subsets.
	private static ReadOnlySpan<byte> Partitions3 => [
		128,   0,   1, 129,   0,   0,   1,   1,   0,   2,   2,   1,   2,   2,   2, 130, //0
		128,   0,   0, 129,   0,   0,   1,   1, 130,   2,   1,   1,   2,   2,   2,   1, //1
		128,   0,   0,   0,   2,   0,   0,   1, 130,   2,   1,   1,   2,   2,   1, 129, //2
		128,   2,   2, 130,   0,   0,   2,   2,   0,   0,   1,   1,   0,   1,   1, 129, //3
		128,   0,   0,   0,   0,   0,   0,   0, 129,   1,   2,   2,   1,   1,   2, 130, //4
		128,   0,   1, 129,   0,   0,   1,   1,   0,   0,   2,   2,   0,   0,   2, 130, //5
		128,   0,   2, 130,   0,   0,   2,   2,   1,   1,   1,   1,   1,   1,   1, 129, //6
		128,   0,   1,   1,   0,   0,   1,   1, 130,   2,   1,   1,   2,   2,   1, 129, //7
		128,   0,   0,   0,   0,   0,   0,   0, 129,   1,   1,   1,   2,   2,   2, 130, //8
		128,   0,   0,   0,   1,   1,   1,   1, 129,   1,   1,   1,   2,   2,   2, 130, //9
		128,   0,   0,   0,   1,   1, 129,   1,   2,   2,   2,   2,   2,   2,   2, 130, //10
		128,   0,   1,   2,   0,   0, 129,   2,   0,   0,   1,   2,   0,   0,   1, 130, //11
		128,   1,   1,   2,   0,   1, 129,   2,   0,   1,   1,   2,   0,   1,   1, 130, //12
		128,   1,   2,   2,   0, 129,   2,   2,   0,   1,   2,   2,   0,   1,   2, 130, //13
		128,   0,   1, 129,   0,   1,   1,   2,   1,   1,   2,   2,   1,   2,   2, 130, //14
		128,   0,   1, 129,   2,   0,   0,   1, 130,   2,   0,   0,   2,   2,   2,   0, //15
		128,   0,   0, 129,   0,   0,   1,   1,   0,   1,   1,   2,   1,   1,   2, 130, //16
		128,   1,   1, 129,   0,   0,   1,   1, 130,   0,   0,   1,   2,   2,   0,   0, //17
		128,   0,   0,   0,   1,   1,   2,   2, 129,   1,   2,   2,   1,   1,   2, 130, //18
		128,   0,   2, 130,   0,   0,   2,   2,   0,   0,   2,   2,   1,   1,   1, 129, //19
		128,   1,   1, 129,   0,   1,   1,   1,   0,   2,   2,   2,   0,   2,   2, 130, //20
		128,   0,   0, 129,   0,   0,   0,   1, 130,   2,   2,   1,   2,   2,   2,   1, //21
		128,   0,   0,   0,   0,   0, 129,   1,   0,   1,   2,   2,   0,   1,   2, 130, //22
		128,   0,   0,   0,   1,   1,   0,   0, 130,   2, 129,   0,   2,   2,   1,   0, //23
		128,   1,   2, 130,   0, 129,   2,   2,   0,   0,   1,   1,   0,   0,   0,   0, //24
		128,   0,   1,   2,   0,   0,   1,   2, 129,   1,   2,   2,   2,   2,   2, 130, //25
		128,   1,   1,   0,   1,   2, 130,   1, 129,   2,   2,   1,   0,   1,   1,   0, //26
		128,   0,   0,   0,   0,   1, 129,   0,   1,   2, 130,   1,   1,   2,   2,   1, //27
		128,   0,   2,   2,   1,   1,   0,   2, 129,   1,   0,   2,   0,   0,   2, 130, //28
		128,   1,   1,   0,   0, 129,   1,   0,   2,   0,   0,   2,   2,   2,   2, 130, //29
		128,   0,   1,   1,   0,   1,   2,   2,   0,   1, 130,   2,   0,   0,   1, 129, //30
		128,   0,   0,   0,   2,   0,   0,   0, 130,   2,   1,   1,   2,   2,   2, 129, //31
		128,   0,   0,   0,   0,   0,   0,   2, 129,   1,   2,   2,   1,   2,   2, 130, //32
		128,   2,   2, 130,   0,   0,   2,   2,   0,   0,   1,   2,   0,   0,   1, 129, //33
		128,   0,   1, 129,   0,   0,   1,   2,   0,   0,   2,   2,   0,   2,   2, 130, //34
		128,   1,   2,   0,   0, 129,   2,   0,   0,   1, 130,   0,   0,   1,   2,   0, //35
		128,   0,   0,   0,   1,   1, 129,   1,   2,   2, 130,   2,   0,   0,   0,   0, //36
		128,   1,   2,   0,   1,   2,   0,   1, 130,   0, 129,   2,   0,   1,   2,   0, //37
		128,   1,   2,   0,   2,   0,   1,   2, 129, 130,   0,   1,   0,   1,   2,   0, //38
		128,   0,   1,   1,   2,   2,   0,   0,   1,   1, 130,   2,   0,   0,   1, 129, //39
		128,   0,   1,   1,   1,   1, 130,   2,   2,   2,   0,   0,   0,   0,   1, 129, //40
		128,   1,   0, 129,   0,   1,   0,   1,   2,   2,   2,   2,   2,   2,   2, 130, //41
		128,   0,   0,   0,   0,   0,   0,   0, 130,   1,   2,   1,   2,   1,   2, 129, //42
		128,   0,   2,   2,   1, 129,   2,   2,   0,   0,   2,   2,   1,   1,   2, 130, //43
		128,   0,   2, 130,   0,   0,   1,   1,   0,   0,   2,   2,   0,   0,   1, 129, //44
		128,   2,   2,   0,   1,   2, 130,   1,   0,   2,   2,   0,   1,   2,   2, 129, //45
		128,   1,   0,   1,   2,   2, 130,   2,   2,   2,   2,   2,   0,   1,   0, 129, //46
		128,   0,   0,   0,   2,   1,   2,   1, 130,   1,   2,   1,   2,   1,   2, 129, //47
		128,   1,   0, 129,   0,   1,   0,   1,   0,   1,   0,   1,   2,   2,   2, 130, //48
		128,   2,   2, 130,   0,   1,   1,   1,   0,   2,   2,   2,   0,   1,   1, 129, //49
		128,   0,   0,   2,   1, 129,   1,   2,   0,   0,   0,   2,   1,   1,   1, 130, //50
		128,   0,   0,   0,   2, 129,   1,   2,   2,   1,   1,   2,   2,   1,   1, 130, //51
		128,   2,   2,   2,   0, 129,   1,   1,   0,   1,   1,   1,   0,   2,   2, 130, //52
		128,   0,   0,   2,   1,   1,   1,   2, 129,   1,   1,   2,   0,   0,   0, 130, //53
		128,   1,   1,   0,   0, 129,   1,   0,   0,   1,   1,   0,   2,   2,   2, 130, //54
		128,   0,   0,   0,   0,   0,   0,   0,   2,   1, 129,   2,   2,   1,   1, 130, //55
		128,   1,   1,   0,   0, 129,   1,   0,   2,   2,   2,   2,   2,   2,   2, 130, //56
		128,   0,   2,   2,   0,   0,   1,   1,   0,   0, 129,   1,   0,   0,   2, 130, //57
		128,   0,   2,   2,   1,   1,   2,   2, 129,   1,   2,   2,   0,   0,   2, 130, //58
		128,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   2, 129,   1, 130, //59
		128,   0,   0, 130,   0,   0,   0,   1,   0,   0,   0,   2,   0,   0,   0, 129, //60
		128,   2,   2,   2,   1,   2,   2,   2,   0,   2,   2,   2, 129,   2,   2, 130, //61
		128,   1,   0, 129,   2,   2,   2,   2,   2,   2,   2,   2,   2,   2,   2, 130, //62
		128,   1,   1, 129,   2,   0,   1,   1, 130,   2,   0,   1,   2,   2,   2,   0, //63
	];

	private static byte Interpolate( int a, int b, int weight )
		=> (byte)( ( a * ( 64 - weight ) + b * weight + 32 ) >> 6 );

	//Unpacks one BC7 block into sixteen pixels of four bytes. A block in no mode at all -
	//eight zero bits - comes out transparent black, which is what the hardware shows for it.
	internal static bool DecodeBc7( ReadOnlySpan<byte> source, Span<byte> pixels )
	{
		var bits = new BitReader( source );

		//The mode is written as however many zero bits come before the first one - so mode six is a one in bit six.
		var mode = 0;

		while ( mode < 8 && bits.ReadBit() == 0 ) mode++;

		if ( mode >= 8 )
		{
			pixels[..64].Clear();
			return true;
		}

		var partition = 0;
		var subsets = 1;
		var rotation = 0;
		var indexSelection = 0;

		if ( mode == 0 || mode == 1 || mode == 2 || mode == 3 || mode == 7 )
		{
			subsets = ( mode == 0 || mode == 2 ) ? 3 : 2;
			partition = bits.Read( mode == 0 ? 4 : 6 );
		}

		var endpointCount = subsets * 2;

		if ( mode == 4 || mode == 5 )
		{
			rotation = bits.Read( 2 );

			if ( mode == 4 ) indexSelection = bits.ReadBit();
		}

		//Six endpoints of four channels, red first for every endpoint, then green, then blue, then alpha where the mode has one.
		Span<int> endpoints = stackalloc int[6 * 4];

		endpoints.Clear();

		for ( var channel = 0; channel < 3; ++channel )
		{
			for ( var e = 0; e < endpointCount; ++e )
				endpoints[( e * 4 ) + channel] = bits.Read( ColourBits[mode] );
		}

		if ( AlphaBits[mode] > 0 )
		{
			for ( var e = 0; e < endpointCount; ++e )
				endpoints[( e * 4 ) + 3] = bits.Read( AlphaBits[mode] );
		}

		//The modes with a low bit: every channel shifts up to make room, and the bit goes in
		//below - one bit shared by an endpoint pair in mode one, one bit each everywhere else.
		if ( mode == 0 || mode == 1 || mode == 3 || mode == 6 || mode == 7 )
		{
			for ( var i = 0; i < endpointCount * 4; ++i )
				endpoints[i] <<= 1;

			if ( mode == 1 )
			{
				var first = bits.ReadBit();
				var second = bits.ReadBit();

				for ( var k = 0; k < 3; ++k )
				{
					endpoints[( 0 * 4 ) + k] |= first;
					endpoints[( 1 * 4 ) + k] |= first;
					endpoints[( 2 * 4 ) + k] |= second;
					endpoints[( 3 * 4 ) + k] |= second;
				}
			}
			else if ( ( ModesWithPBits & ( 1 << mode ) ) != 0 )
			{
				for ( var e = 0; e < endpointCount; ++e )
				{
					var p = bits.ReadBit();

					for ( var k = 0; k < 4; ++k )
						endpoints[( e * 4 ) + k] |= p;
				}
			}
		}

		var hasPBit = ( ModesWithPBits >> mode ) & 1;

		for ( var e = 0; e < endpointCount; ++e )
		{
			//Up to eight bits, with the top bits repeated into the ones the shift revealed, so full scale stays full.
			var precision = ColourBits[mode] + hasPBit;

			for ( var k = 0; k < 3; ++k )
			{
				var value = endpoints[( e * 4 ) + k] << ( 8 - precision );
				endpoints[( e * 4 ) + k] = value | ( value >> precision );
			}

			precision = AlphaBits[mode] + hasPBit;

			var alpha = endpoints[( e * 4 ) + 3] << ( 8 - precision );
			endpoints[( e * 4 ) + 3] = alpha | ( alpha >> precision );
		}

		//A mode without alpha is opaque.
		if ( AlphaBits[mode] == 0 )
		{
			for ( var e = 0; e < endpointCount; ++e )
				endpoints[( e * 4 ) + 3] = 0xFF;
		}

		var indexBits = ( mode == 0 || mode == 1 ) ? 3 : ( mode == 6 ? 4 : 2 );
		var indexBits2 = mode == 4 ? 3 : ( mode == 5 ? 2 : 0 );

		var weights = indexBits == 2 ? Weights2 : ( indexBits == 3 ? Weights3 : Weights4 );
		var weights2 = indexBits2 == 2 ? Weights2 : Weights3;

		var shapes = subsets == 2 ? Partitions2 : Partitions3;

		//The indices are not interleaved with the alpha ones, so two passes: first every colour index, then the alpha indices as the pixels are written.
		Span<int> indices = stackalloc int[16];

		for ( var i = 0; i < 16; ++i )
		{
			var set = subsets == 1 ? ( i == 0 ? 128 : 0 ) : shapes[( partition * 16 ) + i];

			//The fix-up pixel of a subset is written with one bit fewer: its top bit is known to be zero.
			var count = indexBits;
			if ( ( set & 0x80 ) != 0 ) count--;

			indices[i] = bits.Read( count );
		}

		for ( var i = 0; i < 16; ++i )
		{
			var set = ( subsets == 1 ? ( i == 0 ? 128 : 0 ) : shapes[( partition * 16 ) + i] ) & 0x03;
			var index = indices[i];

			var e0 = set * 2 * 4;
			var e1 = e0 + 4;

			byte r, g, b, a;

			if ( indexBits2 == 0 )
			{
				var w = weights[index];

				r = Interpolate( endpoints[e0 + 0], endpoints[e1 + 0], w );
				g = Interpolate( endpoints[e0 + 1], endpoints[e1 + 1], w );
				b = Interpolate( endpoints[e0 + 2], endpoints[e1 + 2], w );
				a = Interpolate( endpoints[e0 + 3], endpoints[e1 + 3], w );
			}
			else
			{
				var index2 = bits.Read( i == 0 ? indexBits2 - 1 : indexBits2 );

				//The colour reads the secondary indices when the mode has a selection bit and it is
				//set, and the primary ones otherwise; the alpha takes whichever the colour did not.
				var w = weights[index];
				var w2 = weights2[index2];

				if ( indexSelection == 0 )
				{
					r = Interpolate( endpoints[e0 + 0], endpoints[e1 + 0], w );
					g = Interpolate( endpoints[e0 + 1], endpoints[e1 + 1], w );
					b = Interpolate( endpoints[e0 + 2], endpoints[e1 + 2], w );
					a = Interpolate( endpoints[e0 + 3], endpoints[e1 + 3], w2 );
				}
				else
				{
					r = Interpolate( endpoints[e0 + 0], endpoints[e1 + 0], w2 );
					g = Interpolate( endpoints[e0 + 1], endpoints[e1 + 1], w2 );
					b = Interpolate( endpoints[e0 + 2], endpoints[e1 + 2], w2 );
					a = Interpolate( endpoints[e0 + 3], endpoints[e1 + 3], w );
				}
			}

			//Modes four and five can store any one channel in the alpha slot, and say which.
			switch ( rotation )
			{
				case 1: ( a, r ) = ( r, a ); break;
				case 2: ( a, g ) = ( g, a ); break;
				case 3: ( a, b ) = ( b, a ); break;
			}

			pixels[( i * 4 ) + 0] = r;
			pixels[( i * 4 ) + 1] = g;
			pixels[( i * 4 ) + 2] = b;
			pixels[( i * 4 ) + 3] = a;
		}

		return true;
	}
}
