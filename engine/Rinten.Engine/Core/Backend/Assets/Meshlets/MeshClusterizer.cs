namespace Rinten.Meshlets;

//Splits a triangle list into meshlets, ported from meshoptimizer's buildMeshlets
internal static class MeshClusterizer
{
	/// <summary>One meshlet: where its vertices and triangles start and how many of each.</summary>
	public struct Meshlet
	{
		/// <summary>Offsets into the meshlet vertex and triangle arrays.</summary>
		public uint VertexOffset;
		public uint TriangleOffset;

		/// <summary>How many of each the meshlet uses, from those offsets on.</summary>
		public uint VertexCount;
		public uint TriangleCount;
	}

	//Meshlets are limited only by vertex and triangle counts, not cut at concave folds

	private const int MaxSeeds = 256;
	private const int AddSeeds = 4;
	private const int MaxTreeDepth = 50;

	/// <summary>Upper bound on meshlet count for sizing output: one per triangle</summary>
	public static int Bound( int indexCount, int maxVertices, int maxTriangles )
	{
		return indexCount / 3;
	}

	//Three of each a triangle, whatever the clusters come out as: a triangle puts at most three vertices into whichever cluster takes it, and exactly three corners.
	/// <summary>How many vertices and triangle corners the output of that many meshlets can hold.</summary>
	public static int BoundWords( int indexCount ) => indexCount;

	//-- adjacency: which triangles touch each vertex ------------------------

	private struct Adjacency
	{
		public uint[] Counts;
		public uint[] Offsets;
		public uint[] Data;
	}

	private static Adjacency BuildAdjacency( ReadOnlySpan<uint> indices, int vertexCount )
	{
		var faceCount = indices.Length / 3;

		var adjacency = new Adjacency
		{
			Counts = new uint[vertexCount],
			Offsets = new uint[vertexCount],
			Data = new uint[indices.Length],
		};

		foreach ( var index in indices ) adjacency.Counts[index]++;

		var offset = 0u;

		for ( var i = 0; i < vertexCount; ++i )
		{
			adjacency.Offsets[i] = offset;
			offset += adjacency.Counts[i];
		}

		for ( var i = 0; i < faceCount; ++i )
		{
			uint a = indices[( i * 3 ) + 0], b = indices[( i * 3 ) + 1], c = indices[( i * 3 ) + 2];

			adjacency.Data[adjacency.Offsets[a]++] = (uint)i;
			adjacency.Data[adjacency.Offsets[b]++] = (uint)i;
			adjacency.Data[adjacency.Offsets[c]++] = (uint)i;
		}

		for ( var i = 0; i < vertexCount; ++i )
			adjacency.Offsets[i] -= adjacency.Counts[i];

		return adjacency;
	}

	//-- what a triangle is, for scoring --------------------------------------

	private struct Cone
	{
		public float px, py, pz;
		public float nx, ny, nz;
	}

	private static float MeshletScore( float distance, float spread, float coneWeight, float expectedRadius )
	{
		var cone = 1.0f - ( spread * coneWeight );
		var coneClamped = cone < 1e-3f ? 1e-3f : cone;

		return ( 1 + ( distance / expectedRadius * ( 1 - coneWeight ) ) ) * coneClamped;
	}

	private static Cone MeshletCone( in Cone acc, uint triangleCount )
	{
		var result = acc;

		var centerScale = triangleCount == 0 ? 0.0f : 1.0f / triangleCount;

		result.px *= centerScale;
		result.py *= centerScale;
		result.pz *= centerScale;

		var axisLength = ( result.nx * result.nx ) + ( result.ny * result.ny ) + ( result.nz * result.nz );
		var axisScale = axisLength == 0.0f ? 0.0f : 1.0f / MathF.Sqrt( axisLength );

		result.nx *= axisScale;
		result.ny *= axisScale;
		result.nz *= axisScale;

		return result;
	}

