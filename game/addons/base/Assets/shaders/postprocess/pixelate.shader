//Blocks, as a point effect that moves where the frame is read rather than what a pixel becomes - see Pixelate,
//and Where in CorePointChain. The edge filter is left off a frame with this on: the blocks are the point.

FEATURES
{
}

PARAMETERS
{
	//How many pixels of the picture a block is across. One is the picture as it is.
	float g_flBlock < Attribute( "Block" ); Default( 1.0 ); >;
}

#include "postprocess.slang"

float2 Where( float2 uv )
{
	if ( g_flBlock <= 1.0 ) return uv;

	float2 pixels = uv * g_vFrameSize.xy;
	float2 at = ( floor( pixels / g_flBlock ) + 0.5 ) * g_flBlock;

	return at / max( g_vFrameSize.xy, 1.0 );
}
