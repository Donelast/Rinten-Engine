#include "colour.slang"
#include "frame_data.slang"

//Kept as a file rather than as text inside the engine's C#, so it is compiled the way every other shader is - includes
//and all - and a project may put its own file of this name in its assets to replace it. See CoreShaderCache.Pass.

//Lines: clip transform in the push constants, colour straight through. One shader
//for the grid and the selection boxes; depth behaviour is the pipeline's business.

struct VIn
{
	[[vk::location(0)]] float3 position;
	[[vk::location(1)]] float4 colour;
};

struct V2F
{
	float4 position : SV_Position;
	float4 colour : COLOR0;
};

[[vk::push_constant]]
cbuffer Push
{
	//A finished world-to-clip, or the object's world matrix where g_mode.x says so - see gizmo.slang, which carries the same pair for the same reason.
	row_major float4x4 g_matrix;
	float4 g_mode;
};

[shader("vertex")]
V2F MainVs( VIn i )
{
	V2F o;

	float4 placed = mul( float4( i.position, 1.0 ), g_matrix );
	o.position = g_mode.x != 0.0 ? EyeClip( placed ) : placed;

	//Colour is authored sRGB and the swapchain encodes again, so decode per vertex
	o.colour = float4( ToLinear( i.colour.rgb ), i.colour.a );

	return o;
}

[shader("fragment")]
float4 MainPs( V2F i ) : SV_Target
{
	return i.colour;
}