	//Centroid and unit normal per triangle; returns the mesh's area, as the sum of unnormalised normals.
	private static float TriangleCones( Span<Cone> triangles, ReadOnlySpan<uint> indices, ReadOnlySpan<Vector3> positions )
	{
		var faceCount = indices.Length / 3;
		var meshArea = 0.0f;

		for ( var i = 0; i < faceCount; ++i )
		{
			ref readonly var p0 = ref positions[(int)indices[( i * 3 ) + 0]];
			ref readonly var p1 = ref positions[(int)indices[( i * 3 ) + 1]];
			ref readonly var p2 = ref positions[(int)indices[( i * 3 ) + 2]];

			float p10x = p1.x - p0.x, p10y = p1.y - p0.y, p10z = p1.z - p0.z;
			float p20x = p2.x - p0.x, p20y = p2.y - p0.y, p20z = p2.z - p0.z;

			var nx = ( p10y * p20z ) - ( p10z * p20y );
			var ny = ( p10z * p20x ) - ( p10x * p20z );
			var nz = ( p10x * p20y ) - ( p10y * p20x );

			var area = MathF.Sqrt( ( nx * nx ) + ( ny * ny ) + ( nz * nz ) );
			var invarea = area == 0.0f ? 0.0f : 1.0f / area;

			ref var t = ref triangles[i];

			t.px = ( p0.x + p1.x + p2.x ) / 3.0f;
			t.py = ( p0.y + p1.y + p2.y ) / 3.0f;
			t.pz = ( p0.z + p1.z + p2.z ) / 3.0f;

			t.nx = nx * invarea;
			t.ny = ny * invarea;
			t.nz = nz * invarea;

			meshArea += area;
		}

		return meshArea;
	}

	//-- growing a meshlet ------------------------------------------------------

	//Adds a triangle to the meshlet, emitting the meshlet first if the triangle would not fit. Returns whether one was emitted.
	private static bool Append( ref Meshlet meshlet, uint a, uint b, uint c, short[] used, Span<Meshlet> meshlets,
		Span<uint> meshletVertices, Span<byte> meshletTriangles, int meshletOffset, int maxVertices, int maxTriangles )
	{
		ref var av = ref used[a];
		ref var bv = ref used[b];
		ref var cv = ref used[c];

		var result = false;

		var usedExtra = ( av < 0 ? 1 : 0 ) + ( bv < 0 ? 1 : 0 ) + ( cv < 0 ? 1 : 0 );

		if ( meshlet.VertexCount + usedExtra > maxVertices || meshlet.TriangleCount >= maxTriangles )
		{
			meshlets[meshletOffset] = meshlet;

			for ( var j = 0u; j < meshlet.VertexCount; ++j )
				used[meshletVertices[(int)( meshlet.VertexOffset + j )]] = -1;

			meshlet.VertexOffset += meshlet.VertexCount;
			meshlet.TriangleOffset += meshlet.TriangleCount * 3;
			meshlet.VertexCount = 0;
			meshlet.TriangleCount = 0;

			result = true;
		}

		if ( av < 0 )
		{
			av = (short)meshlet.VertexCount;
			meshletVertices[(int)( meshlet.VertexOffset + meshlet.VertexCount++ )] = a;
		}

		if ( bv < 0 )
		{
			bv = (short)meshlet.VertexCount;
			meshletVertices[(int)( meshlet.VertexOffset + meshlet.VertexCount++ )] = b;
		}

		if ( cv < 0 )
		{
			cv = (short)meshlet.VertexCount;
			meshletVertices[(int)( meshlet.VertexOffset + meshlet.VertexCount++ )] = c;
		}

		var at = (int)( meshlet.TriangleOffset + ( meshlet.TriangleCount * 3 ) );
		meshletTriangles[at + 0] = (byte)av;
		meshletTriangles[at + 1] = (byte)bv;
		meshletTriangles[at + 2] = (byte)cv;
		meshlet.TriangleCount++;

		return result;
	}

