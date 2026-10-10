namespace Rinten.Meshlets;

//Shared open-addressing hash of indices; the hasher is a struct generic so lookups inline
internal static class MeshHash
{
	public const uint Empty = uint.MaxValue;

	/// <summary>How to hash and compare two indices into whatever the caller holds.</summary>
	public interface IIndexHasher
	{
		uint Hash( uint index );
		bool Equal( uint a, uint b );
	}

	/// <summary>A power of two with a quarter of slack, which keeps the probe short.</summary>
	public static int Buckets( int count )
	{
		var buckets = 1;
		while ( buckets < count + ( count / 4 ) ) buckets *= 2;

		return buckets;
	}

	/// <summary>The slot for <paramref name="key"/>: the one holding an index equal to it, or the first empty one on its probe path.</summary>
	public static ref uint Lookup<THasher>( Span<uint> table, in THasher hasher, uint key )
		where THasher : struct, IIndexHasher
	{
		var mask = table.Length - 1;
		var bucket = (int)( hasher.Hash( key ) & (uint)mask );

		for ( var probe = 0; probe <= mask; ++probe )
		{
			ref var item = ref table[bucket];

			if ( item == Empty ) return ref item;
			if ( hasher.Equal( item, key ) ) return ref item;

			bucket = ( bucket + probe + 1 ) & mask;
		}

		throw new InvalidOperationException( "mesh hash table is full" );
	}

	/// <summary>MurmurHash2 over whole words; the tail bytes, if any, are ignored, as the original does.</summary>
	public static uint Words( ReadOnlySpan<byte> key )
	{
		const uint m = 0x5bd1e995;
		const int r = 24;

		uint h = 0;

		while ( key.Length >= 4 )
		{
			var k = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( key );

			k *= m;
			k ^= k >> r;
			k *= m;

			h *= m;
			h ^= k;

			key = key[4..];
		}

		return h;
	}

	/// <summary>Optimized Spatial Hashing for Collision Detection of Deformable Objects (Teschner et al. 2003), over the bits of a position, with negative zero folded into zero so the two hash alike as they compare alike.</summary>
	public static uint Position( in Vector3 p )
	{
		var x = BitConverter.SingleToUInt32Bits( p.x );
		var y = BitConverter.SingleToUInt32Bits( p.y );
		var z = BitConverter.SingleToUInt32Bits( p.z );

		x = x == 0x80000000 ? 0 : x;
		y = y == 0x80000000 ? 0 : y;
		z = z == 0x80000000 ? 0 : z;

		x ^= x >> 17;
		y ^= y >> 17;
		z ^= z >> 17;

		return ( x * 73856093 ) ^ ( y * 19349663 ) ^ ( z * 83492791 );
	}
}
