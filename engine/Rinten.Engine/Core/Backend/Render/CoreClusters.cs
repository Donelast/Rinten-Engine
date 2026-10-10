using System;
using System.Runtime.InteropServices;

namespace Rinten;

//All loaded meshes' clusters, vertices and triangles on the GPU, plus one record per mesh
internal static class CoreClusters
{
	//One cluster as the file and the card have it - ModelFile.ClusterStride bytes.
	[StructLayout( LayoutKind.Sequential )]
	internal struct Record
	{
		public float CentreX, CentreY, CentreZ, Radius;
		public float ConeX, ConeY, ConeZ, ConeCutoff;
		public float MinX, MinY, MinZ, Error;
		public float MaxX, MaxY, MaxZ, ParentError;
		public uint VertexOffset;
		public uint TriangleOffset;
		public uint Counts;
		public uint PartLevel;
	}

	//One mesh as the card has it - see mesh_tables.slang. One level of a model's chain: where its clusters are, its error, and
	//the error of the level above it. What the cull picks a level by, once an instance - see ChainLevel in mesh_cull.slang.
	[StructLayout( LayoutKind.Sequential )]
	internal struct LevelSpan
	{
		public uint First;
		public uint Count;
		public float MinError;
		public float MaxParent;
	}

	//The most levels a mesh is described by here; the rest fold into the last.
	internal const int MostLevels = 8;

	//How many meshes were taken with each number of levels, for the mesh report - a scene whose models were built with one level
	//has no cut to make, and the cull will draw every cluster of every one of them at the finest level whatever the camera is doing.
	internal static readonly int[] KeptWithLevels = new int[MostLevels + 1];

	[StructLayout( LayoutKind.Sequential )]
	internal struct MeshInfo
	{
		public uint ClusterFirst;
		public uint ClusterCount;
		public uint Flags;
		public uint Levels;
		public float CentreX, CentreY, CentreZ, Radius;
		public float MinX, MinY, MinZ, MinPad;
		public float MaxX, MaxY, MaxZ, MaxPad;
		public LevelSpan L0, L1, L2, L3, L4, L5, L6, L7;
	}

	//MeshInfo.Flags: the mesh's levels are a chain, and two bits from MeshShadowShift say how many levels coarser its shadows are.
	internal const uint MeshChain = 4;
	internal const int MeshShadowShift = 8;

	internal const int RecordBytes = 80;
	internal const int MeshBytes = 64 + ( MostLevels * 16 );

	//What one level of a chain holds on the device: its clusters, the triangles and vertex references they carry, and
	//the pool vertices only they name - for the memory report, and for what a chain of levels would have to hold instead.
	internal struct LevelTally
	{
		public long Clusters, Triangles, References, Vertices;

		/// <summary>Its bytes: the records, the references, the triangles and the vertices with their sides.</summary>
		public readonly long Bytes => ( Clusters * RecordBytes ) + ( References * 4 ) + ( Triangles * 3 ) +
			( Vertices * CoreGeometryPool.VertexBytes );
	}

	//Every live mesh's levels added together.
	internal static readonly LevelTally[] Levels = new LevelTally[MostLevels];

	//Pool vertices no cluster names - the loose path's alone.
	internal static long UnnamedVertices;

	internal static readonly CoreGeometryPool.Pool Clusters = new( RecordBytes, 1 << 14 );
	internal static readonly CoreGeometryPool.Pool ClusterVertices = new( 4, 1 << 18 );
	internal static readonly CoreGeometryPool.Pool ClusterTriangles = new( 3, 1 << 18 );
	internal static readonly CoreGeometryPool.Pool Meshes = new( MeshBytes, 1 << 10 );

	//Where each level of a mesh's chain sits and what error it carries - see LevelSpan. A chain's builder
	//writes the levels in order, so a level is one run. Levels past the eighth fold into the eighth.
	private static void Describe( ReadOnlySpan<Record> clusters, uint clusterAt, Span<LevelSpan> into )
	{
		for ( var i = 0; i < into.Length; i++ )
			into[i] = new LevelSpan { First = 0, Count = 0, MinError = 0.0f, MaxParent = 0.0f };

		Span<uint> last = stackalloc uint[MostLevels];

		for ( var i = 0; i < clusters.Length; i++ )
		{
			var level = Math.Min( (int)( clusters[i].PartLevel >> 16 ), MostLevels - 1 );
			var at = clusterAt + (uint)i;

			ref var span = ref into[level];

			if ( span.Count == 0 )
			{
				span.First = at;
				span.MinError = clusters[i].Error;
				span.MaxParent = clusters[i].ParentError;
			}
			else
			{
				if ( clusters[i].Error < span.MinError ) span.MinError = clusters[i].Error;
				if ( clusters[i].ParentError > span.MaxParent ) span.MaxParent = clusters[i].ParentError;
			}

			last[level] = at;
			span.Count = ( last[level] - span.First ) + 1;
		}
	}