	//The best triangle touching the meshlet's vertices, or MeshHash.Empty.
	private static uint NeighbourTriangle( in Meshlet meshlet, in Cone meshletCone, ReadOnlySpan<uint> meshletVertices,
		ReadOnlySpan<uint> indices, in Adjacency adjacency, ReadOnlySpan<Cone> triangles, ReadOnlySpan<uint> liveTriangles,
		ReadOnlySpan<short> used, float expectedRadius, float coneWeight )
	{
		var bestTriangle = MeshHash.Empty;
		var bestPriority = 5;
		var bestScore = float.MaxValue;

		for ( var i = 0u; i < meshlet.VertexCount; ++i )
		{
			var index = meshletVertices[(int)( meshlet.VertexOffset + i )];

			var neighbours = adjacency.Data.AsSpan( (int)adjacency.Offsets[index], (int)adjacency.Counts[index] );

			foreach ( var triangle in neighbours )
			{
				uint a = indices[(int)( triangle * 3 ) + 0], b = indices[(int)( triangle * 3 ) + 1], c = indices[(int)( triangle * 3 ) + 2];

				var extra = ( used[(int)a] < 0 ? 1 : 0 ) + ( used[(int)b] < 0 ? 1 : 0 ) + ( used[(int)c] < 0 ? 1 : 0 );

				int priority;

				//No new vertices is best of all; a triangle whose vertex would otherwise be left dangling is next, since it is
				//dear to add later; one that leaves another dangling helps the flow; the rest go by how many vertices they add.
				if ( extra == 0 )
					priority = 0;
				else if ( liveTriangles[(int)a] == 1 || liveTriangles[(int)b] == 1 || liveTriangles[(int)c] == 1 )
					priority = 1;
				else if ( ( liveTriangles[(int)a] == 2 ? 1 : 0 ) + ( liveTriangles[(int)b] == 2 ? 1 : 0 ) + ( liveTriangles[(int)c] == 2 ? 1 : 0 ) >= 2 )
					priority = 1 + extra;
				else
					priority = 2 + extra;

				//Topology outranks the score, so scoring is skipped where it cannot win.
				if ( priority > bestPriority ) continue;

				ref readonly var triCone = ref triangles[(int)triangle];

				float dx = triCone.px - meshletCone.px, dy = triCone.py - meshletCone.py, dz = triCone.pz - meshletCone.pz;
				var distance = MathF.Sqrt( ( dx * dx ) + ( dy * dy ) + ( dz * dz ) );
				var spread = ( triCone.nx * meshletCone.nx ) + ( triCone.ny * meshletCone.ny ) + ( triCone.nz * meshletCone.nz );

				var score = MeshletScore( distance, spread, coneWeight, expectedRadius );

				if ( priority < bestPriority || score < bestScore )
				{
					bestTriangle = triangle;
					bestPriority = priority;
					bestScore = score;
				}
			}
		}

		return bestTriangle;
	}

