namespace Rinten;

//Index decoder for meshoptimizer's codec: edge and vertex rings, must match the encoder exactly
internal static partial class CoreMeshOptimizer
{
	//The top nibble of a compressed index buffer's first byte.
	private const byte IndexHeader = 0xe0;

	/// <summary>Unpacks an index buffer into indices, or null where this is not something it can read.</summary>
	public static int[] DecodeIndices( int count, int size, ReadOnlySpan<byte> data )
	{
		if ( count <= 0 || count % 3 != 0 )
		{
			log.Warning( $"{count} indices is not whole triangles" );
			return null;
		}

		if ( size != 2 && size != 4 )
		{
			log.Warning( $"an index of {size} bytes is not one of theirs" );
			return null;
		}

		//A byte a triangle after the header, then the data, then the sixteen byte table of the common cases.
		var dataOffset = 1 + ( count / 3 );

		if ( data.Length < dataOffset + 16 )
		{
			log.Warning( "a compressed index buffer is too short for the triangles it claims" );
			return null;
		}

		if ( ( data[0] & 0xf0 ) != IndexHeader )
		{
			log.Warning( "an index buffer that says it is compressed does not start like one" );
			return null;
		}

		var version = data[0] & 0x0f;

		if ( version > 1 )
		{
			log.Warning( $"index encoding {version} is newer than this reads" );
			return null;
		}

		Span<uint> vertexFifo = stackalloc uint[16];
		Span<uint> edgeFifoA = stackalloc uint[16];
		Span<uint> edgeFifoB = stackalloc uint[16];

		var vertexAt = 0;
		var edgeAt = 0;

		//The next vertex nobody has named yet, and the last one that was spelled out - free indices are written as steps from it.
		var next = 0u;
		var last = 0u;

		//The second encoding took two of the sixteen codes for the two step case, so it has two fewer ring slots to name.
		var fifoMax = version >= 1 ? 13 : 15;

		var codes = data[1..dataOffset];
		var table = data[^16..];
		var body = data[dataOffset..^16];

		var indices = new int[count];
		var at = 0;

		for ( var i = 0; i < count; i += 3 )
		{
			var code = codes[i / 3];

			if ( code < 0xf0 )
			{
				//An edge from the ring, and one vertex to go with it.
				var edge = ( edgeAt - 1 - ( code >> 4 ) ) & 15;

				var a = edgeFifoA[edge];
				var b = edgeFifoB[edge];

				var third = code & 15;
				uint c;

				if ( third < fifoMax )
				{
					var seen = third == 0;

					c = seen ? next : vertexFifo[( vertexAt - 1 - third ) & 15];

					if ( seen ) next++;

					PushVertex( vertexFifo, ref vertexAt, c, seen );
				}
				else
				{
					//Thirteen and fourteen mean one step back or forward from the last named vertex; fifteen means it is written out.
					if ( third != 15 )
					{
						c = last = (uint)( last + ( third - ( third ^ 3 ) ) );
					}
					else
					{
						c = last = ReadIndex( body, ref at, last );
					}

					PushVertex( vertexFifo, ref vertexAt, c, true );
				}

				PushEdge( edgeFifoA, edgeFifoB, ref edgeAt, c, b );
				PushEdge( edgeFifoA, edgeFifoB, ref edgeAt, a, c );

				Write( indices, i, a, b, c );
			}
			else if ( code < 0xfe )
			{
				//A triangle of its own, in one of the fifteen shapes common enough that the table at the end of the buffer names them.
				var aux = table[code & 15];

				var second = aux >> 4;
				var third = aux & 15;

				var a = next++;

				var b = second == 0 ? next : vertexFifo[( vertexAt - second ) & 15];
				if ( second == 0 ) next++;

				var c = third == 0 ? next : vertexFifo[( vertexAt - third ) & 15];
				if ( third == 0 ) next++;

				Write( indices, i, a, b, c );

				PushVertex( vertexFifo, ref vertexAt, a, true );
				PushVertex( vertexFifo, ref vertexAt, b, second == 0 );
				PushVertex( vertexFifo, ref vertexAt, c, third == 0 );

				PushEdge( edgeFifoA, edgeFifoB, ref edgeAt, b, a );
				PushEdge( edgeFifoA, edgeFifoB, ref edgeAt, c, b );
				PushEdge( edgeFifoA, edgeFifoB, ref edgeAt, a, c );
			}
			else
			{
				//Nothing to reuse: the shape is written out as a byte of its own, and any vertex it does not find in the ring follows it.
				if ( at >= body.Length ) return Short();

				var aux = body[at++];

				var first = code == 0xfe ? 0 : 15;
				var second = aux >> 4;
				var third = aux & 15;

				//An all zero shape is how the encoder starts again from the beginning of the buffer.
				if ( aux == 0 ) next = 0;

				var a = first == 0 ? next++ : 0u;
				var b = second == 0 ? next++ : vertexFifo[( vertexAt - second ) & 15];
				var c = third == 0 ? next++ : vertexFifo[( vertexAt - third ) & 15];

				if ( first == 15 ) last = a = ReadIndex( body, ref at, last );
				if ( second == 15 ) last = b = ReadIndex( body, ref at, last );
				if ( third == 15 ) last = c = ReadIndex( body, ref at, last );

				Write( indices, i, a, b, c );

				PushVertex( vertexFifo, ref vertexAt, a, true );
				PushVertex( vertexFifo, ref vertexAt, b, second is 0 or 15 );
				PushVertex( vertexFifo, ref vertexAt, c, third is 0 or 15 );

				PushEdge( edgeFifoA, edgeFifoB, ref edgeAt, b, a );
				PushEdge( edgeFifoA, edgeFifoB, ref edgeAt, c, b );
				PushEdge( edgeFifoA, edgeFifoB, ref edgeAt, a, c );
			}

			if ( at > body.Length ) return Short();
		}

		//Everything between the triangle codes and the table should have been read: anything left means this was decoded as something it is not.
		if ( at != body.Length )
		{
			log.Warning( "a compressed index buffer had bytes left over" );
			return null;
		}

		return indices;

		int[] Short()
		{
			log.Warning( "a compressed index buffer ran out partway through" );
			return null;
		}
	}

