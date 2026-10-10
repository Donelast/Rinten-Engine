using System.Collections.Generic;

namespace Rinten;

internal sealed partial class CoreModel
{
	//Visible geometry (LOD 0, default body groups) as positions and one absolute triangle list
	internal unsafe bool CopyTriangles( out Vector3[] positions, out int[] indices )
	{
		positions = null;
		indices = null;

		if ( !IsValid ) return false;

		var vertexCount = handle.VertexCount;
		var indexCount = handle.IndexCount;

		if ( vertexCount <= 0 || indexCount <= 0 ) return false;

		var vertices = new Vertex[vertexCount];

		fixed ( Vertex* v = vertices )
		{
			if ( handle.CopyVertices( v, vertexCount ) <= 0 ) return false;
		}

		var raw = new uint[indexCount];

		fixed ( uint* i = raw )
		{
			if ( handle.CopyIndices( i, indexCount ) <= 0 ) return false;
		}

		var points = new Vector3[vertexCount];

		for ( var i = 0; i < vertexCount; i++ )
			points[i] = vertices[i].Position;

		var mask = DefaultMask;
		var meshes = MeshCount;

		var triangles = new List<int>( indexCount );

		for ( var mesh = 0; mesh < meshes; mesh++ )
		{

			//The same for body groups: a mesh nobody claims is always drawn.
			var group = MeshGroup( mesh );
			if ( group != 0 && ( group & mask ) == 0 ) continue;

			var range = MeshRange( mesh );

			var first = range.FirstIndex;
			var count = range.IndexCount;
			var baseVertex = range.FirstVertex;

			if ( first < 0 || count <= 0 || first + count > indexCount ) continue;

			for ( var i = 0; i + 2 < count; i += 3 )
			{
				var a = baseVertex + (int)raw[first + i];
				var b = baseVertex + (int)raw[first + i + 1];
				var c = baseVertex + (int)raw[first + i + 2];

				if ( (uint)a >= (uint)vertexCount ) continue;
				if ( (uint)b >= (uint)vertexCount ) continue;
				if ( (uint)c >= (uint)vertexCount ) continue;

				triangles.Add( a );
				triangles.Add( b );
				triangles.Add( c );
			}
		}

		if ( triangles.Count == 0 ) return false;

		positions = points;
		indices = triangles.ToArray();

		return true;
	}

	//Which bone owns each vertex, for the models that have a skeleton. The heaviest of the four a vertex names, which is all a bounding volume needs:
	//a vertex shared between two bones sits in the box of whichever one moves it most, and the box is only ever asked whether a ray came near.
	internal unsafe int[] CopyTraceBoneOwners( int vertexCount )
	{
		if ( !IsValid || vertexCount <= 0 ) return null;

		var count = handle.SkinCount;
		if ( count < vertexCount ) return null;

		var skin = new byte[vertexCount * 8];

		fixed ( byte* s = skin )
		{
			if ( handle.CopySkin( s, vertexCount ) <= 0 ) return null;
		}

		var owners = new int[vertexCount];

		for ( var i = 0; i < vertexCount; i++ )
		{
			var at = i * 8;

			var best = 0;
			var heaviest = skin[at + 4];

			for ( var j = 1; j < 4; j++ )
			{
				if ( skin[at + 4 + j] <= heaviest ) continue;

				heaviest = skin[at + 4 + j];
				best = j;
			}

			owners[i] = skin[at + best];
		}

		return owners;
	}

	//Model-to-bone at rest, one row-major matrix per bone laid end to end. The same matrices the shader skins with, so a vertex put through
	//one of these and then through wherever that bone stands now lands exactly where it is drawn. Row vectors, translation in the last row.
	internal unsafe float[] CopyInverseBinds( int bones )
	{
		if ( !IsValid || bones <= 0 ) return null;

		var values = new float[bones * 16];

		fixed ( float* v = values )
		{
			for ( var bone = 0; bone < bones; bone++ )
				handle.BoneInverseBind( bone, v + ( bone * 16 ) );
		}

		return values;
	}
}
