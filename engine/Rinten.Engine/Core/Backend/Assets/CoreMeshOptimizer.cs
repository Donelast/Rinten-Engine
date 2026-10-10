namespace Rinten;

//Decodes meshoptimizer-compressed vertex buffers, ported from the codec; unknown versions rejected
internal static partial class CoreMeshOptimizer
{
	private static readonly Logger log = new( "Core" );

	//The top nibble of a compressed vertex buffer's first byte.
	private const byte VertexHeader = 0xa0;

	//The most bytes one block of vertices takes, and the most vertices in it.
	private const int BlockSizeBytes = 8192;
	private const int BlockMaxVertices = 256;

	//How many values a group holds, and the least bytes one can decode from.
	private const int GroupSize = 16;
	private const int GroupDecodeLimit = 24;

	//How many bits a value takes, for the two bit code in a group header. The second encoding
	//added one bit wide groups, and shifts the table by what the block's control byte says.
	private static readonly int[] WidthsV0 = { 0, 2, 4, 8 };
	private static readonly int[] WidthsV1 = { 0, 1, 2, 4, 8 };

	/// <summary>Unpacks a vertex buffer into the stride it was written with, or null where this is not something it can read.</summary>
	public static byte[] DecodeVertices( int count, int stride, ReadOnlySpan<byte> data )
	{
		if ( count <= 0 || stride <= 0 || stride > 256 || stride % 4 != 0 )
		{
			log.Warning( $"{count} vertices of {stride} bytes is not a buffer their codec writes" );
			return null;
		}

		if ( data.Length < 1 || ( data[0] & 0xf0 ) != VertexHeader )
		{
			log.Warning( "a vertex buffer that says it is compressed does not start like one" );
			return null;
		}

		var version = data[0] & 0x0f;

		if ( version > 1 )
		{
			log.Warning( $"vertex encoding {version} is newer than this reads" );
			return null;
		}

		data = data[1..];

		//The last vertex of the buffer is written at the end, because the first block is delta coded against it.
		//The second encoding puts a control byte per four of its bytes after it, saying how that channel was coded.
		var tail = stride + ( version == 0 ? 0 : stride / 4 );
		var padded = Math.Max( tail, version == 0 ? 32 : 24 );

		if ( data.Length < padded )
		{
			log.Warning( "a compressed vertex buffer is too short to hold its own tail" );
			return null;
		}

		var vertices = new byte[count * stride];

		Span<byte> last = stackalloc byte[stride];
		data.Slice( data.Length - tail, stride ).CopyTo( last );

		var channels = version == 0
			? ReadOnlySpan<byte>.Empty
			: data.Slice( data.Length - tail + stride, stride / 4 );

		//Blocks are sized to fit the scratch buffer, rounded down to whole groups, and never longer than the delta coding's reach.
		var blockMax = Math.Min( ( BlockSizeBytes / stride ) & ~( GroupSize - 1 ), BlockMaxVertices );

		if ( blockMax <= 0 ) return null;

		var offset = 0;

		while ( offset < count )
		{
			var block = Math.Min( blockMax, count - offset );

			if ( !DecodeVertexBlock( ref data, vertices.AsSpan( offset * stride ), block, stride, last, channels, version ) )
			{
				log.Warning( "a compressed vertex buffer ran out partway through" );
				return null;
			}

			offset += block;
		}

		//What is left should be exactly the tail the first block read from.
		if ( data.Length != padded )
		{
			log.Warning( "a compressed vertex buffer did not end where its tail begins" );
			return null;
		}

		return vertices;
	}

	//One block: every channel of the vertex read out of its own groups, then undone from deltas back into vertices.
	private static bool DecodeVertexBlock( ref ReadOnlySpan<byte> data, scoped Span<byte> into,
		int count, int stride, scoped Span<byte> last, ReadOnlySpan<byte> channels, int version )
	{
		if ( count <= 0 || count > BlockMaxVertices ) return false;

		//Four channels at a time, and the block as it comes out - a vertex stride apart in the encoding, side by side once written.
		Span<byte> buffer = stackalloc byte[BlockMaxVertices * 4];
		Span<byte> transposed = stackalloc byte[BlockSizeBytes];

		var aligned = ( count + GroupSize - 1 ) & ~( GroupSize - 1 );
		var controlSize = version == 0 ? 0 : stride / 4;

		if ( data.Length < controlSize ) return false;

		var control = data[..controlSize];
		data = data[controlSize..];

		for ( var k = 0; k < stride; k += 4 )
		{
			var controlByte = version == 0 ? 0 : control[k / 4];

			for ( var j = 0; j < 4; j++ )
			{
				var kind = ( controlByte >> ( j * 2 ) ) & 3;
				var slot = buffer[( j * count )..];

				switch ( kind )
				{
					//Written as they are, where packing them would have cost more than it saved.
					case 3:
						if ( data.Length < count ) return false;

						data[..count].CopyTo( slot );
						data = data[count..];
						break;

					//Nothing changed down this byte in the whole block.
					case 2:
						slot[..count].Clear();
						break;

					default:
						if ( slot.Length < aligned ) return false;
						if ( !DecodeBytes( ref data, slot[..aligned], version == 0 ? WidthsV0 : WidthsV1.AsSpan( kind ) ) ) return false;
						break;
				}
			}

			//How the four bytes were coded: one at a time, as pairs, or as a whole word rotated and exclusive
			//ored - which is what a float channel gets, since its low bits are noise and its high ones are not.
			var channel = version == 0 ? 0 : channels[k / 4];

			switch ( channel & 3 )
			{
				case 0: DecodeDeltas( 1, buffer, transposed[k..], count, stride, last[k..], 0 ); break;
				case 1: DecodeDeltas( 2, buffer, transposed[k..], count, stride, last[k..], 0 ); break;
				case 2: DecodeDeltas( 4, buffer, transposed[k..], count, stride, last[k..], ( 32 - ( channel >> 4 ) ) & 31 ); break;
				default: return false;
			}
		}

		transposed[..( count * stride )].CopyTo( into );

		//The next block picks up where this one left off.
		transposed.Slice( stride * ( count - 1 ), stride ).CopyTo( last );

		return true;
	}

