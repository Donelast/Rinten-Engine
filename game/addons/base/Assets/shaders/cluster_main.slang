//Shades the culled per-material cluster lists, depth-tested equal against the pre-pass

#ifdef CLUSTER

#include "vis_instance.slang"

//A posed thing's vertex out of its posed range rather than the mesh's own.
uint VisPosed( VisInstance i, uint v )
{
	return ( i.flags & 16u ) != 0 ? ( v - i.posedSource ) + i.posedFirst : v;
}

struct VisCluster
{
	float4 sphere;
	float4 cone;
	float4 boxMinError;
	float4 boxMaxParentError;
	uint4 words;
};

//The head of CoreMeshShaders' Params, word for word, and the camera's view after
//it - only the first entry is ever read, so the stride past these does not matter.
struct VisParams
{
	uint instanceCount;
	uint viewCount;
	uint listCount;
	uint workCapacity;
	uint retestList;
	uint cameraLists;
	uint materialsAt;
	uint entriesAt;
	uint shadowIndexAt;
	uint shadowIndexCapacity;
	uint listTotal;
	uint materials;
	uint binCountsAt;
	uint binOffsetsAt;
	uint binFillAt;
	uint binnedAt;
	uint binArgsAt;
	uint hiddenAt;
	uint binSlices;
	uint meshArgsAt;
	uint debugAt;
	uint errorScale;
	uint binTrisAt;
	uint bins;
	uint singlesAt;
	uint singlesCapacity;
	uint binTriFillAt;
	uint binTriOffsetsAt;
	uint drawArgsAt;
	uint binIndexAt;
	uint binIndexCapacity;
	uint binnedCapacity;

	//How many draws a material's run is cut into by distance - see binDraws in mesh_tables.slang.
	uint binDraws;

	//See meshDepth in mesh_tables.slang.
	uint meshDepth;
	//See cellsAt in mesh_tables.slang - the cull's alone.
	uint cellsAt;

	//Which still clusters the depth pre-pass draws, by distance slice: those in a nearer slice. Nought is none, all ones is all - see
	//CoreSceneDrawer.PrepassSlices. Past it a cluster's material writes its depth in its own far pass, and the cull writes it no pre-pass run.
	uint prepassSlices;

	//See wantAt in mesh_tables.slang - the cull's alone.
	uint wantAt;
	uint wantPad0;
	uint wantPad1;
	uint wantPad2;

	//Must start on a float4 boundary; keep in sync with CoreMeshRender.HeaderWords and mesh_tables Params
	float4 planes[6];
	row_major float4x4 worldToClip;
};

[[vk::binding(0,2)]] StructuredBuffer<VisParams> g_visParams;
[[vk::binding(1,2)]] StructuredBuffer<VisInstance> g_visInstances;
[[vk::binding(2,2)]] StructuredBuffer<VisCluster> g_visClusters;
[[vk::binding(3,2)]] StructuredBuffer<uint> g_visClusterVertices;
[[vk::binding(4,2)]] StructuredBuffer<uint> g_visClusterTriangles;
[[vk::binding(5,2)]] ByteAddressBuffer g_visVertices;
[[vk::binding(6,2)]] StructuredBuffer<uint> g_visVisible;

//A posed thing's position and frame, at its posed vertex - the pool's two sides the skin pass writes;
//see CoreGeometryPool.FrameSide and engine/mesh_skin.slang. The rest of a posed vertex is the mesh's own.
[[vk::binding(8,2)]] StructuredBuffer<float> g_visPositions;
[[vk::binding(9,2)]] StructuredBuffer<uint> g_visFrames;

//One PackedVertex from the pool; both UV sets are read whole since surfaces may use all four
struct VisVertex
{
	float3 position;
	uint frame;
	float4 uv0;
	float4 uv1;
	float4 colour;
};

static const uint VisVertexBytes = EngineVertexBytes;

VisVertex LoadVisVertex( uint v )
{
	uint at = v * VisVertexBytes;

	VisVertex o;
	o.position = asfloat( g_visVertices.Load3( at ) );
	uint packed = g_visVertices.Load( at + EngineVertexColour );
	o.colour = float4( packed & 255u, ( packed >> 8 ) & 255u, ( packed >> 16 ) & 255u, packed >> 24 ) / 255.0;
	o.frame = g_visVertices.Load( at + EngineVertexFrame );
	o.uv0 = asfloat( g_visVertices.Load4( at + EngineVertexTexCoord0 ) );
	o.uv1 = asfloat( g_visVertices.Load4( at + EngineVertexTexCoord1 ) );

	return o;
}


