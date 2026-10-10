using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Rinten;

//Browser graphics setup over WebGPU or WebGL2 (see BrowserNative), shaped like the Vulkan side
internal static class BrowserDevice
{
	//-- what the WebGPU flags are, as numbers ----------------------------------- GPUBufferUsage, GPUTextureUsage and GPUShaderStage are bit
	//fields in the browser and plain numbers over the seam. Written down here rather than in each caller so a wrong bit is wrong in one place.

	internal static class BufferUse
	{
		internal const int CopySrc = 4;
		internal const int CopyDst = 8;
		internal const int Index = 16;
		internal const int Vertex = 32;
		internal const int Uniform = 64;
		internal const int Storage = 128;
	}

	internal static class TextureUse
	{
		internal const int CopySrc = 1;
		internal const int CopyDst = 2;
		internal const int TextureBinding = 4;
		internal const int RenderAttachment = 16;
	}

	private static class Stage
	{
		internal const int Vertex = 1;
		internal const int Fragment = 2;
		internal const int VertexFragment = Vertex | Fragment;
	}

	//-- the formats this backend names ------------------------------------------

	internal const string FormatRgba8 = "rgba8unorm";
	internal const string FormatRgba8Srgb = "rgba8unorm-srgb";
	internal const string FormatRgba16Float = "rgba16float";
	internal const string FormatDepth32 = "depth32float";

	internal const string AspectAll = "all";
	internal const string AspectDepth = "depth-only";

	//-- the sizes the whole backend is cut to -----------------------------------

	internal const int PushConstantSize = 128;
	internal const int PushAlign = 256;
	internal const int PushSlotsPerFrame = 4096;
	internal const int MaxBones = 128;
	internal const int BoneBlockSize = MaxBones * 64;

	//How many skinned poses one frame holds - each a whole block, bound at its own offset - and runs
	//of sprites, which ride in the same slots. A frame that runs out draws the rest in no pose at all.
	internal const int BoneSlotsPerFrame = 256;
	internal const int MaxLights = 16;

	//Frame block size, taken from CoreSceneRenderer's layout so it always matches the shaders
	internal const int FrameBlockSize = CoreSceneRenderer.FrameFloats * sizeof( float );

	internal const int MaterialTextures = 4;

	//Where the push ring and the two samplers sit in the groups below. The shader translators say the same -
	//see CoreShaderCompiler.Wgsl and .Glsl - since neither WGSL nor GLSL from Slang puts them there on its own.
	internal const int PushBinding = 6;
	internal const int MaterialSamplerBinding = 5;
	internal const int ShadowSamplerBinding = 3;

	//Binding of the plain filtering sampler in the frame group; see OneSamplerAGroup
	internal const int FilterSamplerBinding = 4;
	//A material's numbers: every slot a shader's PARAMETERS block can ask for - see CoreRenderDevice.MaterialParamSlots.
	internal const int MaterialParamSize = CoreRenderDevice.MaterialParamFloats * sizeof( float );
	internal const int FramesInFlight = 2;
	internal const int DynamicScratch = 4 * 1024 * 1024;

	//Which set of pictures a pass draws into. Matches SBC_TARGET_*.
	internal static class Target
	{
		internal const int Screen = 0;
		internal const int Scene = 1;
		internal const int Depth = 4;
	}

	//-- the device ---------------------------------------------------------------

	internal static bool validation;

	internal static int sampler;
	internal static int clampSampler;
	internal static int pointSampler;
	internal static int shadowSampler;

	internal static int textureSetLayout;
	internal static int boneSetLayout;
	internal static int objectSetLayout;

	internal static int shadowImage;
	internal static int shadowView;
	//The sky's table and the picture it lives in - see CoreSkyTable.
	internal static int skyTableImage;
	internal static int skyTableView;

	//How many texels across it is - see CoreSkyTable.Side, which fills it.
	internal const int SkyTableSide = 64;

	//Binding for the second eye's frame block in stereo passes; must match webgl2.js SecondFrameBinding
	internal const int SecondFrameBinding = 6;

	internal static int defaultParams;
	internal static int pushRing;
	internal static int defaultMaterial;
	internal static int defaultBones;
	internal static int defaultObject;

	//What the two default groups name. Kept for as long as the groups are: a released buffer is a destroyed one in WebGPU, and a
	//group naming one fails every submit it is bound in - which was every frame, since every draw binds the default object group.
	private static int defaultBonesBuffer;
	private static int defaultFrameBuffer;
	private static int defaultObjectBuffer;