	//Turns the deltas of four channels back into bytes, in whatever width they were coded at: one byte, two, or the four as one word.
	private static void DecodeDeltas( int size, ReadOnlySpan<byte> buffer, Span<byte> transposed,
		int count, int stride, ReadOnlySpan<byte> last, int rotation )
	{
		for ( var k = 0; k < 4; k += size )
		{
			var at = k;

			//Where the channel was when the last block ended.
			uint previous = last[0];

			for ( var j = 1; j < size; j++ )
			{
				previous |= (uint)( last[j] << ( 8 * j ) );
			}

			for ( var i = 0; i < count; i++ )
			{
				uint value = buffer[i];

				for ( var j = 1; j < size; j++ )
				{
					value |= (uint)( buffer[i + count * j] << ( 8 * j ) );
				}

				value = size switch
				{
					1 => (byte)( Unzigzag8( (byte)value ) + previous ),
					2 => (ushort)( Unzigzag16( (ushort)value ) + previous ),
					_ => Rotate32( value, rotation ) ^ previous,
				};

				for ( var j = 0; j < size; j++ )
				{
					transposed[at + j] = (byte)( value >> ( j * 8 ) );
				}

				previous = value;
				at += stride;
			}

			buffer = buffer[( count * size )..];
			last = last[size..];
		}
	}

	//Reads however many values were asked for, a group of sixteen at a time, each group as wide as the header says.
	private static bool DecodeBytes( ref ReadOnlySpan<byte> data, scoped Span<byte> into, ReadOnlySpan<int> widths )
	{
		if ( into.Length % GroupSize != 0 ) return false;

		//Two bits a group, four groups to a header byte.
		var headerSize = ( ( into.Length / GroupSize ) + 3 ) / 4;
		if ( data.Length < headerSize ) return false;

		var header = data[..headerSize];
		data = data[headerSize..];

		for ( var i = 0; i < into.Length; i += GroupSize )
		{
			if ( data.Length < GroupDecodeLimit ) return false;

			var group = i / GroupSize;
			var code = ( header[group / 4] >> ( ( group % 4 ) * 2 ) ) & 3;

			if ( code >= widths.Length ) return false;
			if ( !DecodeGroup( ref data, into[i..], widths[code] ) ) return false;
		}

		return true;
	}

	//Sixteen values, at the width this group was packed to.
	private static bool DecodeGroup( ref ReadOnlySpan<byte> data, scoped Span<byte> into, int width )
	{
		switch ( width )
		{
			//Nothing changed anywhere in the group.
			case 0:
				into[..GroupSize].Clear();
				return true;

			case 1:
			case 2:
			case 4:
				return DecodePacked( ref data, into, width );

			//A byte a value, which is where packing stops paying.
			case 8:
				if ( data.Length < GroupSize ) return false;

				data[..GroupSize].CopyTo( into );
				data = data[GroupSize..];
				return true;

			default:
				return false;
		}
	}

	//The packed groups: values from the top of each byte down, and the widest value a width can
	//hold meaning "this one did not fit", with the byte for it taken from after the packed run.
	private static bool DecodePacked( ref ReadOnlySpan<byte> data, scoped Span<byte> into, int width )
	{
		var packed = GroupSize * width / 8;
		if ( data.Length < packed ) return false;

		var escape = (byte)( ( 1 << width ) - 1 );
		var perByte = 8 / width;
		var overflow = packed;
		var at = 0;

		for ( var i = 0; i < packed; i++ )
		{
			int bits = data[i];

			//One bit values are packed the other way round from the rest.
			if ( width == 1 ) bits = Reverse( (byte)bits );

			for ( var j = 0; j < perByte; j++ )
			{
				var value = ( bits >> ( 8 - width ) ) & escape;
				bits = ( bits << width ) & 0xff;

				if ( value == escape )
				{
					if ( overflow >= data.Length ) return false;

					into[at++] = data[overflow++];
				}
				else
				{
					into[at++] = (byte)value;
				}
			}
		}

		data = data[overflow..];

		return true;
	}

	//A byte with its bits the other way round.
	private static byte Reverse( byte value )
		=> (byte)( ( ( ( value * 0x80200802UL ) & 0x0884422110UL ) * 0x0101010101UL ) >> 32 );

	//Deltas are stored with the sign in the low bit, so that a small step either way stays a small number.
	private static byte Unzigzag8( byte value ) => (byte)( ( 0 - ( value & 1 ) ) ^ ( value >> 1 ) );

	private static ushort Unzigzag16( ushort value ) => (ushort)( ( 0 - ( value & 1 ) ) ^ ( value >> 1 ) );

	private static uint Rotate32( uint value, int by ) => ( value << by ) | ( value >> ( ( 32 - by ) & 31 ) );
}
