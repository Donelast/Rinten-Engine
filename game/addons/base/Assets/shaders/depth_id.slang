//First V2F interpolants (cutout UV, instance id, stereo eye); implicit locations keep them at 0 and 1
#ifdef STEREO
#define DEPTH_ID_FIELD float2 depthUv : TEXCOORD14; nointerpolation uint depthId : TEXCOORD15; nointerpolation uint eyeId : TEXCOORD16;
#define DEPTH_ID_SET( o, uv ) o.depthUv = uv; o.depthId = 0; o.eyeId = ViewOfThisVertex()

//Published at a pixel entry, before anything of the surface runs.
#define STEREO_EYE_IN( i ) g_eyeId = i.eyeId
#else
#define DEPTH_ID_FIELD float2 depthUv : TEXCOORD14; nointerpolation uint depthId : TEXCOORD15;
#define DEPTH_ID_SET( o, uv ) o.depthUv = uv; o.depthId = 0

#define STEREO_EYE_IN( i )
#endif