	//What the world is finished into: eight bits a channel with the curve, as a desktop's window is - the device encodes the light the shaders write,
	//which is what every screen stage is written for. It went into the canvas's own format before, which has no curve, and the whole picture was dark.
	internal const string ScreenFormat = FormatRgba8Srgb;

	//The canvas's format: the same bytes without the curve, which WebGPU copies the finished picture into as they are. Every browser offers it.
	internal const string CanvasFormat = FormatRgba8;

	//The language this device's shaders are in: WGSL on WebGPU, GLSL where the page had only WebGL2 - see webgl2.js.
	//Everything above the handle table is the same either way; only the words handed to a shader module differ.
	internal static CoreShaderCompiler.Format ShaderFormat { get; private set; } = CoreShaderCompiler.Format.Wgsl;
	internal static int maxImageSize = 8192;

	//Whether the device reads the BC formats a compiled texture is stored in. A desktop's
	//browser does; a phone's does not, and gets them unpacked - see BrowserTextures.CreateMips.
	internal static bool BlockCompression;

	//Whether the device reads ASTC's four-by-four blocks - a headset's or a phone's - and ETC2's, which every WebGL2 browser
	//on a phone has. What a web build's pictures are turned into where there is no BC: see BrowserTextures.CreateUniversal.
	internal static bool AstcCompression;
	internal static bool Etc2Compression;

	internal static string infoText = "not initialised";
	internal static string deviceNameText = "";

	internal static long trackedBufferBytes;
	internal static long trackedTextureBytes;
	internal static int trackedBufferCount;
	internal static int trackedTextureCount;

	//Whether the device came up and has not been taken down again.
	internal static bool Ready => ready;

	private static bool ready;

	//Whether the browser has told the page its device is gone. Asked of the page once a frame and read as a field after that. Every draw looks at
	//this, and a call into JavaScript per draw to learn something that can only change between frames would be the most expensive thing on the path.
	internal static bool Lost => lost;

	private static bool lost;

	//Asks the page whether the device is still there. Once a frame, at its start.
	internal static void RefreshLost()
	{
		if ( ready ) lost = BrowserNative.Lost() != 0;
	}

	//-- making things ------------------------------------------------------------

	//A buffer with bytes already in it, or an empty one where the span is empty. The size is rounded up to four, because WebGPU maps only whole words
	//at creation; the tail of a mapped range is fresh and therefore zero, which is what the C the browser renderer used to be wrote there by hand.
	internal static int MakeBuffer( Span<byte> data, int usage )
	{
		if ( data.Length == 0 ) return 0;

		return BrowserNative.CreateBufferWithData( data, usage, null );
	}

	//A buffer of a size with nothing in it yet.
	internal static int MakeBuffer( long size, int usage )
	{
		if ( size <= 0 ) return 0;

		return BrowserNative.CreateBuffer( ( size + 3 ) & ~3L, usage, null );
	}

	internal static int MakeTexture( int width, int height, string format, int usage, int mips = 1, int layers = 1,
		int samples = 1 )
	{
		if ( width <= 0 || height <= 0 ) return 0;

		return BrowserNative.CreateTexture( width, height, mips <= 0 ? 1 : mips, format, usage, null,
			layers <= 0 ? 1 : layers, samples <= 1 ? 1 : samples );
	}

	internal static int MakeView( int texture, string format, string aspect = AspectAll )
	{
		if ( texture == 0 ) return 0;

		return BrowserNative.CreateView( texture, format, aspect, 0, 0 );
	}

	//What one entry of a bind group names, and by which kind of handle.
	internal readonly struct Bind
	{
		private readonly int binding;
		private readonly string kind;
		private readonly int handle;
		private readonly long size;

		private Bind( int binding, string kind, int handle, long size )
		{
			this.binding = binding;
			this.kind = kind;
			this.handle = handle;
			this.size = size;
		}

		internal static Bind Buffer( int binding, int buffer, long size ) => new( binding, "buffer", buffer, size );
		internal static Bind View( int binding, int view ) => new( binding, "textureView", view, 0 );
		internal static Bind Sampler( int binding, int sampler ) => new( binding, "sampler", sampler, 0 );

		internal void Write( StringBuilder json )
		{
			json.Append( "{\"binding\":" ).Append( binding ).Append( ",\"" ).Append( kind ).Append( "\":" )
				.Append( handle );
			if ( size > 0 ) json.Append( ",\"size\":" ).Append( size );
			json.Append( '}' );
		}
	}

