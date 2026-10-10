using Rinten.Engine.Utility.RayTrace;
using System;

namespace Rinten;

//A model's triangles with a BVH, built on first trace, for accurate editor picking
internal sealed class ModelTraceMesh
{
	//How many triangles a leaf of the tree may hold.
	private const int LeafSize = 8;

	private readonly Vector3[] positions;
	private readonly int[] indices;

	//Triangle numbers, grouped so that a leaf is a run of them.
	private readonly int[] order;

	private readonly Node[] nodes;
	private int nodeCount;

	/// <summary>The whole thing, in the model's own space.</summary>
	public BBox Bounds { get; }

	public int TriangleCount => indices.Length / 3;

	//A branch keeps its right child; the left one is always the node after it. A leaf keeps a run of order and no children.
	private struct Node
	{
		public Vector3 Mins;
		public Vector3 Maxs;
		public int Start;
		public int Count;
		public int Right;
	}

	public ModelTraceMesh( Vector3[] positions, int[] indices )
	{
		this.positions = positions;
		this.indices = indices;

		var count = indices.Length / 3;

		order = new int[count];
		for ( var i = 0; i < count; i++ ) order[i] = i;

		nodes = new Node[Math.Max( 1, ( count * 2 ) + 1 )];

		if ( count > 0 )
		{
			var centroids = new Vector3[count];

			for ( var i = 0; i < count; i++ )
			{
				var a = positions[indices[i * 3]];
				var b = positions[indices[( i * 3 ) + 1]];
				var c = positions[indices[( i * 3 ) + 2]];

				centroids[i] = ( a + b + c ) / 3.0f;
			}

			Build( centroids, new float[count], 0, count );

			Bounds = new BBox( nodes[0].Mins, nodes[0].Maxs );
		}
		else
		{
			nodeCount = 1;
			Bounds = default;
		}
	}

	//Builds a node over a run of triangles, splitting at the middle of the longest centroid axis
	private int Build( Vector3[] centroids, float[] keys, int start, int count )
	{
		var node = nodeCount++;

		var mins = new Vector3( float.MaxValue );
		var maxs = new Vector3( float.MinValue );

		var centroidMins = new Vector3( float.MaxValue );
		var centroidMaxs = new Vector3( float.MinValue );

		for ( var i = start; i < start + count; i++ )
		{
			var triangle = order[i];

			for ( var corner = 0; corner < 3; corner++ )
			{
				var point = positions[indices[( triangle * 3 ) + corner]];

				mins = Vector3.Min( mins, point );
				maxs = Vector3.Max( maxs, point );
			}

			centroidMins = Vector3.Min( centroidMins, centroids[triangle] );
			centroidMaxs = Vector3.Max( centroidMaxs, centroids[triangle] );
		}

		nodes[node].Mins = mins;
		nodes[node].Maxs = maxs;

		if ( count <= LeafSize )
		{
			nodes[node].Start = start;
			nodes[node].Count = count;

			return node;
		}

		var extent = centroidMaxs - centroidMins;

		var axis = 0;
		if ( extent.y > extent.x ) axis = 1;
		if ( extent.z > extent[axis] ) axis = 2;

		//Everything on top of everything else: no split tells them apart, so they stay together rather than recursing forever.
		if ( extent[axis] < 1e-9f )
		{
			nodes[node].Start = start;
			nodes[node].Count = count;

			return node;
		}

		for ( var i = start; i < start + count; i++ )
		{
			keys[i] = centroids[order[i]][axis];
		}

		Array.Sort( keys, order, start, count );

		var half = count / 2;

		nodes[node].Count = 0;

		Build( centroids, keys, start, half );
		nodes[node].Right = Build( centroids, keys, start + half, count - half );

		return node;
	}

	/// <summary>Traces a segment through the tree, in the model's own space. The segment is left unnormalised, so what comes back is a fraction along it, and the cull mode is the trace's own - nothing culled, backs only, or fronts.</summary>
	public bool Trace( in Vector3 start, in Vector3 delta, int cullMode,
		out float fraction, out Vector3 normal, out int triangle )
	{
		fraction = float.MaxValue;
		normal = default;
		triangle = -1;

		if ( nodeCount == 0 || indices.Length < 3 ) return false;

		Span<int> stack = stackalloc int[64];
		var depth = 0;

		stack[depth++] = 0;

		while ( depth > 0 )
		{
			var node = stack[--depth];

			if ( !Crosses( nodes[node].Mins, nodes[node].Maxs, start, delta, fraction ) )
				continue;

			var count = nodes[node].Count;

			if ( count == 0 )
			{
				//The two children, nearest first is not worth working out here - the fraction test above throws
				//out most of what is behind a hit either way, and the stack is shallower for not sorting.
				if ( depth + 2 <= stack.Length )
				{
					stack[depth++] = nodes[node].Right;
					stack[depth++] = node + 1;
				}

				continue;
			}

			for ( var i = nodes[node].Start; i < nodes[node].Start + count; i++ )
			{
				var at = order[i];

				var a = positions[indices[at * 3]];
				var b = positions[indices[( at * 3 ) + 1]];
				var c = positions[indices[( at * 3 ) + 2]];

				if ( !MeshTraceRequest.TraceTriangle( a, b, c, start, delta, cullMode, out var t, out var face ) )
					continue;

				if ( t >= fraction ) continue;

				fraction = t;
				normal = face;
				triangle = at;
			}
		}

		return triangle >= 0;
	}

	//Whether the segment reaches a box before limit along it.
	private static bool Crosses( in Vector3 mins, in Vector3 maxs, in Vector3 start, in Vector3 delta, float limit )
	{
		var near = 0.0f;
		var far = limit > 1.0f ? 1.0f : limit;

		for ( var i = 0; i < 3; ++i )
		{
			var origin = start[i];
			var direction = delta[i];

			if ( MathF.Abs( direction ) < 1e-9f )
			{
				if ( origin < mins[i] || origin > maxs[i] ) return false;

				continue;
			}

			var inverse = 1.0f / direction;

			var first = ( mins[i] - origin ) * inverse;
			var second = ( maxs[i] - origin ) * inverse;

			if ( first > second ) (first, second) = (second, first);

			if ( first > near ) near = first;
			if ( second < far ) far = second;

			if ( near > far ) return false;
		}

		return true;
	}
}
