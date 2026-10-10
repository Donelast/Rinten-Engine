using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Rinten.Porting.SBox;

//Compiled mesh geometry: vertex/index buffer descriptors, often meshoptimizer-compressed
internal sealed class CoreImportedBuffers
{
	private static readonly Logger log = new( "Core" );

	//What one part of a vertex is, and where in it.
	internal readonly record struct Attribute( string Semantic, int Index, int Format, int Offset );

	//Vertices as bytes, with enough to read them back.
	internal sealed class VertexBuffer
	{
		public int Count;
		public int Stride;
		public Attribute[] Attributes = System.Array.Empty<Attribute>();
		public byte[] Data;

		/// <summary>The named part of a vertex, where the buffer has one.</summary>
		public Attribute? Find( string semantic, int index = 0 )
		{
			foreach ( var attribute in Attributes )
			{
				if ( attribute.Semantic == semantic && attribute.Index == index ) return attribute;
			}

			return null;
		}
	}

	internal sealed class IndexBuffer
	{
		public int[] Values = System.Array.Empty<int>();
	}

	public List<VertexBuffer> Vertices { get; } = new();
	public List<IndexBuffer> Indices { get; } = new();

	private CoreImportedBuffers() { }

	/// <summary>Reads a geometry block, or returns null where it is not one.</summary>
	public static CoreImportedBuffers Read( ReadOnlySpan<byte> block )
	{
		if ( block.Length < 16 ) return null;

		var vertexOffset = Int( block, 0 );
		var vertexCount = Int( block, 4 );
		var indexOffset = Int( block, 8 );
		var indexCount = Int( block, 12 );

		//A mesh with hundreds of buffers is a block being read as something it is not, rather than a mesh.
		if ( vertexCount is < 0 or > 64 || indexCount is < 0 or > 64 ) return null;

		var buffers = new CoreImportedBuffers();

		for ( var i = 0; i < vertexCount; i++ )
		{
			var buffer = ReadVertexBuffer( block, vertexOffset + i * 24 );
			if ( buffer is null ) return null;

			buffers.Vertices.Add( buffer );
		}

		for ( var i = 0; i < indexCount; i++ )
		{
			var buffer = ReadIndexBuffer( block, 8 + indexOffset + i * 24 );
			if ( buffer is null ) return null;

			buffers.Indices.Add( buffer );
		}

		return buffers;
	}

	private static VertexBuffer ReadVertexBuffer( ReadOnlySpan<byte> block, int at )
	{
		if ( at < 0 || at + 24 > block.Length ) return null;

		var count = Int( block, at );

		//The high bits of the size carry flags - one of them says the buffer went through zstd before meshoptimizer, which nothing we read does.
		var size = Int( block, at + 4 );
		var stride = size & 0x3FFFFFF;

		if ( ( size & 0x8000000 ) != 0 )
		{
			log.Warning( "a vertex buffer compressed with zstd is not read" );
			return null;
		}

		var attributes = ReadAttributes( block, at + 8 );
		if ( attributes is null ) return null;

		var data = ReadData( block, at + 16, count, stride, vertices: true );
		if ( data is null ) return null;

		return new VertexBuffer
		{
			Count = count,
			Stride = stride,
			Attributes = attributes,
			Data = data,
		};
	}

	private static IndexBuffer ReadIndexBuffer( ReadOnlySpan<byte> block, int at )
	{
		if ( at < 0 || at + 24 > block.Length ) return null;

		var count = Int( block, at );
		var size = Int( block, at + 4 ) & 0x3FFFFFF;

		if ( size is not (2 or 4) )
		{
			log.Warning( $"an index of {size} bytes is not one of theirs" );
			return null;
		}

		var data = ReadData( block, at + 16, count, size, vertices: false );
		if ( data is null ) return null;

		//Two byte indices are the common case, and everything above here works in whole ones.
		var values = new int[count];

		for ( var i = 0; i < count; i++ )
		{
			values[i] = size == 2
				? BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( i * 2 ) )
				: BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( i * 4 ) );
		}

		return new IndexBuffer { Values = values };
	}

	//The bytes a descriptor points at, unpacked where the compiler packed them.
	private static byte[] ReadData( ReadOnlySpan<byte> block, int at, int count, int stride, bool vertices )
	{
		if ( count < 0 || stride <= 0 ) return null;

		var offset = at + Int( block, at );
		var written = Int( block, at + 4 );

		if ( offset < 0 || written < 0 || offset + written > block.Length ) return null;

		var whole = count * stride;
		var data = block.Slice( offset, written );

		//Written as they are, where the codec would not have helped.
		if ( written >= whole )
		{
			return data[..whole].ToArray();
		}

		if ( vertices ) return CoreMeshOptimizer.DecodeVertices( count, stride, data );

		var indices = CoreMeshOptimizer.DecodeIndices( count, stride, data );
		if ( indices is null ) return null;

		//Back into bytes, so both paths hand back the same thing.
		var bytes = new byte[whole];

		for ( var i = 0; i < count; i++ )
		{
			if ( stride == 2 ) BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( i * 2 ), (ushort)indices[i] );
			else BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( i * 4 ), indices[i] );
		}

		return bytes;
	}

	private static Attribute[] ReadAttributes( ReadOnlySpan<byte> block, int at )
	{
		var offset = at + Int( block, at );
		var count = Int( block, at + 4 );

		if ( count is < 0 or > 32 ) return null;
		if ( offset < 0 || offset + count * 56 > block.Length ) return null;

		var attributes = new Attribute[count];

		for ( var i = 0; i < count; i++ )
		{
			var field = offset + i * 56;
			var name = block.Slice( field, 32 );
			var end = name.IndexOf( (byte)0 );

			attributes[i] = new Attribute(
				Encoding.UTF8.GetString( end < 0 ? name : name[..end] ).ToUpperInvariant(),
				Int( block, field + 32 ),
				Int( block, field + 36 ),
				Int( block, field + 40 ) );
		}

		return attributes;
	}

	private static int Int( ReadOnlySpan<byte> from, int at )
		=> at < 0 || at + 4 > from.Length ? 0 : BinaryPrimitives.ReadInt32LittleEndian( from[at..] );
}