	//A bind group naming resources by handle. Made when a texture, a material or a frame's
	//scratch is made and kept for as long as they are - nothing on the per-draw path builds one.
	internal static int MakeBindGroup( int layout, params Bind[] entries )
	{
		if ( layout == 0 ) return 0;

		var json = new StringBuilder( 256 );
		json.Append( "{\"layout\":" ).Append( layout ).Append( ",\"entries\":[" );

		for ( var i = 0; i < entries.Length; i++ )
		{
			if ( i > 0 ) json.Append( ',' );
			entries[i].Write( json );
		}

		json.Append( "]}" );
		return BrowserNative.CreateBindGroup( json.ToString() );
	}

	private static int MakeSampler( string address, string filter, bool compare )
	{
		var json = new StringBuilder( 192 );
		json.Append( "{\"addressModeU\":\"" ).Append( address )
			.Append( "\",\"addressModeV\":\"" ).Append( address )
			.Append( "\",\"addressModeW\":\"" ).Append( address )
			.Append( "\",\"maxAnisotropy\":1" );

		if ( compare )
		{
			//A comparison sampler is asked a question, not filtered: the browser refuses
			//a linear filter on one, and the shadow lookup does its own spreading anyway.
			json.Append( ",\"compare\":\"less-equal\",\"magFilter\":\"nearest\",\"minFilter\":\"nearest\"" )
				.Append( ",\"mipmapFilter\":\"nearest\"" );
		}
		else
		{
			json.Append( ",\"magFilter\":\"" ).Append( filter )
				.Append( "\",\"minFilter\":\"" ).Append( filter )
				.Append( "\",\"mipmapFilter\":\"linear\"" );
		}

		json.Append( '}' );
		return BrowserNative.CreateSampler( json.ToString() );
	}

	//-- bringing it up -----------------------------------------------------------

	//Group 0 entries, all visible to the vertex stage too, since foliage reads material data there
	private static readonly string TextureSetJson = $$$"""
		{"entries":[
		{"binding":0,"visibility":{{{Stage.VertexFragment}}},"texture":{"sampleType":"float","viewDimension":"2d"}},
		{"binding":1,"visibility":{{{Stage.VertexFragment}}},"texture":{"sampleType":"float","viewDimension":"2d"}},
		{"binding":2,"visibility":{{{Stage.VertexFragment}}},"texture":{"sampleType":"float","viewDimension":"2d"}},
		{"binding":3,"visibility":{{{Stage.VertexFragment}}},"texture":{"sampleType":"float","viewDimension":"2d"}},
		{"binding":4,"visibility":{{{Stage.VertexFragment}}},"buffer":{"type":"uniform","minBindingSize":{{{MaterialParamSize}}}}},
		{"binding":{{{MaterialSamplerBinding}}},"visibility":{{{Stage.VertexFragment}}},"sampler":{"type":"filtering"}},
		{"binding":{{{PushBinding}}},"visibility":{{{Stage.VertexFragment}}},"buffer":{"type":"uniform","minBindingSize":{{{PushConstantSize}}},"hasDynamicOffset":true}}
		]}
		""";

	//Group 1: the bones - or a run of sprites, read by the pixel too - the frame block, the sun's map and the sampler it is compared
	//through, a filtering sampler, and the sky's table. The push ring is group 0 because it is the one thing bound again between draws.
	private static readonly string BoneSetJson = $$$"""
		{"entries":[
		{"binding":0,"visibility":{{{Stage.VertexFragment}}},"buffer":{"type":"read-only-storage","minBindingSize":{{{BoneBlockSize}}},"hasDynamicOffset":true}},
		{"binding":1,"visibility":{{{Stage.VertexFragment}}},"buffer":{"type":"read-only-storage","minBindingSize":{{{FrameBlockSize}}}}},
		{"binding":2,"visibility":{{{Stage.Fragment}}},"texture":{"sampleType":"depth","viewDimension":"2d"}},
		{"binding":{{{ShadowSamplerBinding}}},"visibility":{{{Stage.Fragment}}},"sampler":{"type":"comparison"}},
		{"binding":{{{FilterSamplerBinding}}},"visibility":{{{Stage.Fragment}}},"sampler":{"type":"filtering"}},
		{"binding":5,"visibility":{{{Stage.Fragment}}},"texture":{"sampleType":"float","viewDimension":"2d"}},
		{"binding":{{{SecondFrameBinding}}},"visibility":{{{Stage.VertexFragment}}},"buffer":{"type":"read-only-storage","minBindingSize":{{{FrameBlockSize}}}}}
		]}
		""";

