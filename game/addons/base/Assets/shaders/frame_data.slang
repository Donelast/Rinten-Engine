//Frame block (set 1, binding 1): camera, lights, sky, shadows; CoreSceneRenderer fills it

#ifndef ENGINE_FRAME_DATA
#define ENGINE_FRAME_DATA

struct Light
{
	float4 position;  //xyz, range w (unused on the sun)
	float4 colour;    //rgb already multiplied by brightness, kind w (0 sun, 1 point, 2 spot)
	float4 direction; //xyz, cosine of the inner cone w
	float4 cone;      //outer cosine x, how wide the source is y, spare zw
	float4 shape;     //the power distance is raised to x, which faces read the lamps' movers' map y (a bit each), spare zw
};

//Envmap probe: an octahedral 2D picture of all directions with blurred mips stacked below
struct Probe
{
	float4 where; //xyz where it stands, w which picture in the table
	float4 mins;  //xyz the near corner of its box, w one if the box is projected
	float4 maxs;  //xyz the far corner, w how far from the edge it fades out
	float4 tint;  //rgb, strength a
};

struct FogVolume
{
	float4 centre;   //xyz where it stands, strength w
	float4 extents;  //xyz half its size along its own axes, how its edge fades w
	float4 rotation; //the quaternion that turns its own axes into the world's
	float4 colour;   //rgb what it scatters, spare a
};

struct FrameData
{
	row_major float4x4 worldToClip;
	float4 camera; //xyz where the eye is, light count w
	Light lights[16];
	float4 settings; //occlusion strength, window width, window height, the sky table's index in the picture table
	row_major float4x4 clipToWorld;
	float4 zenith;
	float4 horizon;
	float4 ground;
	float4 sunDirection; //xyz, enabled w
	float4 sunColour;    //rgb, disc cosine a
	float4 viewInfo;     //which debug view x, the occlusion picture's place in the table y, the depth picture's z, the texture LOD bias w

	//Where the camera was looking the frame before. A pixel goes back through this to find where it was on the screen last time.
	row_major float4x4 previousWorldToClip;

	//The gradient fog, which is a property of the air rather than of any material: rgb and the most of the view it may take,
	//the two distances and two heights it runs between, and the exponents that shape both falloffs. fogShape.z below one is off.
	float4 fogColour;
	float4 fogRange;  //start distance, end distance, start height, end height
	float4 fogShape;  //distance exponent, height exponent, enabled, colour from the sky

	//Baked lighting: lightmap sheet for static surfaces, probe volume for moving ones; zero = no bake
	float4 bakedMaps;    //the sheet's place in the picture table x, its directions' y, spare z, one where there is a sheet w
	float4 bakedVolume;  //the three pictures' places xyz, one where there is a volume w
	float4 volumeOrigin; //xyz where the lattice's first point stands, w one over the step between points
	float4 volumeCount;  //how many points along x, y and z, spare w
	float4 bakedMask;    //the sheet's mask's place x, the volume's y, one where the sheet has one z, one where there is a mask w
	float4 bakedSun;     //which channel of the mask holds the sun's shadow, plus one - nought for none x, spare yzw
	float4 bakedSpare[4];

	//The envmap probes. probeInfo.x is how many of the four are filled in, and nothing behind it is worth reading when it is zero.
	float4 probeInfo; //how many, how wide one picture is, how many bands, RGBM range
	Probe probes[4];

	//Two clocks: wall time always runs; scene time pauses and scales with the scene
	float4 time; //seconds since the engine started, the frame's delta, the scene's own seconds, spare

	//The sun, already put through the air it arrives across, and how far into the night that leaves the frame. Both depend on the sun's height and on
	//nothing else - see CoreSceneRenderer.SetSky, which works them out once a frame because every lit pixel in the world used to work them out again.
	float4 sunLight; //rgb what is left of the sun's own light, a how far into night

	//Clip to world ray without the camera position, used by SkyRay; all zero means use clipToWorld
	row_major float4x4 rayToWorld;

	//Sun shadow pages: world to sun frame, page levels and depth range, eye position, map slots
	row_major float4x4 sunToPages;
	float4 sunPages;
	float4 sunEye;
	float4 sunMaps;
	float4 moverZone;

	//The scene's flat ambient light - its AmbientLight components and its directional lights' sky colours,
	//added - and in a how much of what the sky and the probes give it replaces; see SceneAmbient in frame.slang.
	float4 ambient;

