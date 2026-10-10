//Kept as a file rather than as text inside the engine's C#, so it is compiled the way every other shader is - includes
//and all - and a project may put its own file of this name in its assets to replace it. See CoreShaderCache.Pass.

//Screen motion for moved objects only; elsewhere motion is derived from depth and the camera

#include "frame_data.slang"

struct VIn
{
	[[vk::location(0)]] float3 position;
};

struct V2F
{
	float4 position : SV_Position;

	//Where this vertex is now and where it was, both in clip space, so that the division by w happens per
	//pixel rather than per vertex - a triangle across the screen is not linear in the thing being divided.
	float4 now : TEXCOORD0;
	float4 before : TEXCOORD1;
};

[[vk::push_constant]]
cbuffer Push
{
	row_major float4x4 g_modelToWorld;
	row_major float4x4 g_wasModelToWorld;
};

[shader("vertex")]
V2F MainVs( VIn i )
{
	float3 world = mul( float4( i.position, 1.0 ), g_modelToWorld ).xyz;
	float3 was = mul( float4( i.position, 1.0 ), g_wasModelToWorld ).xyz;

	V2F o;

	o.now = mul( float4( world, 1.0 ), EYE_FRAME( worldToClip ) );
	o.before = mul( float4( was, 1.0 ), EYE_FRAME( previousWorldToClip ) );
	o.position = o.now;

	return o;
}

[shader("fragment")]
float4 MainPs( V2F i ) : SV_Target
{
	if ( i.now.w <= 0.0001 || i.before.w <= 0.0001 ) return float4( 0.0, 0.0, 0.0, 0.0 );

	float2 here = ( ( i.now.xy / i.now.w ) * 0.5 ) + 0.5;
	float2 there = ( ( i.before.xy / i.before.w ) * 0.5 ) + 0.5;

	//Screen delta, view depth (clip w), and 1 to mark the pixel as written by this pass
	return float4( here - there, i.now.w, 1.0 );
}