//What one corner of one triangle of one cluster is, as the vertex stage wants it: the vertex out of the pool, and whose instance it is.
struct ClusterVertex
{
	float3 position;
	uint frame;
	float4 uv0;
	float4 uv1;
	float4 colour;
	uint instance;

	//Which vertex of the pool the corner is as the mesh was modelled: the one the fields above were read out of for
	//an instance nothing poses, and the bind pose's for one the skin pass posed - see RestPosition in frame.slang.
	uint rest;

	//Where the corner lands, worked out the way the pre-pass worked it out - the same two products in the same order - so that the depth
	//the material's pass is tested equal against is the depth it wrote, bit for bit. Nowhere at all for a corner that is not drawn.
	float4 clip;

};

//Where a corner that is not drawn goes: nowhere the rasteriser will look.
float4 ClusterCulled()
{
	return float4( asfloat( 0x7fc00000u ), 0.0, 0.0, 1.0 );
}

//The draw's numbers out of its instance rather than out of a push block: the matrix, the tint, and what the draw set for itself.
void ClusterInstance( uint index )
{
	VisInstance instance = g_visInstances[index];

	g_modelToWorld = float4x4( instance.world0, instance.world1, instance.world2, instance.world3 );

	//Instance tint times the material tint, which arrives separately in the push block's spare slot
	g_tint = instance.tint * g_visSpare0;
	g_object = instance.numbers;
	g_draw0 = instance.said0;
	g_draw1 = instance.said1;
}

//The vertex an index of the material's binned run names: the pair's place in the binned list in the high bits, which vertex of its cluster in the
//low seven - see the bin pass of engine/mesh_cull.slang and EntryShift there. A vertex two triangles share is one index and is worked out once.
bool LoadClusterVertex( uint vid, out ClusterVertex v )
{
	uint4 named = asuint( g_visNamed[0] );

	uint pair = named.x + ( ( vid >> 7 ) * 2 );
	uint index = g_visVisible[pair];

	//The bin pass writes the cluster plain. The mask is for the lists' own pairs, whose top bits carry the cull's answers -
	//see ClusterOf in CoreMeshShaders - so a reader copied from here onto those does not index the cluster table past its end.
	uint cluster = g_visVisible[pair + 1] & 0x3FFFFFFFu;

	return LoadClusterCorner( index, cluster, vid & 127u, v );
}

//A corner before anything is known of it: nowhere on the screen.
void ClusterNothing( uint index, out ClusterVertex v )
{
	v.instance = index;
	v.clip = ClusterCulled();
	v.position = float3( 0.0, 0.0, 0.0 );
	v.frame = VertexFrameEncode( float3( 0.0, 1.0, 0.0 ), float4( 1.0, 0.0, 0.0, 1.0 ) );
	v.uv0 = float4( 0.0, 0.0, 0.0, 0.0 );
	v.uv1 = float4( 0.0, 0.0, 0.0, 0.0 );
	v.colour = float4( 1.0, 1.0, 1.0, 1.0 );
	v.rest = 0u;
}

//One vertex of one cluster of one instance, as the vertex stage wants it: the
//vertex out of the pool - posed, where the skin pass posed it - and where it lands.
bool LoadClusterCorner( uint index, uint cluster, uint local, out ClusterVertex v )
{
	ClusterNothing( index, v );

	VisCluster held = g_visClusters[cluster];
	VisInstance instance = g_visInstances[index];
	v.rest = g_visClusterVertices[held.words.x + local];
	VisVertex loaded = LoadVisVertex( v.rest );

	if ( ( instance.flags & 16u ) != 0 )
	{
		uint posed = VisPosed( instance, v.rest );
		loaded.position = float3( g_visPositions[posed * 3], g_visPositions[( posed * 3 ) + 1], g_visPositions[( posed * 3 ) + 2] );
		loaded.frame = g_visFrames[posed];
	}

	v.position = loaded.position;
	v.frame = loaded.frame;
	v.uv0 = loaded.uv0;
	v.uv1 = loaded.uv1;
	v.colour = loaded.colour;

	//Must match mesh_raster.slang bit for bit (precise) so the equal depth test keeps one fragment
	float4x4 world = float4x4( instance.world0, instance.world1, instance.world2, instance.world3 );
	precise float3 at = mul( float4( loaded.position, 1.0 ), world ).xyz;
	precise float4 clip = mul( float4( at, 1.0 ), g_visParams[0].worldToClip );
	v.clip = clip;

	return true;
}

#endif