	//Adds a few seeds off the finished meshlet's edge, nearest the corner first.
	private static int AppendSeeds( Span<uint> seeds, in Meshlet meshlet, ReadOnlySpan<uint> meshletVertices, ReadOnlySpan<uint> indices,
		in Adjacency adjacency, ReadOnlySpan<Cone> triangles, ReadOnlySpan<uint> liveTriangles, float cornerx, float cornery, float cornerz )
	{
		Span<uint> bestSeeds = stackalloc uint[AddSeeds];
		Span<uint> bestLive = stackalloc uint[AddSeeds];
		Span<float> bestScore = stackalloc float[AddSeeds];

		for ( var i = 0; i < AddSeeds; ++i )
		{
			bestSeeds[i] = MeshHash.Empty;
			bestLive[i] = MeshHash.Empty;
			bestScore[i] = float.MaxValue;
		}

		for ( var i = 0u; i < meshlet.VertexCount; ++i )
		{
			var index = meshletVertices[(int)( meshlet.VertexOffset + i )];

			var bestNeighbour = MeshHash.Empty;
			var bestNeighbourLive = MeshHash.Empty;

			var neighbours = adjacency.Data.AsSpan( (int)adjacency.Offsets[index], (int)adjacency.Counts[index] );

			foreach ( var triangle in neighbours )
			{
				uint a = indices[(int)( triangle * 3 ) + 0], b = indices[(int)( triangle * 3 ) + 1], c = indices[(int)( triangle * 3 ) + 2];

				var live = liveTriangles[(int)a] + liveTriangles[(int)b] + liveTriangles[(int)c];

				if ( live < bestNeighbourLive )
				{
					bestNeighbour = triangle;
					bestNeighbourLive = live;
				}
			}

			if ( bestNeighbour == MeshHash.Empty ) continue;

			ref readonly var t = ref triangles[(int)bestNeighbour];
			float dx = t.px - cornerx, dy = t.py - cornery, dz = t.pz - cornerz;
			var bestNeighbourScore = MathF.Sqrt( ( dx * dx ) + ( dy * dy ) + ( dz * dz ) );

			for ( var j = 0; j < AddSeeds; ++j )
			{
				//Non-strict, so a triangle adjacent to several vertices is not entered several times over.
				if ( bestNeighbourLive < bestLive[j] || ( bestNeighbourLive == bestLive[j] && bestNeighbourScore <= bestScore[j] ) )
				{
					bestSeeds[j] = bestNeighbour;
					bestLive[j] = bestNeighbourLive;
					bestScore[j] = bestNeighbourScore;
					break;
				}
			}
		}

		var seedCount = 0;

		for ( var i = 0; i < AddSeeds; ++i )
			if ( bestSeeds[i] != MeshHash.Empty )
				seeds[seedCount++] = bestSeeds[i];

		return seedCount;
	}

	private static int PruneSeeds( Span<uint> seeds, int seedCount, ReadOnlySpan<byte> emitted )
	{
		var result = 0;

		for ( var i = 0; i < seedCount; ++i )
		{
			var index = seeds[i];

			seeds[result] = index;
			result += emitted[(int)index] == 0 ? 1 : 0;
		}

		return result;
	}

	private static uint SelectSeed( ReadOnlySpan<uint> seeds, ReadOnlySpan<uint> indices, ReadOnlySpan<Cone> triangles,
		ReadOnlySpan<uint> liveTriangles, float cornerx, float cornery, float cornerz )
	{
		var bestSeed = MeshHash.Empty;
		var bestLive = MeshHash.Empty;
		var bestScore = float.MaxValue;

		foreach ( var index in seeds )
		{
			uint a = indices[(int)( index * 3 ) + 0], b = indices[(int)( index * 3 ) + 1], c = indices[(int)( index * 3 ) + 2];

			var live = liveTriangles[(int)a] + liveTriangles[(int)b] + liveTriangles[(int)c];

			ref readonly var t = ref triangles[(int)index];
			float dx = t.px - cornerx, dy = t.py - cornery, dz = t.pz - cornerz;
			var score = MathF.Sqrt( ( dx * dx ) + ( dy * dy ) + ( dz * dz ) );

			if ( live < bestLive || ( live == bestLive && score < bestScore ) )
			{
				bestSeed = index;
				bestLive = live;
				bestScore = score;
			}
		}

		return bestSeed;
	}

	//-- the kd-tree over triangle centroids, for when adjacency runs out ------

	private struct KdNode
	{
		/// <summary>Split plane on a branch; the point's index on a leaf.</summary>
		public float Split;
		public uint Index;

		/// <summary>3 marks a leaf.</summary>
		public byte Axis;

