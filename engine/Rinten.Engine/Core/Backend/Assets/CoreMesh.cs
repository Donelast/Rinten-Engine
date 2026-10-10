namespace Rinten;

//A mesh as vertices and indices in memory, not GPU buffers; ModelBuilder turns it into a model
internal sealed class CoreMesh
{
	//What it is called, for the mesh entry in the model.
	internal string Name;

	//What it is drawn with. May be null.
	internal Material Material;

	//The vertices, as they were written.
	internal byte[] Vertices;

	//How many of them, and how far apart.
	internal int VertexCount;
	internal int VertexStride;

	//What each vertex holds. Null where the mesh was written with the engine's own
	//Vertex, which is the common case and the one that crosses to the core as a copy.
	internal VertexAttribute[] Layout;

	//The indices, or null where the mesh draws its vertices in order.
	internal int[] Indices;

	//The blend shapes it was given, by name, or null where it has none. The vertices they name are this mesh's own, numbered from its first.
	internal Dictionary<string, MorphDelta[]> MorphTargets;

	//How much of it to draw, where a range was set.
	internal (int Start, int Count) VertexRange;
	internal (int Start, int Count) IndexRange;

	//How big it is. Set by whoever built it.
	internal BBox Bounds;

	//Ranges of the index buffer drawn with a material of their own, where the mesh was given any.
	internal readonly List<SubMesh> SubMeshes = new();

	internal record struct SubMesh( Material Material, int StartIndex, int IndexCount, int StartVertex, int VertexCount );

	//Whether the vertices match the engine Vertex layout and can be copied as is
	internal bool IsEngineVertex
		=> VertexStride == System.Runtime.InteropServices.Marshal.SizeOf<Vertex>() && SameAsEngineLayout();

	//Whether the vertex layout includes a position, the one attribute a mesh cannot be read without
	internal bool HasPositions
	{
		get
		{
			if ( Layout is null ) return true;

			foreach ( var attribute in Layout )
			{
				if ( attribute.Type == VertexAttributeType.Position ) return true;
			}

			return false;
		}
	}

	private bool SameAsEngineLayout()
	{
		if ( Layout is null ) return true;
		if ( Layout.Length != Vertex.Layout.Length ) return false;

		for ( var i = 0; i < Layout.Length; i++ )
		{
			if ( Layout[i].Type != Vertex.Layout[i].Type ) return false;
			if ( Layout[i].Format != Vertex.Layout[i].Format ) return false;
			if ( Layout[i].Components != Vertex.Layout[i].Components ) return false;
		}

		return true;
	}

	//Vertices converted to engine Vertex via their layout; attributes with no slot are dropped
	internal Vertex[] AsEngineVertices()
	{
		if ( Vertices is null || VertexCount <= 0 || VertexStride <= 0 ) return System.Array.Empty<Vertex>();

		var vertices = new Vertex[VertexCount];

		if ( IsEngineVertex )
		{
			System.Runtime.InteropServices.MemoryMarshal
				.Cast<byte, Vertex>( Vertices.AsSpan( 0, VertexCount * VertexStride ) )
				.CopyTo( vertices );

			return vertices;
		}

		//Every vertex starts white and facing up: a mesh that says nothing about a channel
		//gets something drawable rather than zeroes, which would come out black and unlit.
		for ( var i = 0; i < vertices.Length; i++ )
		{
			vertices[i].Color = Color32.White;
			vertices[i].Normal = Vector3.Up;
		}

		if ( Layout is null ) return vertices;

		var offset = 0;

		foreach ( var attribute in Layout )
		{
			var size = Size( attribute );

			//A format we cannot read leaves the rest of the vertex where it is: the offsets after it would be guesses.
			if ( size == 0 ) break;

			Read( attribute, offset, vertices );
			offset += size;
		}

		return vertices;
	}

	//How many bytes an attribute takes, or zero for one we cannot read.
	private static int Size( VertexAttribute attribute ) => attribute.Format switch
	{
		VertexAttributeFormat.Float32 or VertexAttributeFormat.SInt32 or VertexAttributeFormat.UInt32
			=> 4 * attribute.Components,

		VertexAttributeFormat.Float16 or VertexAttributeFormat.SInt16 or VertexAttributeFormat.UInt16
			=> 2 * attribute.Components,

		VertexAttributeFormat.SInt8 or VertexAttributeFormat.UInt8
			=> attribute.Components,

		_ => 0,
	};

	//Reads one attribute out of every vertex, into the engine's own.
	private void Read( VertexAttribute attribute, int offset, Vertex[] into )
	{
		var bytes = Vertices.AsSpan();

		for ( var i = 0; i < into.Length; i++ )
		{
			var at = (i * VertexStride) + offset;
			if ( at + Size( attribute ) > bytes.Length ) return;

			var pixel = bytes[at..];

			switch ( attribute.Type )
			{
				case VertexAttributeType.Position:
					into[i].Position = Floats3( pixel, attribute );
					break;

				case VertexAttributeType.Normal:
					into[i].Normal = Floats3( pixel, attribute );
					break;

				case VertexAttributeType.Tangent:
					into[i].Tangent = Floats4( pixel, attribute );
					break;

				case VertexAttributeType.TexCoord:
					if ( attribute.SemanticIndex == 0 ) into[i].TexCoord0 = Floats4( pixel, attribute );
					else if ( attribute.SemanticIndex == 1 ) into[i].TexCoord1 = Floats4( pixel, attribute );
					break;

				case VertexAttributeType.Color:
					into[i].Color = Colour( pixel, attribute );
					break;
			}
		}
	}

	private static Vector3 Floats3( System.ReadOnlySpan<byte> from, VertexAttribute attribute )
	{
		var value = Floats4( from, attribute );
		return new Vector3( value.x, value.y, value.z );
	}

	private static Vector4 Floats4( System.ReadOnlySpan<byte> from, VertexAttribute attribute )
	{
		var value = Vector4.Zero;

		for ( var c = 0; c < attribute.Components && c < 4; c++ )
		{
			var f = attribute.Format switch
			{
				VertexAttributeFormat.Float32 => System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian( from[(c * 4)..] ),
				VertexAttributeFormat.Float16 => (float)System.BitConverter.UInt16BitsToHalf(
					System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( from[(c * 2)..] ) ),
				VertexAttributeFormat.UInt8 => from[c] / 255.0f,
				VertexAttributeFormat.SInt8 => (sbyte)from[c] / 127.0f,
				VertexAttributeFormat.UInt16 => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( from[(c * 2)..] ),
				VertexAttributeFormat.SInt16 => System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian( from[(c * 2)..] ),
				VertexAttributeFormat.UInt32 => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( from[(c * 4)..] ),
				VertexAttributeFormat.SInt32 => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian( from[(c * 4)..] ),
				_ => 0.0f,
			};

			value[c] = f;
		}

		return value;
	}

	private static Color32 Colour( System.ReadOnlySpan<byte> from, VertexAttribute attribute )
	{
		if ( attribute.Format == VertexAttributeFormat.UInt8 && attribute.Components >= 4 )
			return new Color32( from[0], from[1], from[2], from[3] );

		var value = Floats4( from, attribute );
		return new Color( value.x, value.y, value.z, attribute.Components >= 4 ? value.w : 1.0f );
	}
}