	//The exposure and which tonemap curve: the exposure for everything that takes the frame's light to the screen's - the compose,
	//the curve's point effect - and the curve for a pass that finishes the picture itself; see TARGET_SCREEN in frame.slang.
	float4 finish; //exposure x, curve y, where a run of point effects the world's shading carries starts in effects z, spare w

	//The numbers of the point effects laid over this frame - see CorePointChain. Each chain
	//of them reads its own run, from the slot its push block names; nothing else reads these.
	float4 effects[128];

	//The nearest eight fog boxes around the camera; fogVolumeInfo.x is how many are set
	FogVolume fogVolumes[8];
	float4 fogVolumeInfo;

	//The sheet of slices this view's air was worked out into: where the picture sits in the table, how many slices it holds, and the two distances they
	//are laid out between - see CoreVolumeFog and FogVolumeSheet in frame.slang. A nought in x is "no sheet", and then a surface marches the air itself.
	float4 fogVolumeSheet;

	//The sun's maps read as depth, for the search for what stands in front of a point that softens the sun's edge: the pages' place in the table and the
	//map of what moves - nought where a map is not read so - see SunSoft. z and w the lamps' map of what moves, compared and as depth - see LampShadow.
	float4 sunDepth;

	//What water and glass trace their reflections through, once the solid world is shaded: the chain of the nearest depth and the
	//picture of that world, by their places in the table - nought where no reflections were drawn this frame. See ScreenReflected.
	float4 screenTrace;
};

#ifdef WEBGL
//WebGL has no storage buffers: a uniform block array of one, every member float4-wide
#ifdef STEREO
//Both of a headset's eyes in one pass: two blocks, the second the second eye's. Only what depends on the eye
//is read at g_frame[Eye()]; everything else stays at g_frame[0], which is the same for both - see Eye() below.
[[vk::binding(1,1)]] ConstantBuffer<FrameData> g_frame[2];
#else
[[vk::binding(1,1)]] ConstantBuffer<FrameData> g_frame[1];
#endif
#else
[[vk::binding(1,1)]] StructuredBuffer<FrameData> g_frame;
#endif

//Reads a field from this eye's block; WebGL only allows constant indices into block arrays
#if defined( STEREO )
#define EYE_FRAME( field ) ( Eye() == 0 ? g_frame[0].field : g_frame[1].field )
#else
#define EYE_FRAME( field ) g_frame[Eye()].field
#endif

//Headset eye being drawn, 0 otherwise; per-eye data reads g_frame[Eye()], the rest g_frame[0]
#ifdef STEREO
//<summary> The view a pixel is being drawn for, as the vertex stage handed it over. </summary> <remarks> A global set at the entry, the way InstanceNumbers sets
//the instance's - and for the same reason: the shading reads it deep inside, where the interpolants are not in scope. STEREO_EYE_IN( i ) is what sets it. </remarks>
static uint g_eyeId;

//<summary> Which view a vertex is being drawn for. The vertex stage only: this is SPIR-V's ViewIndex, which SPIRV-Cross writes out as gl_ViewID_OVR, and it will
//only do that for a vertex stage - "OVR_multiview2 can only be used with Vertex shaders", which is why the pixel stage is told rather than asking. </summary>
uint ViewOfThisVertex()
{
	__stage_switch
	{
	case vertex:
		return spirv_asm
		{
			OpCapability MultiView;
			OpExtension "SPV_KHR_multiview";
			result:$$uint = OpLoad builtin(ViewIndex:uint);
		};
	default:
		return 0;
	}
}
#endif

uint Eye()
{
#if defined( STEREO )
	//Asked where it can be asked, and taken from the vertex stage where it cannot - see ViewOfThisVertex and g_eyeId above.
	__stage_switch
	{
	case vertex:
		return ViewOfThisVertex();
	case fragment:
		return g_eyeId;
	default:
		return 0;
	}
#elif defined( WEBGPU ) || defined( WEBGL )
	return 0;
#else
	__stage_switch
	{
	case vertex:
	case fragment:
		return spirv_asm
		{
			OpCapability MultiView;
			OpExtension "SPV_KHR_multiview";
			result:$$uint = OpLoad builtin(ViewIndex:uint);
		};
	default:
		return 0;
	}
#endif
}

//World position to this eye's clip space, for passes whose push block holds only a world matrix
float4 EyeClip( float4 world )
{
	return mul( world, EYE_FRAME( worldToClip ) );
}

#endif
