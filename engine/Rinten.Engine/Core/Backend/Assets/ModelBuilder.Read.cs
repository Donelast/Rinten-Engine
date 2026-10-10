using System;
using System.Collections.Generic;

namespace Rinten.CoreAssets;

//Reading geometry back during a build so compile steps can derive shapes from it
internal sealed unsafe partial class ModelBuilder
{
	/// <summary>How many vertices have been added so far.</summary>
	public int VertexCount => vertices.Count;

	/// <summary>One of them, or a default vertex for an index nobody added.</summary>
	public Vertex VertexAt( int index )
		=> index >= 0 && index < vertices.Count ? vertices[index] : default;

	//The same shape as the pointer overload beside it, and it goes through it: the placement rules a delta obeys - turned with its run, not moved with it - live in one place rather than two.
	/// <summary>Add a shape from managed lists, for a caller that worked one out rather than reading it off a file.</summary>
	public int AddMorph( string morphName, List<int> moved,
		List<Vector3> positions, List<Vector3> normals )
	{
		if ( morphName is null || moved is null || positions is null ) return -1;

		var count = moved.Count;

		if ( count <= 0 || positions.Count < count ) return -1;
		if ( normals is not null && normals.Count < count ) return -1;

		var flatPositions = new float[count * 3];
		var flatNormals = new float[count * 3];

		for ( var i = 0; i < count; i++ )
		{
			flatPositions[i * 3] = positions[i].x;
			flatPositions[( i * 3 ) + 1] = positions[i].y;
			flatPositions[( i * 3 ) + 2] = positions[i].z;

			if ( normals is null ) continue;

			flatNormals[i * 3] = normals[i].x;
			flatNormals[( i * 3 ) + 1] = normals[i].y;
			flatNormals[( i * 3 ) + 2] = normals[i].z;
		}

		var indices = moved.ToArray();

		fixed ( int* v = indices )
		fixed ( float* p = flatPositions, n = flatNormals )
		{
			return AddMorph( morphName, v, p, n, count );
		}
	}
}