		/// <summary>Leaf: points in this run, including this one. Branch: how far to the right subtree. Zero once exhausted.</summary>
		public uint Children;
	}

	private static int KdPartition( Span<uint> indices, ReadOnlySpan<Cone> points, int axis, float pivot )
	{
		var m = 0;

		for ( var i = 0; i < indices.Length; ++i )
		{
			ref readonly var p = ref points[(int)indices[i]];
			var v = axis == 0 ? p.px : axis == 1 ? p.py : p.pz;

			( indices[m], indices[i] ) = ( indices[i], indices[m] );

			m += v < pivot ? 1 : 0;
		}

		return m;
	}

	private static int KdBuildLeaf( int offset, Span<KdNode> nodes, ReadOnlySpan<uint> indices )
	{
		ref var result = ref nodes[offset];

		result.Index = indices[0];
		result.Axis = 3;
		result.Children = (uint)indices.Length;

		for ( var i = 1; i < indices.Length; ++i )
		{
			ref var tail = ref nodes[offset + i];

			tail.Index = indices[i];
			tail.Axis = 3;
			tail.Children = uint.MaxValue >> 2;
		}

		return offset + indices.Length;
	}

	private static int KdBuild( int offset, Span<KdNode> nodes, ReadOnlySpan<Cone> points, Span<uint> indices, int leafSize, int depth )
	{
		var count = indices.Length;

		if ( count <= leafSize ) return KdBuildLeaf( offset, nodes, indices );

		Span<float> mean = stackalloc float[3];
		Span<float> vars = stackalloc float[3];
		float runc = 1, runs = 1;

		//Welford's, for the variance along each axis.
		for ( var i = 0; i < count; ++i, runc += 1.0f, runs = 1.0f / runc )
		{
			ref readonly var point = ref points[(int)indices[i]];

			for ( var k = 0; k < 3; ++k )
			{
				var pk = k == 0 ? point.px : k == 1 ? point.py : point.pz;
				var delta = pk - mean[k];
				mean[k] += delta * runs;
				vars[k] += delta * ( pk - mean[k] );
			}
		}

		var axis = ( vars[0] >= vars[1] && vars[0] >= vars[2] ) ? 0 : ( vars[1] >= vars[2] ? 1 : 2 );

		var split = mean[axis];
		var middle = KdPartition( indices, points, axis, split );

		//A degenerate partition is a leaf, which also bounds the depth.
		if ( middle <= leafSize / 2 || middle >= count - ( leafSize / 2 ) || depth >= MaxTreeDepth )
			return KdBuildLeaf( offset, nodes, indices );

		ref var result = ref nodes[offset];

		result.Split = split;
		result.Axis = (byte)axis;

		var nextOffset = KdBuild( offset + 1, nodes, points, indices[..middle], leafSize, depth + 1 );

		nodes[offset].Children = (uint)( nextOffset - offset - 1 );

		return KdBuild( nextOffset, nodes, points, indices[middle..], leafSize, depth + 1 );
	}

	private static void KdNearest( Span<KdNode> nodes, int root, ReadOnlySpan<Cone> points, ReadOnlySpan<byte> emitted,
		float px, float py, float pz, ref uint result, ref float limit )
	{
		ref var node = ref nodes[root];

		if ( node.Children == 0 ) return;

		if ( node.Axis == 3 )
		{
			var inactive = true;

			for ( var i = 0u; i < node.Children; ++i )
			{
				var index = nodes[root + (int)i].Index;

				if ( emitted[(int)index] != 0 ) continue;

				inactive = false;

				ref readonly var point = ref points[(int)index];

				float dx = point.px - px, dy = point.py - py, dz = point.pz - pz;
				var distance = MathF.Sqrt( ( dx * dx ) + ( dy * dy ) + ( dz * dz ) );

				if ( distance < limit )
				{
					result = index;
					limit = distance;
				}
			}

			//A leaf with nothing left to give is skipped from here on.
			if ( inactive ) nodes[root].Children = 0;
		}
		else
		{
			var position = node.Axis == 0 ? px : node.Axis == 1 ? py : pz;
			var delta = position - node.Split;
			var first = delta <= 0 ? 0u : node.Children;
			var second = first ^ node.Children;

			//Deactivated before recursing, which delays it a step but keeps the tail call.
			if ( ( nodes[root + 1 + (int)first].Children | nodes[root + 1 + (int)second].Children ) == 0 )
				nodes[root].Children = 0;

			KdNearest( nodes, root + 1 + (int)first, points, emitted, px, py, pz, ref result, ref limit );

			if ( MathF.Abs( delta ) <= limit )
				KdNearest( nodes, root + 1 + (int)second, points, emitted, px, py, pz, ref result, ref limit );
		}
	}