	internal static bool Keep( CoreSceneRenderer.SceneMesh mesh, Span<Record> clusters, Span<uint> vertices,
		ReadOnlySpan<byte> triangles, int levels )
	{
		if ( mesh is null || !mesh.Pooled ) return false;

		var slot = Meshes.Take( 1 );
		if ( slot < 0 ) return false;

		var clusterAt = clusters.Length > 0 ? Clusters.Take( clusters.Length ) : 0;
		var vertexAt = vertices.Length > 0 ? ClusterVertices.Take( vertices.Length ) : 0;
		var triangleAt = triangles.Length > 0 ? ClusterTriangles.Take( triangles.Length / 3 ) : 0;

		if ( clusterAt < 0 || vertexAt < 0 || triangleAt < 0 )
		{
			Meshes.Give( slot, 1 );
			if ( clusterAt >= 0 && clusters.Length > 0 ) Clusters.Give( clusterAt, clusters.Length );
			if ( vertexAt >= 0 && vertices.Length > 0 ) ClusterVertices.Give( vertexAt, vertices.Length );
			if ( triangleAt >= 0 && triangles.Length > 0 ) ClusterTriangles.Give( triangleAt, triangles.Length / 3 );
			return false;
		}

		for ( var i = 0; i < clusters.Length; i++ )
		{
			clusters[i].VertexOffset += (uint)vertexAt;
			clusters[i].TriangleOffset += (uint)triangleAt;
		}

		var poolVertex = (uint)mesh.PoolVertexAt;
		for ( var i = 0; i < vertices.Length; i++ ) vertices[i] += poolVertex;

		var centre = mesh.Bounds.Center;
		var radius = mesh.Bounds.Size.Length * 0.5f;

		FitSphere( clusters, ref centre, ref radius );

		var info = new MeshInfo
		{
			ClusterFirst = (uint)clusterAt,
			ClusterCount = (uint)clusters.Length,
			Flags = ( clusters.Length > 0 ? 1u : 0u ) | ( mesh.IsSkinned ? 2u : 0u ) |
				( mesh.Chain ? MeshChain | ( (uint)Math.Clamp( mesh.ShadowLevelOffset, 0, 3 ) << MeshShadowShift ) : 0u ),
			Levels = (uint)Math.Max( 1, levels ),
			CentreX = centre.x, CentreY = centre.y, CentreZ = centre.z, Radius = radius,
			MinX = mesh.Bounds.Mins.x, MinY = mesh.Bounds.Mins.y, MinZ = mesh.Bounds.Mins.z,
			MaxX = mesh.Bounds.Maxs.x, MaxY = mesh.Bounds.Maxs.y, MaxZ = mesh.Bounds.Maxs.z,
		};

		Describe( clusters, (uint)clusterAt, MemoryMarshal.CreateSpan( ref info.L0, MostLevels ) );

		var written = Clusters.Write( clusterAt, MemoryMarshal.AsBytes( clusters ) ) &&
			ClusterVertices.Write( vertexAt, MemoryMarshal.AsBytes( vertices ) ) &&
			ClusterTriangles.Write( triangleAt, triangles ) &&
			Meshes.Write( slot, MemoryMarshal.AsBytes( MemoryMarshal.CreateSpan( ref info, 1 ) ) );

		if ( !written )
		{
			Meshes.Give( slot, 1 );
			if ( clusters.Length > 0 ) Clusters.Give( clusterAt, clusters.Length );
			if ( vertices.Length > 0 ) ClusterVertices.Give( vertexAt, vertices.Length );
			if ( triangles.Length > 0 ) ClusterTriangles.Give( triangleAt, triangles.Length / 3 );
			return false;
		}

		mesh.MeshSlot = (int)slot;
		mesh.ClusterFirst = (int)clusterAt;
		mesh.ClusterCount = clusters.Length;
		mesh.ClusterVertexAt = (int)vertexAt;
		mesh.ClusterVertexCount = vertices.Length;
		mesh.ClusterTriangleAt = (int)triangleAt;
		mesh.ClusterTriangleCount = triangles.Length / 3;
		mesh.ClusterLevels = Math.Max( 1, levels );
		KeptWithLevels[Math.Clamp( mesh.ClusterLevels, 0, MostLevels )]++;
		mesh.LevelTallies = Tally( clusters, vertices, (uint)vertexAt, (uint)mesh.PoolVertexAt, mesh.PoolVertexCount, out var unnamed );
		mesh.UnnamedVertices = unnamed;
		Add( mesh, 1 );
		mesh.BoundCentre = centre;
		mesh.BoundRadius = radius;

		return true;
	}

