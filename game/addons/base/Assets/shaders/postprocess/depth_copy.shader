//The world's depth, as the pre-pass wrote it, into a picture of its own - see DepthPicture. Nought at the near plane, one at the far,
//one where nothing was drawn; a surface reads it back through DepthBehind in frame.slang and turns it into the point behind itself.

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
	return float4( SceneDepthRaw( i.uv ), 0.0, 0.0, 1.0 );
}