	//-- the walk ------------------------------------------------------------------

	/// <summary>Cuts <paramref name="indices"/> into meshlets. The three outputs must be sized by <see cref="Bound"/>: that many meshlets, that many times <paramref name="maxVertices"/> vertices, that many times <paramref name="maxTriangles"/> times three triangle bytes. Returns how many meshlets were made.</summary>
	public static int Build( Span<Meshlet> meshlets, Span<uint> meshletVertices, Span<byte> meshletTriangles,
		ReadOnlySpan<uint> indices, ReadOnlySpan<Vector3> positions, int maxVertices, int maxTriangles, float coneWeight )
	{
		if ( indices.IsEmpty ) return 0;

		var vertexCount = positions.Length;
		var faceCount = indices.Length / 3;

		var adjacency = BuildAdjacency( indices, vertexCount );

		//Aliased: triangles are removed from the adjacency as they are emitted, so the counts are the live counts.
		var liveTriangles = adjacency.Counts;

		var emitted = new byte[faceCount];

		var triangles = new Cone[faceCount];
		var meshArea = TriangleCones( triangles, indices, positions );

		//A meshlet taken as a square patch: its expected radius is the root of its area.
		var triangleAreaAvg = faceCount == 0 ? 0.0f : meshArea / faceCount * 0.5f;
		var expectedRadius = MathF.Sqrt( triangleAreaAvg * maxTriangles ) * 0.5f;

		var kdIndices = new uint[faceCount];
		for ( var i = 0; i < faceCount; ++i ) kdIndices[i] = (uint)i;

		var nodes = new KdNode[faceCount * 2];
		KdBuild( 0, nodes, triangles, kdIndices, 8, 0 );

		//The corner the flow starts from.
		float cornerx = float.MaxValue, cornery = float.MaxValue, cornerz = float.MaxValue;

		for ( var i = 0; i < faceCount; ++i )
		{
			ref readonly var tri = ref triangles[i];

			cornerx = cornerx > tri.px ? tri.px : cornerx;
			cornery = cornery > tri.py ? tri.py : cornery;
			cornerz = cornerz > tri.pz ? tri.pz : cornerz;
		}

		//Each vertex's slot in the current meshlet, -1 when it has none.
		var used = new short[vertexCount];
		System.Array.Fill( used, (short)-1 );

		var initialSeed = MeshHash.Empty;
		var initialScore = float.MaxValue;

		for ( var i = 0; i < faceCount; ++i )
		{
			ref readonly var tri = ref triangles[i];

			float dx = tri.px - cornerx, dy = tri.py - cornery, dz = tri.pz - cornerz;
			var score = MathF.Sqrt( ( dx * dx ) + ( dy * dy ) + ( dz * dz ) );

			if ( initialSeed == MeshHash.Empty || score < initialScore )
			{
				initialSeed = (uint)i;
				initialScore = score;
			}
		}

		Span<uint> seeds = stackalloc uint[MaxSeeds];
		var seedCount = 0;

		var meshlet = new Meshlet();
		var meshletOffset = 0;

		var coneAcc = new Cone();

		for ( ; ; )
		{
			var meshletCone = MeshletCone( in coneAcc, meshlet.TriangleCount );

			uint bestTriangle;

			//The first triangle of all is the seed; after that, the best neighbour.
			if ( meshletOffset == 0 && meshlet.TriangleCount == 0 )
				bestTriangle = initialSeed;
			else
				bestTriangle = NeighbourTriangle( in meshlet, in meshletCone, meshletVertices, indices, in adjacency,
					triangles, liveTriangles, used, expectedRadius, coneWeight );

			//Out of neighbours: the nearest triangle left, connected or not.
			if ( bestTriangle == MeshHash.Empty )
			{
				var index = MeshHash.Empty;
				var distance = float.MaxValue;

				KdNearest( nodes, 0, triangles, emitted, meshletCone.px, meshletCone.py, meshletCone.pz, ref index, ref distance );

				bestTriangle = index;
			}

			if ( bestTriangle == MeshHash.Empty ) break;

			var bestExtra = ( used[indices[(int)( bestTriangle * 3 ) + 0]] < 0 ? 1 : 0 )
				+ ( used[indices[(int)( bestTriangle * 3 ) + 1]] < 0 ? 1 : 0 )
				+ ( used[indices[(int)( bestTriangle * 3 ) + 2]] < 0 ? 1 : 0 );

			//It will not fit, so the next meshlet starts from a seed instead, to keep the flow going from the corner.
			if ( meshlet.VertexCount + bestExtra > maxVertices || meshlet.TriangleCount >= maxTriangles )
			{
				seedCount = PruneSeeds( seeds, seedCount, emitted );
				seedCount = seedCount + AddSeeds <= MaxSeeds ? seedCount : MaxSeeds - AddSeeds;
				seedCount += AppendSeeds( seeds[seedCount..], in meshlet, meshletVertices, indices, in adjacency,
					triangles, liveTriangles, cornerx, cornery, cornerz );

				var bestSeed = SelectSeed( seeds[..seedCount], indices, triangles, liveTriangles, cornerx, cornery, cornerz );

				//A disconnected mesh may have no seed, since seeds come off adjacency.
				bestTriangle = bestSeed != MeshHash.Empty ? bestSeed : bestTriangle;
			}

			uint a = indices[(int)( bestTriangle * 3 ) + 0], b = indices[(int)( bestTriangle * 3 ) + 1], c = indices[(int)( bestTriangle * 3 ) + 2];

			if ( Append( ref meshlet, a, b, c, used, meshlets, meshletVertices, meshletTriangles, meshletOffset, maxVertices, maxTriangles ) )
			{
				meshletOffset++;
				coneAcc = default;
			}

			//The emitted triangle leaves the adjacency lists, which shortens every later walk and keeps the live counts true.
			for ( var k = 0; k < 3; ++k )
			{
				var index = indices[(int)( bestTriangle * 3 ) + k];

				var offset = (int)adjacency.Offsets[index];
				var size = (int)adjacency.Counts[index];

				for ( var i = 0; i < size; ++i )
				{
					if ( adjacency.Data[offset + i] == bestTriangle )
					{
						adjacency.Data[offset + i] = adjacency.Data[offset + size - 1];
						adjacency.Counts[index]--;
						break;
					}
				}
			}

			ref readonly var chosen = ref triangles[(int)bestTriangle];
			coneAcc.px += chosen.px;
			coneAcc.py += chosen.py;
			coneAcc.pz += chosen.pz;
			coneAcc.nx += chosen.nx;
			coneAcc.ny += chosen.ny;
			coneAcc.nz += chosen.nz;

			emitted[(int)bestTriangle] = 1;
		}

		if ( meshlet.TriangleCount != 0 ) meshlets[meshletOffset++] = meshlet;

		return meshletOffset;
	}
}
