//A compute pass over the frame: it runs inside one of a camera's stages, reads the frame's pictures by their place in the
//table and stores into a picture a command list made. See postprocess.slang, ComputeShader and CommandList.DispatchCompute.
//    var result = cl.GetRenderTarget( "Result", ImageFormat.RGBA16161616F );
//    cl.Attributes.Set( "Result", result.ColorTexture );
//    cl.DispatchCompute( new ComputeShader( "shaders/my.shader" ), result.Size );

FEATURES
{
}

PARAMETERS
{
	//Where it stores, in the format of the picture it is handed.
	RWTexture2D g_tResult < Attribute( "Result" ); Format( "rgba16f" ); >;
}

#include "postprocess.slang"

[shader("compute")]
[numthreads( 8, 8, 1 )]
void MainCs( uint3 id : SV_DispatchThreadID )
{
	g_tResult[id.xy] = float4( float( id.x & id.y ), ( id.x & 15 ) / 15.0, ( id.y & 15 ) / 15.0, 0.0 );
}
