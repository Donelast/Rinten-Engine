//Kept as a file rather than as text inside the engine's C#, so it is compiled the way every other shader is - includes
//and all - and a project may put its own file of this name in its assets to replace it. See CoreShaderCache.Pass.

//Adds one weighted blend shape into a posed instance's sums, one thread per delta

[[vk::binding(0,0)]] ByteAddressBuffer g_deltas;
[[vk::binding(1,0)]] RWByteAddressBuffer g_sums;

[[vk::push_constant]]
cbuffer ShapesPush
{
	//This shape's run of the model's deltas, sorted by shape - see SceneMesh.ShapeDeltas.
	uint g_first;
	uint g_count;

	//Where the instance's sums start, in vertices, and how many it has - a delta naming a vertex past the end is not written.
	uint g_sumsAt;
	uint g_vertices;

	float g_weight;
};

//A vertex index and two float3s - MorphDelta's twenty-eight bytes, with the vertex where the shape's index was. See CoreSceneRenderer.ShapeMajor.
static const uint ShapeDeltaStride = 28;
static const uint ShapeSumBytes = 24;

[shader("compute")]
[numthreads(128, 1, 1)]
void main( uint3 id : SV_DispatchThreadID )
{
	if ( id.x >= g_count ) return;

	uint at = ( g_first + id.x ) * ShapeDeltaStride;
	uint vertex = g_deltas.Load( at );
	if ( vertex >= g_vertices ) return;

	float3 position = asfloat( g_deltas.Load3( at + 4 ) ) * g_weight;
	float3 normal = asfloat( g_deltas.Load3( at + 16 ) ) * g_weight;

	uint sum = ( g_sumsAt + vertex ) * ShapeSumBytes;
	float3 heldPosition = asfloat( g_sums.Load3( sum ) );
	float3 heldNormal = asfloat( g_sums.Load3( sum + 12 ) );

	g_sums.Store3( sum, asuint( heldPosition + position ) );
	g_sums.Store3( sum + 12, asuint( heldNormal + normal ) );
}