	//Group 2: an object's own two buffers, whatever they hold. No minimum size: a chunk of a world and a palette of instances are the same binding here.
	private static readonly string ObjectSetJson = $$$"""
		{"entries":[
		{"binding":0,"visibility":{{{Stage.VertexFragment}}},"buffer":{"type":"read-only-storage"}},
		{"binding":1,"visibility":{{{Stage.VertexFragment}}},"buffer":{"type":"read-only-storage"}}
		]}
		""";

	private static bool CreateLayouts()
	{
		textureSetLayout = BrowserNative.CreateBindGroupLayout( TextureSetJson );
		boneSetLayout = BrowserNative.CreateBindGroupLayout( BoneSetJson );
		objectSetLayout = BrowserNative.CreateBindGroupLayout( ObjectSetJson );

		return textureSetLayout != 0 && boneSetLayout != 0 && objectSetLayout != 0;
	}

	private static bool CreateShadows()
	{
		const int usage = TextureUse.RenderAttachment | TextureUse.TextureBinding;

		//A page draws the portable frame, which has no shadow maps - see RenderTier. The depth one fills the
		//layout's slot and is never drawn into; it was once four thousand texels a side, 128 MB nothing read.
		shadowImage = MakeTexture( 1, 1, FormatDepth32, usage );

		//The sky's table, cleared - its alpha is nought until CoreSkyTable sends one, which is what says the frame has none. See SkyTableAt in frame.slang.
		skyTableImage = MakeTexture( SkyTableSide, SkyTableSide, FormatRgba16Float,
			TextureUse.TextureBinding | TextureUse.CopyDst );

		if ( shadowImage == 0 || skyTableImage == 0 ) return false;

		shadowView = MakeView( shadowImage, FormatDepth32, AspectDepth );
		skyTableView = MakeView( skyTableImage, FormatRgba16Float );

		var empty = new byte[SkyTableSide * SkyTableSide * 8];
		BrowserNative.WriteTexture( skyTableImage, 0, 0, 0, SkyTableSide, SkyTableSide, SkyTableSide * 8, empty );

		return shadowView != 0 && skyTableView != 0;
	}

	private static bool CreateDefaults()
	{
		Span<float> parameters = stackalloc float[CoreRenderDevice.MaterialParamFloats];
		parameters.Clear();
		parameters[0] = 0.5f;
		parameters[2] = 1.0f;
		defaultParams = MakeBuffer( MemoryMarshal.AsBytes( parameters ), BufferUse.Uniform );
		pushRing = MakeBuffer( (long)PushAlign * PushSlotsPerFrame * FramesInFlight,
			BufferUse.Uniform | BufferUse.CopyDst );

		var identity = new byte[BoneBlockSize];
		defaultBonesBuffer = MakeBuffer( identity, BufferUse.Storage );
		defaultFrameBuffer = MakeBuffer( FrameBlockSize, BufferUse.Storage | BufferUse.CopyDst );

		if ( defaultParams == 0 || pushRing == 0 || defaultBonesBuffer == 0 || defaultFrameBuffer == 0 ) return false;

		defaultBones = MakeBindGroup( boneSetLayout,
			Bind.Buffer( 0, defaultBonesBuffer, BoneBlockSize ),
			Bind.Buffer( 1, defaultFrameBuffer, FrameBlockSize ),
			Bind.View( 2, shadowView ),
			Bind.Sampler( ShadowSamplerBinding, shadowSampler ),
			Bind.Sampler( FilterSamplerBinding, clampSampler ),
			Bind.View( 5, skyTableView ),

			//Every entry the layout has, the second eye's block included - with six the page
			//refused the group on every start ("did not match the expected number of entries").
			Bind.Buffer( SecondFrameBinding, defaultFrameBuffer, FrameBlockSize ) );


		Span<byte> zeros = stackalloc byte[64];
		zeros.Clear();
		defaultObjectBuffer = MakeBuffer( zeros, BufferUse.Storage );
		if ( defaultObjectBuffer == 0 ) return false;

		defaultObject = MakeBindGroup( objectSetLayout,
			Bind.Buffer( 0, defaultObjectBuffer, 64 ),
			Bind.Buffer( 1, defaultObjectBuffer, 64 ) );

		return defaultBones != 0 && defaultObject != 0;
	}

	private static void ReleaseTexture( ref int texture, ref int view )
	{
		if ( view != 0 ) BrowserNative.Release( view );
		if ( texture != 0 ) BrowserNative.Release( texture );
		view = 0;
		texture = 0;
	}

