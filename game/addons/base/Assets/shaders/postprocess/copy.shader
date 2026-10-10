//Copies the picture back when a post chain with an odd effect count ends in the wrong buffer

FEATURES
{
}

PARAMETERS
{
}

#include "postprocess.slang"

[shader("fragment")]
float4 MainPs( PostV2F i ) : SV_Target0
{
	return float4( SceneColour( i.uv ), 1.0 );
}
