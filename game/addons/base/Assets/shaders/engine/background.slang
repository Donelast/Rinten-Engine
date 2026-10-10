//Kept as a file rather than as text inside the engine's C#, so it is compiled the way every other shader is - includes
//and all - and a project may put its own file of this name in its assets to replace it. See CoreShaderCache.Pass.

//Camera background colour at the far plane where nothing drew, with exposure and tonemap applied

#include "tonemap.slang"


struct V2F
{
	float4 position : SV_Position;
};

[[vk::push_constant]]
cbuffer Push
{
	float4 g_colour;  //the background, in the light the world is shaded in
	float4 g_finish;  //exposure x, curve y
};

[shader("vertex")]
V2F MainVs( uint id : SV_VertexID )
{
	float2 corner = float2( ( id << 1 ) & 2, id & 2 );

	V2F o;

	//At the far plane, like the sky, so the depth test keeps it behind everything that drew.
	o.position = float4( ( corner * 2.0 ) - 1.0, 1.0, 1.0 );
	return o;
}

[shader("fragment")]
float4 MainPs( V2F i ) : SV_Target
{
	return float4( EngineTonemap( g_colour.rgb * g_finish.x, g_finish.y ), 1.0 );
}