	//A sphere that actually holds the finest level, pulled in from the box's by Ritter's pass over the clusters.
	private static void FitSphere( ReadOnlySpan<Record> clusters, ref Vector3 centre, ref float radius )
	{
		if ( clusters.IsEmpty ) return;

		var middle = centre;
		var reach = 0.0f;

		foreach ( ref readonly var one in clusters )
		{
			if ( ( one.PartLevel >> 16 ) != 0 ) continue;
			var at = new Vector3( one.CentreX, one.CentreY, one.CentreZ );
			reach = MathF.Max( reach, middle.Distance( at ) + one.Radius );
		}

		foreach ( ref readonly var one in clusters )
		{
			if ( ( one.PartLevel >> 16 ) != 0 ) continue;
			var at = new Vector3( one.CentreX, one.CentreY, one.CentreZ );
			var away = at - middle;
			var far = away.Length;

			if ( far + one.Radius <= reach ) continue;

			var grown = ( reach + far + one.Radius ) * 0.5f;
			if ( far > 0.0001f ) middle += away * ( ( grown - reach ) / far );
			reach = grown;
		}

		if ( reach > 0.0f && reach < radius )
		{
			centre = middle;
			radius = reach;
		}
	}

	//What each level of a mesh holds. A pool vertex counts to the coarsest level that
	//names it - since the levels own their vertices, that is almost always the only one.
	private static LevelTally[] Tally( ReadOnlySpan<Record> clusters, ReadOnlySpan<uint> vertices, uint vertexAt, uint poolAt, int poolCount, out long unnamed )
	{
		var tallies = new LevelTally[MostLevels];
		var owner = new sbyte[Math.Max( poolCount, 0 )];
		owner.AsSpan().Fill( -1 );

		foreach ( ref readonly var one in clusters )
		{
			var level = Math.Min( (int)( one.PartLevel >> 16 ), MostLevels - 1 );
			var count = (int)( one.Counts & 0xFFFFu );

			tallies[level].Clusters++;
			tallies[level].Triangles += one.Counts >> 16;
			tallies[level].References += count;

			//Both already moved to where they sit on the device: the offsets into the arena, the references into the pool.
			var first = (int)( one.VertexOffset - vertexAt );

			for ( var i = 0; i < count; i++ )
			{
				var at = first + i;
				if ( at < 0 || at >= vertices.Length ) continue;

				var vertex = (long)vertices[at] - poolAt;
				if ( vertex < 0 || vertex >= owner.Length ) continue;

				if ( owner[vertex] < level ) owner[vertex] = (sbyte)level;
			}
		}

		unnamed = 0;

		foreach ( var level in owner )
		{
			if ( level < 0 ) unnamed++;
			else tallies[level].Vertices++;
		}

		return tallies;
	}

	private static void Add( CoreSceneRenderer.SceneMesh mesh, int sign )
	{
		if ( mesh.LevelTallies is null ) return;

		for ( var i = 0; i < MostLevels; i++ )
		{
			ref var into = ref Levels[i];
			var one = mesh.LevelTallies[i];

			into.Clusters += sign * one.Clusters;
			into.Triangles += sign * one.Triangles;
			into.References += sign * one.References;
			into.Vertices += sign * one.Vertices;
		}

		UnnamedVertices += sign * mesh.UnnamedVertices;
	}

	internal static void Give( CoreSceneRenderer.SceneMesh mesh )
	{
		if ( mesh is null || mesh.MeshSlot < 0 ) return;

		Add( mesh, -1 );
		mesh.LevelTallies = null;

		Meshes.Give( mesh.MeshSlot, 1 );
		if ( mesh.ClusterCount > 0 ) Clusters.Give( mesh.ClusterFirst, mesh.ClusterCount );
		if ( mesh.ClusterVertexCount > 0 ) ClusterVertices.Give( mesh.ClusterVertexAt, mesh.ClusterVertexCount );
		if ( mesh.ClusterTriangleCount > 0 ) ClusterTriangles.Give( mesh.ClusterTriangleAt, mesh.ClusterTriangleCount );

		mesh.MeshSlot = -1;
		mesh.ClusterCount = 0;
		mesh.ClusterVertexCount = 0;
		mesh.ClusterTriangleCount = 0;
	}

	//Forgets the lot, which is what a device going takes with it.
	internal static void Drop()
	{
		Clusters.Drop();
		ClusterVertices.Drop();
		ClusterTriangles.Drop();
		Meshes.Drop();
	}
}
