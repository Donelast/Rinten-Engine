namespace Rinten.Meshlets;

//Vertex remap by a caller-chosen key, ported from meshoptimizer's generateVertexRemap
internal static class MeshWeld
{
	private struct ByteHasher : MeshHash.IIndexHasher
	{
		public byte[] Keys;
		public int Size;

		public uint Hash( uint index ) => MeshHash.Words( new ReadOnlySpan<byte>( Keys, (int)index * Size, Size ) );

		public bool Equal( uint a, uint b )
			=> new ReadOnlySpan<byte>( Keys, (int)a * Size, Size )
				.SequenceEqual( new ReadOnlySpan<byte>( Keys, (int)b * Size, Size ) );
	}

	private struct PositionHasher : MeshHash.IIndexHasher
	{
		public Vector3[] Positions;

		public uint Hash( uint index ) => MeshHash.Position( Positions[index] );

		public bool Equal( uint a, uint b )
		{
			ref readonly var l = ref Positions[a];
			ref readonly var r = ref Positions[b];

			return l.x == r.x && l.y == r.y && l.z == r.z;
		}
	}

	//The keys are that many bytes per vertex and are compared whole: a vertex is the same as another when every byte of it is.
	/// <summary>For every vertex the indices name, the compact index it maps to; <see cref="MeshHash.Empty"/> for one they do not. Returns how many distinct vertices there are.</summary>
	public static int Remap( Span<uint> remap, ReadOnlySpan<uint> indices, byte[] keys, int vertexCount, int keySize )
	{
		var hasher = new ByteHasher { Keys = keys, Size = keySize };
		return Remap( remap, indices, vertexCount, in hasher );
	}

	/// <summary>The same, with two vertices the same when they stand at one point.</summary>
	public static int RemapPositions( Span<uint> remap, ReadOnlySpan<uint> indices, Vector3[] positions )
	{
		var hasher = new PositionHasher { Positions = positions };
		return Remap( remap, indices, positions.Length, in hasher );
	}

	/// <summary>The same, with whatever says two vertices are one.</summary>
	public static int Remap<THasher>( Span<uint> remap, ReadOnlySpan<uint> indices, int vertexCount, in THasher hasher )
		where THasher : struct, MeshHash.IIndexHasher
	{
		remap[..vertexCount].Fill( MeshHash.Empty );

		var table = new uint[MeshHash.Buckets( vertexCount )];
		table.AsSpan().Fill( MeshHash.Empty );

		var next = 0u;

		foreach ( var index in indices )
		{
			if ( remap[(int)index] != MeshHash.Empty ) continue;

			ref var entry = ref MeshHash.Lookup( table, in hasher, index );

			if ( entry == MeshHash.Empty )
			{
				entry = index;
				remap[(int)index] = next++;
			}
			else
			{
				remap[(int)index] = remap[(int)entry];
			}
		}

		return (int)next;
	}

	/// <summary>Every vertex the remap kept, at the index it was given. The destination must be as long as the count <see cref="Remap"/> returned.</summary>
	public static void Apply<T>( Span<T> destination, ReadOnlySpan<T> vertices, ReadOnlySpan<uint> remap ) where T : unmanaged
	{
		for ( var i = 0; i < vertices.Length; ++i )
		{
			var to = remap[i];
			if ( to != MeshHash.Empty ) destination[(int)to] = vertices[i];
		}
	}

	/// <summary>The same, over raw bytes of a given size a vertex.</summary>
	public static void Apply( Span<byte> destination, ReadOnlySpan<byte> vertices, int vertexSize, ReadOnlySpan<uint> remap )
	{
		for ( var i = 0; i < remap.Length; ++i )
		{
			var to = remap[i];
			if ( to == MeshHash.Empty ) continue;

			vertices.Slice( i * vertexSize, vertexSize ).CopyTo( destination.Slice( (int)to * vertexSize, vertexSize ) );
		}
	}
}