	private static void TearDown()
	{
		BrowserSwapchain.DestroySwapchain();
		BrowserPipelines.DestroyPipelines();
		BrowserBuffers.DestroyObjectSets();
		BrowserTextures.DestroyTextures();
		BrowserBuffers.DestroyBuffers();

		if ( defaultBones != 0 ) BrowserNative.Release( defaultBones );
		if ( defaultObject != 0 ) BrowserNative.Release( defaultObject );
		if ( defaultMaterial != 0 ) BrowserNative.Release( defaultMaterial );
		if ( defaultParams != 0 ) BrowserNative.Release( defaultParams );
		if ( pushRing != 0 ) BrowserNative.Release( pushRing );
		if ( defaultBonesBuffer != 0 ) BrowserNative.Release( defaultBonesBuffer );
		if ( defaultFrameBuffer != 0 ) BrowserNative.Release( defaultFrameBuffer );
		if ( defaultObjectBuffer != 0 ) BrowserNative.Release( defaultObjectBuffer );
		defaultBones = defaultObject = defaultMaterial = defaultParams = pushRing = 0;
		defaultBonesBuffer = defaultFrameBuffer = defaultObjectBuffer = 0;

		ReleaseTexture( ref shadowImage, ref shadowView );
		ReleaseTexture( ref skyTableImage, ref skyTableView );

		//The sky's table was in that picture - see CoreSkyTable, which sends it again rather than leaving the next device with a cleared one.
		CoreSkyTable.Forget();

		if ( textureSetLayout != 0 ) BrowserNative.Release( textureSetLayout );
		if ( boneSetLayout != 0 ) BrowserNative.Release( boneSetLayout );
		if ( objectSetLayout != 0 ) BrowserNative.Release( objectSetLayout );
		textureSetLayout = boneSetLayout = objectSetLayout = 0;

		if ( sampler != 0 ) BrowserNative.Release( sampler );
		if ( clampSampler != 0 ) BrowserNative.Release( clampSampler );
		if ( pointSampler != 0 ) BrowserNative.Release( pointSampler );
		if ( shadowSampler != 0 ) BrowserNative.Release( shadowSampler );
		sampler = clampSampler = pointSampler = shadowSampler = 0;

		BlockCompression = false;
		AstcCompression = false;
		Etc2Compression = false;
		validation = false;
		ready = false;
		lost = false;
	}

	//Async device and adapter setup, awaited once by the launcher before the engine starts
	internal static async Task<bool> Init( int enableValidation )
	{
		if ( ready ) return true;

		if ( await BrowserNative.Init( enableValidation ) == 0 )
		{
			infoText = BrowserNative.Info();
			TearDown();
			return false;
		}

		validation = enableValidation != 0;
		deviceNameText = BrowserNative.DeviceName();
		ShaderFormat = BrowserNative.Language() == "glsl" ? CoreShaderCompiler.Format.Glsl : CoreShaderCompiler.Format.Wgsl;
		BlockCompression = BrowserNative.HasFeature( "texture-compression-bc" ) != 0;
		AstcCompression = BrowserNative.HasFeature( "texture-compression-astc" ) != 0;
		Etc2Compression = BrowserNative.HasFeature( "texture-compression-etc2" ) != 0;

		var largest = (int)BrowserNative.Limit( "maxTextureDimension2D" );
		if ( largest > 0 ) maxImageSize = largest;

		sampler = MakeSampler( "repeat", "linear", false );
		clampSampler = MakeSampler( "clamp-to-edge", "linear", false );
		pointSampler = MakeSampler( "clamp-to-edge", "nearest", false );
		shadowSampler = MakeSampler( "clamp-to-edge", "linear", true );

		if ( !CreateLayouts() || !CreateShadows() || !CreateDefaults() )
		{
			infoText = "WebGPU layouts or shadow maps would not be made";
			TearDown();
			return false;
		}

		infoText = BrowserNative.Info();
		ready = true;
		return true;
	}

	internal static void Shutdown()
	{
		TearDown();
		infoText = "not initialised";
		deviceNameText = "";
	}

	//What the device is, for the seam's sbc_render_device_info : one device, no
	//second queue, no bindless, and whether errors are being reported to the console.
	internal static unsafe void DeviceInfo( int* outInfo )
	{
		if ( outInfo is null ) return;

		outInfo[0] = 1;
		outInfo[1] = 0;
		outInfo[2] = 0;
		outInfo[3] = validation ? 1 : 0;
	}
}