	private static void Write( int[] indices, int at, uint a, uint b, uint c )
	{
		indices[at + 0] = (int)a;
		indices[at + 1] = (int)b;
		indices[at + 2] = (int)c;
	}

	//A vertex onto the ring. It is written either way; whether the ring moves on is what says it can be named again.
	private static void PushVertex( Span<uint> fifo, ref int at, uint value, bool advance )
	{
		fifo[at] = value;
		at = ( at + ( advance ? 1 : 0 ) ) & 15;
	}

	private static void PushEdge( Span<uint> a, Span<uint> b, ref int at, uint first, uint second )
	{
		a[at] = first;
		b[at] = second;
		at = ( at + 1 ) & 15;
	}

	//A vertex written out: a step from the last one, sign in the low bit.
	private static uint ReadIndex( ReadOnlySpan<byte> data, ref int at, uint last )
	{
		var value = ReadVarint( data, ref at );

		return last + ( ( value >> 1 ) ^ (uint)-(int)( value & 1 ) );
	}

	//Seven bits a byte, top bit saying there is more.
	private static uint ReadVarint( ReadOnlySpan<byte> data, ref int at )
	{
		if ( at >= data.Length ) return 0;

		var lead = (uint)data[at++];
		if ( lead < 128 ) return lead;

		var value = lead & 127;
		var shift = 7;

		for ( var i = 0; i < 4; i++ )
		{
			if ( at >= data.Length ) break;

			var group = (uint)data[at++];

			value |= ( group & 127 ) << shift;
			shift += 7;

			if ( group < 128 ) break;
		}

		return value;
	}
}
