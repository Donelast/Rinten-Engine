#include "colour.slang"
#include "frame_data.slang"

//Kept as a file rather than as text inside the engine's C#, so it is compiled the way every other shader is - includes
//and all - and a project may put its own file of this name in its assets to replace it. See CoreShaderCache.Pass.

//Gizmo batches: engine-layout vertices - position at 0, colour at 1, the rest
//ignored - times a per-batch tint carried after the matrix in the push block.

struct VIn
{
	[[vk::location(0)]] float3 position;
	[[vk::location(1)]] float4 colour;

	//Read only by the strips of wide lines (g_mode.y): the line's other end in xyz, and which side / which end / how many pixels wide in xyz of the second.
	[[vk::location(3)]] float4 other;
	[[vk::location(4)]] float4 strip;
};

struct V2F
{
	float4 position : SV_Position;
	float4 colour : COLOR0;
};

[[vk::push_constant]]
cbuffer Push
{
	//What the matrix is depends on g_mode.x below: a finished world-to-clip on every ordinary
	//draw, and the object's world matrix in a pass that draws both of a headset's eyes at once.
	row_major float4x4 g_matrix;
	float4 g_tint;

	//x: use the frame's eye view (stereo); y: wide-line strips; zw: window size. After tint for old bakes
	float4 g_mode;
};

//<summary>A point into clip space, the way every vertex here is put there.</summary>
float4 Place( float3 position )
{
	float4 placed = mul( float4( position, 1.0 ), g_matrix );
	return g_mode.x != 0.0 ? EyeClip( placed ) : placed;
}

//One corner of a wide line's strip, pushed across by half the width in screen pixels
float4 WideCorner( VIn i )
{
	float4 here = Place( i.position );
	float4 there = Place( i.other.xyz );

	//An end behind the eye has no place on the screen. Cut the line where it crosses in front of the eye
	//instead; both ends behind, and the strip is nothing - all six of its corners land on the same point.
	const float nearest = 1e-4;

	if ( here.w < nearest && there.w < nearest ) return float4( 2.0, 2.0, 2.0, 1.0 );
	if ( here.w < nearest ) here = lerp( here, there, ( nearest - here.w ) / ( there.w - here.w ) );
	if ( there.w < nearest ) there = lerp( there, here, ( nearest - there.w ) / ( here.w - there.w ) );

	float2 pixels = g_mode.zw;
	float2 along = ( there.xy / there.w - here.xy / here.w ) * pixels;
	along = dot( along, along ) > 1e-8 ? normalize( along ) : float2( 1.0, 0.0 );

	//The same way along the line at both ends, so the sides agree.
	along *= i.strip.y;

	float2 across = float2( -along.y, along.x ) * i.strip.x * i.strip.z * 0.5;
	here.xy += across * 2.0 / pixels * here.w;
	return here;
}

[shader("vertex")]
V2F MainVs( VIn i )
{
	V2F o;

	//One matrix, or the object's and then the eye's - see g_mode above. A branch on a push constant, which is one scalar test for the whole draw.
	o.position = g_mode.y != 0.0 ? WideCorner( i ) : Place( i.position );
	//Vertex colour is authored sRGB, so decode it; g_tint stays as is since it may be a plain multiplier
	o.colour = float4( ToLinear( i.colour.rgb ) * g_tint.rgb, i.colour.a * g_tint.a );
	return o;
}

[shader("fragment")]
float4 MainPs( V2F i ) : SV_Target
{
	return i.colour;
}
