#include "colour.slang"
#include "frame_data.slang"

//Kept as a file rather than as text inside the engine's C#, so it is compiled the way every other shader is - includes
//and all - and a project may put its own file of this name in its assets to replace it. See CoreShaderCache.Pass.

//Billboards: engine-layout vertices already facing the camera, a glyph texture, a tint - and no lighting, an icon is an icon from anywhere.

struct VIn
{
	[[vk::location(0)]] float3 position;
	[[vk::location(1)]] float4 colour;
	[[vk::location(3)]] float4 texCoord0;
};

struct V2F
{
	float4 position : SV_Position;
	float4 colour : COLOR0;
	float2 uv : TEXCOORD0;
};

[[vk::push_constant]]
cbuffer Push
{
	//A finished world-to-clip, or the object's world matrix where g_mode.x says so - see gizmo.slang, which carries the same pair for the same reason.
	row_major float4x4 g_matrix;
	float4 g_mode;
};

[[vk::binding(0,0)]] Sampler2D g_color;

[shader("vertex")]
V2F MainVs( VIn i )
{
	V2F o;

	float4 placed = mul( float4( i.position, 1.0 ), g_matrix );
	o.position = g_mode.x != 0.0 ? EyeClip( placed ) : placed;
	//The picture beside this is read through an sRGB view and arrives linear already; the colour it is
	//tinted by was authored and does not. See colour.slang - this is what made gizmo labels look washed out.
	o.colour = float4( ToLinear( i.colour.rgb ), i.colour.a );
	o.uv = i.texCoord0.xy;
	return o;
}

[shader("fragment")]
float4 MainPs( V2F i ) : SV_Target
{
	return g_color.Sample( i.uv ) * i.colour;
}
