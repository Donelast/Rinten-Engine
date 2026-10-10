using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkImage = Silk.NET.Vulkan.Image;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Rinten;

//Shared renderer state: instance, device, queue, samplers, layouts, pools and shader constants
//The constants must match what the shaders compile against
internal static unsafe partial class VkDevice
{
	//-- the numbers the shaders share --------------------------------------


	/// <summary>Bindless table size: asks for Wanted, takes what the device allows; never below Least (shader clamp)</summary>
	public const uint BindlessSlotsWanted = 16384;
	public const uint BindlessSlotsLeast = 4096;

	/// <summary>What every pipeline's depth attachment is. One format for the whole engine: 32-bit float depth exists everywhere that matters, and one format means pipelines and the swapchain's depth image can never disagree.</summary>
	public const Format DepthFormat = Format.D32Sfloat;

	/// <summary>HDR scene format; half floats so light can go past 1 before tonemapping</summary>
	public const Format SceneFormat = Format.R16G16B16A16Sfloat;

	/// <summary>World and post chain format: E5B9G9R9 where the device supports it fully, else SceneFormat. No alpha</summary>
	public static Format WorldFormat { get; private set; } = Format.R16G16B16A16Sfloat;

	/// <summary>The normals and how rough they are, kept alongside the colour so the passes that come after the world can ask about a surface they did not see drawn: xyz the normal in view space, w the roughness.</summary>
	public const Format NormalFormat = Format.R8G8B8A8Unorm;

	/// <summary>Velocity: xy = screen motion, z = writer's depth, w = written flag (zero motion is still valid)</summary>
	public const Format VelocityFormat = Format.R16G16B16A16Sfloat;

	/// <summary>The finished picture, at the window's size, for the passes that run after the compose.</summary>
	public const Format FinalFormat = Format.A2B10G10R10UnormPack32;

	/// <summary>Half-size pass format: small floats for faster filtering; falls back to SceneFormat if unsupported</summary>
	public static Format CompactFormat { get; private set; } = SceneFormat;

	//The same format as a compute shader names a storage image of it - see CoreShaderParams.DeclareOutputs.
	internal static string CompactStorageName => CompactFormat == Format.B10G11R11UfloatPack32 ? "r11f_g11f_b10f" : "rgba16f";

	/// <summary>Every pipeline carries this much push constant space, for both stages. 128 bytes is the guaranteed minimum everywhere - a matrix and a bit.</summary>
	public const uint PushConstantSize = 128;

	/// <summary>Max bones per skinned draw; fixed because a descriptor range can slide but not resize</summary>
	public const uint MaxBones = 128;
	public const uint BoneBlockSize = MaxBones * 64;

	/// <summary>How many blend shapes one object can be pushed by at once. A face carries more shapes than this on paper - a full FACS set is hundreds - but what is written per object is one weight per shape the model has, and a model with more than this many is one nobody has.</summary>
	public const uint MaxMorphs = 256;

	/// <summary>One object's worth of morph weights in the frame's scratch, kept to the same sixty-four byte step the bone block uses so either can follow either without a storage buffer landing off its alignment.</summary>
	public const uint MorphBlockSize = MaxMorphs * 4;

	/// <summary>Engine vertex stride; must match EngineVertexBytes in vertex_frame.slang</summary>
	public const uint EngineVertexStride = PackedVertex.Size;

	/// <summary>The side buffers that ride beside it, a vertex at a time: the four bone links a skin binds, and the run of blend-shape deltas a morph binds.</summary>
	public const uint EngineSideStride = 8;
	/// <summary>Storage buffers per compute set 0 / vertex-pulling set 2; sized for the culling pass, the widest</summary>
	public const uint ObjectBindings = 10;

	/// <summary>How many levels of the depth pyramid one dispatch writes - a 32x32 tile of the first reduced through group memory down to one texel.</summary>
	public const uint PyramidLevelsAtOnce = 6;

	/// <summary>How many levels one fold of a picture's chain stores into at most - see picture_fold.slang.</summary>
	public const uint FoldLevelsAtOnce = 6;

	/// <summary>How many lights one frame can have. Sixteen, and the nearest sixteen at that. A real renderer clusters them and lifts the limit; this is the shape that gets a lit scene on the screen, and the clustering can arrive behind the same buffer.</summary>
	public const uint MaxLights = 16;




	/// <summary>Frame block size, derived from CoreSceneRenderer's layout; must match FrameData in frame.slang</summary>
	public const uint FrameBlockSize = (uint)( CoreSceneRenderer.FrameFloats * sizeof( float ) );

	/// <summary>Frame block binding range: room for a second block for the right eye in multiview passes</summary>
	public const uint FrameBlockReach = FrameBlockSize * 2;

	/// <summary>Max profiler zones per frame and name length; extra zones are counted as dropped</summary>
	public const int ProfileMaxZones = 96;
	public const int ProfileNameSize = 48;

	//Zone to collect pipeline statistics for (RINTEN_ZONE_STATS), or null for none
	internal static readonly string StatsZone = Environment.GetEnvironmentVariable( "RINTEN_ZONE_STATS" );

	//Whether the device has the statistics feature and it was asked for.
	internal static bool statsQueries;

	//How many frames have read their statistics back, for saying one in a hundred and twenty.
	internal static int statsFrame;

	/// <summary>Set 0 is a material: 4 images plus params; fixed count so any material fits any pipeline</summary>
	public const uint MaterialTextures = 4;

	/// <summary>Material param slots, one float4 each (unpacked); params past the last slot are silently dropped</summary>
	public const uint MaterialParamSlots = 64;
	public const uint MaterialParamFloats = MaterialParamSlots * 4;
	public const uint MaterialParamSize = MaterialParamFloats * 4;

	/// <summary>Material sets the pool can hold. Live pictures plus the ones still in the graveyard for frames in flight: running out at a thousand was a scene reload, not a leak.</summary>
	public const uint TextureSets = 8192;

	/// <summary>How many objects may bring their own buffers at once. One per drawn chunk of a voxel world, which is what asked for this and what sets the number: a few hundred are in view at a time, and running out is a draw that does not happen rather than a crash.</summary>
	public const uint ObjectSets = 2048;

	/// <summary>Which picture a pass is drawing into. Matches sbc_render_target.</summary>
	public const int TargetScreen = 0;
	public const int TargetScene = 1;
	public const int TargetDepth = 4;
	public const int TargetVelocity = 5;
	public const int TargetPost = 6;
	public const int TargetSceneColour = 7;

	/// <summary>The two screen-size pictures the passes after the compose alternate between - see CorePostBlit and Stage.AfterPostProcess.</summary>
	public const int TargetFinal = 11;
	public const int TargetFinalSecond = 12;

	/// <summary>The world's pictures again at the window's size, in the world's own format and with a chain of halvings: what the effects run over after a temporal upscaler has carried the world up and before the compose takes the curve - see CoreSceneDrawer. Made the first time one is drawn.</summary>
	public const int TargetDisplay = 13;
	public const int TargetDisplaySecond = 14;


	/// <summary>The sky's colour by elevation and by angle to the sun, drawn once a frame for the surfaces to read their ambient out of - see SkyAmbient in frame.slang. Thirty-two texels each way is a thousand of them for a function the sky pass evaluated two million times.</summary>
	public const int TargetSkyTable = 17;
	public const uint SkyTableSide = 64;

	/// <summary>One layer of a shadow map as the target - a level of the sun's pages, or a zone of the map of what moves. Opened a layer at a time and drawn a rectangle at a time: see VkFrame.ShadowBegin and ShadowArea, and CoreSunPages.</summary>
	public const int TargetShadow = 19;

	/// <summary>A level of one of the frame's pictures - see VkPictures and VkFrame.PictureBegin. A pipeline for it is baked against the picture's own format, which is handed over with it.</summary>
	public const int TargetPicture = 20;

	/// <summary>Set in the format handed with <see cref="TargetPicture"/> for a pass that tests against the world's depth - see VkFrame.PictureBegin.</summary>
	public const int PictureWithDepth = 0x40000000;


	/// <summary>Which window a surface is made from. Matches sbc_window_kind.</summary>
	public const int WindowWin32 = 0;
	public const int WindowX11 = 1;
	public const int WindowWayland = 2;
	public const int WindowAndroid = 3;
	public const int WindowMetal = 4;

	public const int VertexNone = 0;
	public const int VertexEngine = 1;
	public const int VertexLine = 2;
	public const int VertexUi = 3;
	public const int VertexSkinned = 4;
	public const int VertexMorphed = 5;
	public const int VertexMorphedSkinned = 6;
	public const int VertexMeshShader = 7;

	//-- the objects, and nothing that outlives them ------------------------

	//The binding, opened against the loader below.
	internal static Vk vk;

	//Where the driver came from - see VulkanLoader.
	internal static VulkanLoader loader;

	//The extensions the surface, the swapchain and the labels come from.
	internal static KhrSurface khrSurface;
	internal static KhrSwapchain khrSwapchain;

	//Mesh stages, where the device has them - null where it has not. See meshShaders.
	internal static ExtMeshShader extMeshShader;
	internal static ExtDebugUtils extDebugUtils;

	//The platform's own way of turning a window into a surface. One of these is not null on any given machine;
	//on Linux either or both may be, which is why they are asked for separately and the window says which it is.
	internal static KhrWin32Surface khrWin32Surface;
	internal static KhrXlibSurface khrXlibSurface;
	internal static KhrWaylandSurface khrWaylandSurface;
	internal static KhrAndroidSurface khrAndroidSurface;
	internal static ExtMetalSurface extMetalSurface;

	internal static uint apiVersion;

	//The version the instance is asked for in: the loader's, up to the 1.3 this engine uses.
	internal static uint instanceApi = Vk.Version13;
	internal static bool validation;

	//Whether the driver will tell us what it thinks our memory use is. Where it will not, the only
	//number available is our own sum of what we asked for, which is always the smaller of the two.
	internal static bool memoryBudget;

	internal static Instance instance;
	internal static DebugUtilsMessengerEXT messenger;
	internal static PhysicalDevice physicalDevice;

	//Who made the card the device is on - PCI's number: 0x10DE NVIDIA, 0x1002 AMD, 0x8086 Intel.
	internal static uint VendorId;

	//Whether the device is opened with buffer device addresses for NVIDIA's NGX, as 1.2's feature - see CoreDlss and VkModern.ChainFeatures.
	internal static bool ngxDeviceAddress;
	internal static Device device;

	//The queue everything is submitted on. One queue to start with. Separate transfer and compute queues are how an
	//engine overlaps uploads with drawing, and that is worth doing - but only once there is something to overlap.
	internal static uint graphicsFamily = uint.MaxValue;
	internal static Queue graphicsQueue;

	//What sampling needs, made once with the device: a single linear-repeat-mipmapped sampler for
	//everything, the layout of set 0 - a material - and the pool its descriptor sets come from.
	internal static Sampler sampler;

	//What the post passes read with: no wrapping, because a tap that runs off the edge of the screen should find the edge rather
	//than the other side. Point for depth, which must not be averaged between surfaces at different distances; linear for colour.
	internal static Sampler clampSampler;
	internal static Sampler pointSampler;

	//Max-reduction sampler for the depth pyramid: coarse texels must report the farthest depth
	internal static Sampler farthestSampler;

	//Whether the device gave us samplerFilterMinmax, so the pyramid's sampler may take the farthest rather than the average.
	internal static bool filterMinmax;

	internal static DescriptorSetLayout textureSetLayout;

	//One level of the pyramid being built: what it reads and what it writes. Its own layout rather than the texture one,
	//because writing an image wants a storage image and the texture set has none - it was made for passes that only read.
	internal static DescriptorSetLayout pyramidSetLayout;

	//And the set a fold of a picture's chain runs through: two levels to read and the levels it stores into - see VkFrame.FoldChain.
	internal static DescriptorSetLayout foldSetLayout;

	//Set 2 of a compute pass over the frame's pictures: the levels it stores into, then the buffers - see VkFrame.PassDispatch.
	internal static DescriptorSetLayout passSetLayout;

	//How many levels one pass may store into, and how many buffers it may write. Twelve since FSR 3's passes: its luma pyramid stores into nine - a counter,
	//the frame's numbers, six levels of a chain and a depth - see CoreFsr3. The buffers come after, at binding twelve; nothing declared them before.
	internal const uint PassImages = 12;
	internal const uint PassBuffers = 4;

	//Whether the device stores into the smaller float formats - the bloom's chains are one where it does. See CompactFormat.
	internal static bool storageExtended;

	//Whether a compute pass may store into a picture without naming its format - see VkDevice.Setup, where it is asked for.
	internal static bool storageWithoutFormat;

	internal static DescriptorPool descriptorPool;

	//The parameters a material has when nobody set any - and what every plain texture points at, so its set is complete without one of its own.
	internal static VkBuffer defaultParams;
	internal static DeviceMemory defaultParamsMemory;

	//Frame light buffer (set 1, binding 7); frame scratch stands in until made, as RADV faults on empty
	internal static VkBuffer lightBuffer;



	//The sky's table of colour by elevation and sun angle - see TargetSkyTable.
	internal static VkImage skyTableImage;
	internal static DeviceMemory skyTableMemory;
	internal static ImageView skyTableView;
	internal static ImageLayout skyTableLayout;
	internal static ulong skyTableHandle;

	//Shadow map sampler: depth compare, repeat addressing since sun pages wrap as a torus
	internal static Sampler shadowSampler;

	//The portable frame's single sun shadow map, bound at frame set binding 2. Zero in the full frame
	internal static ulong portableSunMap;

	//Texels across it.
	internal const int PortableSunSide = 2048;

	//What it is read with: compared, texel by texel, clamped.
	internal static Sampler portableShadowSampler;

	//The same maps read as plain depth - the nearest texel, no compare, wrapped the same way - for how far the surface
	//in front of a pixel is, which is what a shadow's softness is worked out from. See ShadowSoft in frame.slang.
	internal static Sampler shadowDepthSampler;





	//Compares rather than reads: the hardware does the depth test at four taps and averages them, which is most of what soft edges are.

	//Set 1: what the frame knows. Binding 0 is the bone matrices of whatever is being drawn - a different slice of scratch per draw -
	//and binding 1 is the camera and the lights, one slice for the whole frame. Both are read at an offset, so one set serves every draw.
	internal static DescriptorSetLayout boneSetLayout;

	//Set 2: per-object storage buffers, so shaders can pull any vertex format without new layouts
	internal static DescriptorSetLayout objectSetLayout;

	//Set 3: the bindless picture table, one set with its own update-after-bind pool
	internal static DescriptorSetLayout bindlessSetLayout;
	internal static DescriptorPool bindlessPool;
	internal static DescriptorSet bindlessSet;

	//Whether the device would give us any of that. Everything still works without it - a shader that
	//indexes the table draws with whatever slot zero holds - so this is a capability, not a requirement.
	internal static bool bindless;

	//How this device is spoken to - see VkDialect. Chosen with the device, and the only thing that may call what Vulkan 1.3 added.
	internal static VkDialect dialect;

	//The frame this device can hold - see RenderTier. The full one until a device says otherwise,
	//which is what everything asked before there is a device - a bake, a test - is asking about.
	internal static RenderTier Tier => dialect?.Tier ?? RenderTier.Full;

	//Destroys a view, having told whatever may have kept something made around it - see VkDialect.ForgetView. The one way a view goes.
	internal static void DestroyView( ImageView view )
	{
		if ( view.Handle == 0 ) return;

		dialect?.ForgetView( view );

		vk.DestroyImageView( device, view, null );
	}

	//What the device demands of a storage buffer's offset.
	internal static uint storageAlignment = 256;

	//How many pictures the table actually has - see BindlessSlotsWanted.
	internal static uint bindlessSlots = BindlessSlotsLeast;

	//The widest or tallest a 2D picture may be. Asked for rather than assumed: a request past it is refused
	//where it is made, instead of becoming an image that was never created and a driver that is handed it anyway.
	internal static uint maxImageSize = 16384;

	//The device's memory types, asked for once. Every buffer and image creation needs them, and they never change while the device is open.
	internal static PhysicalDeviceMemoryProperties memoryProperties;

	//Nanoseconds per timestamp tick, or zero when the graphics queue cannot timestamp at all
	//- in which case the profiler reports CPU times and no GPU ones, rather than nonsense.
	internal static float timestampPeriod;

	//Pipeline cache, saved to disk between runs to skip driver recompiles
	internal static PipelineCache pipelineCache;

	//Whether the device does anisotropic filtering, and how far. Optional rather than required: a device
	//without it draws the same picture with blurrier ground, which is not a reason to refuse to start.
	internal static bool anisotropy;
	internal static float maxAnisotropy = 1.0f;

	//What the material sampler was last made with. The setting, clamped - not the device's ceiling.
	internal static float anisotropyAsked = 1.0f;

	//The biggest edge a texture may arrive at, or zero for whatever it is. A texture with mips that is larger than this starts from the
	//first level that fits: the top levels are simply not uploaded, and the picture on the device is a quarter the memory per level dropped.
	internal static uint textureMaxEdge;

	//Whether depth can be blitted; required for rendering the world below window resolution
	internal static bool depthBlit;

	//Whether BC textures are supported; without it imports are unpacked to RGBA8
	internal static bool blockCompression;

	//Whether the device samples ASTC 4x4 and ETC2 - a phone's, for the build's KTX2 pictures; see CoreBasisTranscoder.
	internal static bool astc, etc2;

	//Per-attachment blend state; without it normals blend along with colour behind glass
	internal static bool independentBlend;

	//Whether wireframe (fillModeNonSolid) works; without it line pipelines are baked filled
	internal static bool fillLines;

	//Whether indirect draws may use a nonzero first instance, letting instanced draws share one buffer
	internal static bool firstInstance;

	//Whether one indirect call may draw many args; without it batches must be split per command
	internal static bool multiDraw;

	//Whether an index may be any 32-bit number rather than one under 2^24. The cluster raster's index names a visible entry and a vertex
	//of its cluster in one word - see CoreMeshShaders.EntryShift - so without this a frame may hold no more than 131072 visible clusters.
	internal static bool fullIndex;

	//Whether the device draws mesh stages (VK_EXT_mesh_shader). Asked for, never demanded: a desktop card from before
	//2018, most phones and every browser have none, and the indexed draw is what the world is drawn by everywhere.
	internal static bool meshShaders;

	//Every stage a draw may have, for the sets and the push range every graphics pipeline shares: the mesh stage too where the device
	//has it. One answer for all of them, so any two pipelines' layouts stay compatible - a set bound under one is bound under the next.
	internal static ShaderStageFlags DrawStages => ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit
		| ( meshShaders ? ShaderStageFlags.MeshBitExt : 0 );

	//vkCmdDrawIndexedIndirectCount: a draw whose count the card wrote.
	internal static bool drawIndirectCount;

	//Whether multiview (both headset eyes in one pass) is available; on whenever supported
	internal static bool multiview;

	//And how many of them one call may name, which is one where the above is missing and is worth reading even where it is not.
	internal static uint maxDrawIndirect = 1;


	//-- what everything takes before it touches any of the above -----------

	//Serialises device bring-up/teardown and frame recording; cheap even on the draw path
	internal static readonly System.Threading.Lock renderLock = new();

	//Whether the device is open, readable without taking the lock.
	private static volatile bool isReady;

	internal static bool Ready => isReady;

	//A queue call returned VK_ERROR_DEVICE_LOST. Further submits are refused so the driver is not hammered after the first failure.
	private static volatile bool isLost;

	internal static bool Lost => isLost;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static void MarkLostIf( Result result )
	{
		if ( result == Result.ErrorDeviceLost ) isLost = true;
	}

	//The strings the ABI used to hand out as fixed buffers. Plain strings here: nothing outlives
	//the call that reads them any more, because the caller is managed and copies at the assignment.
	internal static string infoText = "not initialised";
	internal static string deviceNameText = "";

	internal static void SetText( string value ) => infoText = value;

	//How much device memory the engine's own meshes and textures hold, for the profiler.
	internal static long trackedBufferBytes;
	internal static long trackedTextureBytes;
	internal static int trackedBufferCount;
	internal static int trackedTextureCount;

	//The pictures the frame draws into - a view's world, normals, depth and post pictures, the window's backdrop - by the memory each was
	//given, so letting one go takes its bytes off again. Not in the textures above: those are what the engine loads, these are what it renders.
	private static readonly Dictionary<ulong, long> targetMemory = new();
	internal static long trackedTargetBytes;

	internal static void CountTarget( DeviceMemory memory, ulong bytes )
	{
		if ( memory.Handle == 0 ) return;

		lock ( targetMemory )
		{
			if ( !targetMemory.TryAdd( memory.Handle, (long)bytes ) ) return;
			trackedTargetBytes += (long)bytes;
		}
	}

	//Called before any image memory is freed; memory that was never counted is passed over.
	internal static void UncountTarget( DeviceMemory memory )
	{
		if ( memory.Handle == 0 ) return;

		lock ( targetMemory )
		{
			if ( targetMemory.Remove( memory.Handle, out var bytes ) ) trackedTargetBytes -= bytes;
		}
	}

	private static readonly Logger log = new( "Render" );

	//What the core used to say through the ABI's log seam.
	internal static void Say( string message ) => log.Info( message );

	//A line only somebody chasing the device wants: hidden until trace is on.
	internal static void SayTrace( string message ) => log.Trace( message );

	internal static void SayWarning( string message )
	{
		CoreErrors.Warned();
		log.Warning( message );
	}

	//An error from the driver or the validation layers. Counted as well as logged: CoreErrors
	//is what a test watches to know whether validation stayed quiet while it drew.
	internal static void SayError( string message )
	{
		CoreErrors.Raised();
		log.Error( message );
	}

	//-- naming -------------------------------------------------------------

	//Names a Vulkan object for validation messages and capture tools; no-op without debug utils
	internal static void NameHandle( ObjectType type, ulong handle, string name )
	{
		if ( extDebugUtils is null ) return;
		if ( device.Handle == 0 || handle == 0 || name is null ) return;

		var bytes = stackalloc byte[128];
		WriteUtf8( name, bytes, 128 );

		var info = new DebugUtilsObjectNameInfoEXT
		{
			SType = StructureType.DebugUtilsObjectNameInfoExt,
			ObjectType = type,
			ObjectHandle = handle,
			PObjectName = bytes,
		};

		extDebugUtils.SetDebugUtilsObjectName( device, &info );
	}

	//The same with a number after the name: "mesh 41", "swapchain image 2".
	internal static void NameHandle( ObjectType type, ulong handle, string name, ulong number )
	{
		if ( extDebugUtils is null ) return;
		if ( name is null ) return;

		NameHandle( type, handle, string.Concat( name, " ", number.ToString( System.Globalization.CultureInfo.InvariantCulture ) ) );
	}

	//A string as NUL-terminated UTF-8 in a buffer the caller owns.
	internal static int WriteUtf8( string value, byte* into, int capacity )
	{
		if ( capacity <= 0 ) return 0;

		var written = 0;

		if ( !string.IsNullOrEmpty( value ) )
		{
			fixed ( char* chars = value )
			{
				var wanted = System.Text.Encoding.UTF8.GetByteCount( chars, value.Length );

				//A name is never worth truncating in the middle of a character, so a value that will not fit is cut by characters until it does.
				var take = value.Length;

				while ( wanted > capacity - 1 && take > 0 )
				{
					take--;
					wanted = System.Text.Encoding.UTF8.GetByteCount( chars, take );
				}

				written = System.Text.Encoding.UTF8.GetBytes( chars, take, into, capacity - 1 );
			}
		}

		into[written] = 0;
		return written;
	}

	//The index of a memory type with these properties that suits these requirement bits, or uint.MaxValue if the device has no such memory.
	internal static uint FindMemoryType( uint typeBits, MemoryPropertyFlags properties )
	{
		//From the copy taken when the device opened - this is asked on every allocation, and the answer never changes while the device is open.
		for ( var i = 0u; i < memoryProperties.MemoryTypeCount; ++i )
		{
			var allowed = ( typeBits & ( 1u << (int)i ) ) != 0;
			var suits = ( memoryProperties.MemoryTypes[(int)i].PropertyFlags & properties ) == properties;

			if ( allowed && suits ) return i;
		}

		return uint.MaxValue;
	}

	internal static uint FindMemoryTypeWithout( uint typeBits, MemoryPropertyFlags wanted, MemoryPropertyFlags unwanted )
	{
		for ( var i = 0u; i < memoryProperties.MemoryTypeCount; ++i )
		{
			var flags = memoryProperties.MemoryTypes[(int)i].PropertyFlags;

			var allowed = ( typeBits & ( 1u << (int)i ) ) != 0;
			var suits = ( flags & wanted ) == wanted && ( flags & unwanted ) == 0;

			if ( allowed && suits ) return i;
		}

		return uint.MaxValue;
	}

	//Whether host-writable memory is the GPU's fast memory (integrated or resizable BAR), via the heaps
	internal static bool HostCanWriteTheFastMemory( uint typeBits )
	{
		const MemoryPropertyFlags Shared = MemoryPropertyFlags.DeviceLocalBit |
			MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;

		ulong sharedHeap = 0;
		ulong deviceHeap = 0;

		for ( var i = 0u; i < memoryProperties.MemoryTypeCount; ++i )
		{
			var type = memoryProperties.MemoryTypes[(int)i];

			if ( ( type.PropertyFlags & MemoryPropertyFlags.DeviceLocalBit ) == 0 ) continue;

			var size = memoryProperties.MemoryHeaps[(int)type.HeapIndex].Size;

			if ( size > deviceHeap ) deviceHeap = size;

			//Only a type this buffer could actually have counts towards the window: a heap the host can see but this allocation may not use is no help to it.
			if ( ( typeBits & ( 1u << (int)i ) ) == 0 ) continue;
			if ( ( type.PropertyFlags & Shared ) != Shared ) continue;

			if ( size > sharedHeap ) sharedHeap = size;
		}

		if ( sharedHeap == 0 ) return false;

		//Within a sixteenth of the biggest device-local heap counts as all of it: the two are often reported as the same heap, and where they
		//are not, a BAR window is smaller than that by orders of magnitude, not by a rounding of how much the driver reserves for itself.
		return sharedHeap >= deviceHeap - ( deviceHeap / 16 );
	}

	//A small host-visible buffer holding a material's numbers. Host-visible rather than staged:
	//a few hundred bytes written once, and a staging copy would cost more than the buffer.
	internal static bool MakeParamsBuffer( float* values, out VkBuffer outBuffer, out DeviceMemory outMemory )
	{
		outBuffer = default;
		outMemory = default;

		var info = new BufferCreateInfo
		{
			SType = StructureType.BufferCreateInfo,
			Size = MaterialParamSize,
			//Copied into as well as read: a material's numbers are written in place when they change - see VkFrame.MaterialNumbers.
			Usage = BufferUsageFlags.UniformBufferBit | BufferUsageFlags.TransferDstBit,
			SharingMode = SharingMode.Exclusive,
		};

		VkBuffer buffer;
		if ( vk.CreateBuffer( device, &info, null, &buffer ) != Result.Success ) return false;

		outBuffer = buffer;

		MemoryRequirements requirements;
		vk.GetBufferMemoryRequirements( device, buffer, &requirements );

		var allocate = new MemoryAllocateInfo
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = requirements.Size,
			MemoryTypeIndex = FindMemoryType( requirements.MemoryTypeBits,
				MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit ),
		};

		DeviceMemory memory = default;
		void* mapped = null;

		if ( allocate.MemoryTypeIndex == uint.MaxValue ||
			vk.AllocateMemory( device, &allocate, null, &memory ) != Result.Success ||
			vk.BindBufferMemory( device, buffer, memory, 0 ) != Result.Success ||
			vk.MapMemory( device, memory, 0, Vk.WholeSize, 0, &mapped ) != Result.Success )
		{
			if ( memory.Handle != 0 ) vk.FreeMemory( device, memory, null );
			vk.DestroyBuffer( device, buffer, null );

			outBuffer = default;
			outMemory = default;
			return false;
		}

		System.Buffer.MemoryCopy( values, mapped, MaterialParamSize, MaterialParamSize );
		vk.UnmapMemory( device, memory );

		outMemory = memory;
		return true;
	}

	//-- deferred destruction: resources wait in a graveyard until a fence proves their ticket finished
	//Tickets count submissions on one queue; mid-recording retires get a sentinel. Needs the render lock

	//One resource waiting for the GPU to be provably done with it.
	private struct Grave
	{
		public ulong Ticket;

		public VkBuffer Buffer;
		public DeviceMemory Memory;
		public VkImage Image;
		public ImageView View;
		public Pipeline Pipeline;
		public PipelineLayout PipelineLayout;
		public DescriptorSet Set;
		public VkSemaphore Semaphore;
		public SwapchainKHR Swapchain;
		public uint Bindless;

		/// <summary>Where this image's memory came from, when it was a slice of a texture slab: the slice is given back rather than freed.</summary>
		public int Block;
		public ulong Offset;
		public ulong Size;
	}

	private static readonly List<Grave> graveyard = new( 256 );

	//Queue submissions, counted. One queue means they finish in order, so one completed ticket stands for everything at or before it.
	private static ulong submittedTicket;
	private static ulong completedTicket;

	//Whether a frame is being recorded, and the sentinel a grave carries while the ticket that recording will submit under is not known yet.
	private static bool recordingOpen;

	private const ulong RecordingSentinel = ulong.MaxValue;

	//What a grave retired right now has to wait for: the open recording if there is one, otherwise only what has already been submitted.
	private static ulong StampForNow() => recordingOpen ? RecordingSentinel : submittedTicket;

	//Ticket a few submissions later, for present semaphores and swapchains the fence doesn't cover
	private static ulong StampAfter( uint submissions )
		=> ( recordingOpen ? submittedTicket + 1 : submittedTicket ) + submissions;

	private static void Bury( in Grave grave )
	{
		if ( grave.Bindless != 0 ) VkTextures.RecycleBindlessSlot( grave.Bindless );

		if ( grave.Set.Handle != 0 ) FreeDescriptorSet( grave.Set );

		if ( grave.Pipeline.Handle != 0 ) vk.DestroyPipeline( device, grave.Pipeline, null );
		if ( grave.PipelineLayout.Handle != 0 ) vk.DestroyPipelineLayout( device, grave.PipelineLayout, null );

		if ( grave.Semaphore.Handle != 0 ) vk.DestroySemaphore( device, grave.Semaphore, null );

		//After the images it presented and the semaphores that waited on them, because it owns those images.
		DestroyView( grave.View );
		if ( grave.Image.Handle != 0 ) vk.DestroyImage( device, grave.Image, null );

		//Before the buffer it was built into, which is its storage.

		if ( grave.Buffer.Handle != 0 ) vk.DestroyBuffer( device, grave.Buffer, null );

		if ( grave.Block >= 0 ) VkTextures.GiveBackTextureSlice( grave.Block, grave.Offset, grave.Size );
		else if ( grave.Memory.Handle != 0 )
		{
			UncountTarget( grave.Memory );
			vk.FreeMemory( device, grave.Memory, null );
		}

		if ( grave.Swapchain.Handle != 0 ) khrSwapchain?.DestroySwapchain( device, grave.Swapchain, null );
	}

	//A fence has proved this ticket's submission finished; buries everything in the graveyard that was waiting on it or on anything earlier.
	internal static void TicketDone( ulong ticket )
	{
		//Under the render lock: burying frees bindless slots that streaming workers allocate concurrently
		lock ( renderLock )
		{
			if ( ticket <= completedTicket ) return;

			completedTicket = ticket;

			//Swap-and-pop rather than remove: order in a graveyard means nothing. The sentinel is
			//ulong.MaxValue, so what the open recording may still name never compares as buryable.
			for ( var at = 0; at < graveyard.Count; )
			{
				if ( graveyard[at].Ticket > completedTicket )
				{
					++at;
					continue;
				}

				Bury( graveyard[at] );
				graveyard[at] = graveyard[^1];
				graveyard.RemoveAt( graveyard.Count - 1 );
			}
		}
	}

	//A grave into the graveyard - from whichever thread let go of it, so under the render lock.
	private static void Retire( Grave grave )
	{
		lock ( renderLock ) graveyard.Add( grave );
	}

	//Everything ever submitted is done - what follows a device-wide wait.
	internal static void AllTicketsDone() => TicketDone( submittedTicket );

	//The last submission a fence has proved finished - see TicketDone. What a buffer on its way back to the host waits for; see VkReadbacks.
	internal static ulong CompletedTicket => completedTicket;

	//Whether a recording is open, whose ticket is not known until it is submitted.
	internal static bool RecordingOpen => recordingOpen;

	//Everything handed to the queue so far - the uploads still being collected included - finished before this
	//returns. A recording still open is not anything handed to the queue: what it holds runs when it is submitted.
	internal static int WaitIdle()
	{
		lock ( renderLock )
		{
			if ( !Ready ) return 0;

			VkTextures.FlushTextureUploads();

			var waited = VkSubmitter.DeviceWaitIdle();

			if ( waited != Result.Success )
			{
				MarkLostIf( waited );
				return 0;
			}

			AllTicketsDone();

			return 1;
		}
	}

	//A frame has begun recording: resources retired from here on may be named by it, and wait for its ticket rather than for anything already known.
	internal static void RecordingBegan()
	{
		lock ( renderLock ) recordingOpen = true;
	}

	//The recording is being submitted right now: takes its ticket and stamps it onto everything retired while it was open.
	internal static ulong RecordingSubmitted()
	{
		ulong ticket;

		//Locked as TicketDone is: a worker may be retiring a grave right now.
		lock ( renderLock )
		{
			ticket = ++submittedTicket;

			for ( var i = 0; i < graveyard.Count; ++i )
			{
				if ( graveyard[i].Ticket != RecordingSentinel ) continue;

				var grave = graveyard[i];
				grave.Ticket = ticket;
				graveyard[i] = grave;
			}

			recordingOpen = false;
		}

		//And the copies back to the host recorded into it, which wait for the same.
		VkReadbacks.Submitted( ticket );

		return ticket;
	}

	//The recording never made it to the queue. What was retired during it can only be named by work already submitted.
	internal static void RecordingAbandoned()
	{
		lock ( renderLock )
		{
			for ( var i = 0; i < graveyard.Count; ++i )
			{
				if ( graveyard[i].Ticket != RecordingSentinel ) continue;

				var grave = graveyard[i];
				grave.Ticket = submittedTicket;
				graveyard[i] = grave;
			}

			recordingOpen = false;
		}

		//A copy back to the host recorded into it never ran, and is asked again.
		VkReadbacks.Abandoned();
	}

	internal static void RetireBuffer( VkBuffer buffer, DeviceMemory memory )
	{
		Retire( new Grave { Ticket = StampForNow(), Buffer = buffer, Memory = memory, Block = -1 } );
	}

	internal static void RetireImage( VkImage image, DeviceMemory memory, ImageView view )
	{
		Retire( new Grave { Ticket = StampForNow(), Image = image, Memory = memory, View = view, Block = -1 } );
	}

	//The same for a picture whose memory is a slice of a slab: the image goes when nothing can be reading it,
	//and the slice is handed back to the pool then rather than freed - the slab belongs to everybody else in it.
	internal static void RetireSlicedImage( VkImage image, ImageView view, int block, ulong offset, ulong size )
	{
		Retire( new Grave
		{
			Ticket = StampForNow(),
			Image = image,
			View = view,
			Block = block,
			Offset = offset,
			Size = size,
		} );
	}

	//These two take a number of further submissions to outlast, because a frame's fence does not answer for the present that follows it: the
	//semaphore the presentation engine waits on and the swapchain whose image it shows are still the queue's business after the fence has signalled.
	internal static void RetireSemaphore( VkSemaphore semaphore, uint after = 0 )
	{
		if ( semaphore.Handle == 0 ) return;

		Retire( new Grave { Ticket = StampAfter( after ), Semaphore = semaphore, Block = -1 } );
	}

	internal static void RetireSwapchain( SwapchainKHR swapchain, uint after = 0 )
	{
		if ( swapchain.Handle == 0 ) return;

		Retire( new Grave { Ticket = StampAfter( after ), Swapchain = swapchain, Block = -1 } );
	}

	internal static void RetirePipeline( Pipeline pipeline, PipelineLayout layout )
	{
		Retire( new Grave { Ticket = StampForNow(), Pipeline = pipeline, PipelineLayout = layout, Block = -1 } );
	}

	internal static void RetireDescriptorSet( DescriptorSet set )
	{
		Retire( new Grave { Ticket = StampForNow(), Set = set, Block = -1 } );
	}

	internal static void RetireBindless( uint slot )
	{
		if ( slot == 0 ) return;

		Retire( new Grave { Ticket = StampForNow(), Bindless = slot, Block = -1 } );
	}

	//Destroys everything in the graveyard regardless of tickets. Only for device shutdown, after the device has been waited idle.
	internal static void DrainGraveyard()
	{
		for ( var i = 0; i < graveyard.Count; ++i ) Bury( graveyard[i] );

		graveyard.Clear();
		submittedTicket = 0;
		completedTicket = 0;
		recordingOpen = false;
	}

	//-- descriptor pools ---------------------------------------------------

	//Descriptor set kinds, counted apart so a pool can be full for one kind but not another
	private enum SetKind
	{
		Texture = 0,
		Object = 1,
		Frame = 2,
	}

	//A descriptor pool with per-kind counts; we track capacity ourselves as some drivers never report full
	private sealed class DescriptorPoolSlot
	{
		public DescriptorPool Pool;
		public readonly uint[] Taken = new uint[3];
	}

	//Every pool sets have been allocated from, the first one first. More than one because they fill up: a world made
	//of chunks wants a set per chunk, and how many chunks there are is a property of the world and not of this file.
	private static readonly List<DescriptorPoolSlot> descriptorPools = new();

	//Where each live set came from and what it counts against, so that giving one back returns the room it took.
	private readonly struct SetHome( int pool, int kind )
	{
		public readonly int Pool = pool;
		public readonly int Kind = kind;
	}

	private static readonly Dictionary<ulong, SetHome> setPools = new();

	//How many of each kind one pool holds - the same numbers its sizes are worked out from.
	private static uint RoomFor( SetKind kind ) => kind switch
	{
		SetKind.Object => ObjectSets,
		SetKind.Frame => 32,
		_ => TextureSets,
	};

	private static SetKind KindOf( DescriptorSetLayout layout )
	{
		if ( layout.Handle == objectSetLayout.Handle ) return SetKind.Object;
		if ( layout.Handle == boneSetLayout.Handle ) return SetKind.Frame;

		return SetKind.Texture;
	}

	//A pool of the shape everything here allocates out of: materials, the frame's own sets, and storage buffers per object that pulls its own
	//vertices. The counts are what one of these holds, not what the device may have. Running out of them is answered by making another one.
	private static DescriptorPool MakeDescriptorPool()
	{
		var poolSizes = stackalloc DescriptorPoolSize[5];

		//And two a frame set in the portable frame, whose sun map and sky table are both bound there - see portableSunMap and skyTableImage.
		poolSizes[0].Type = DescriptorType.CombinedImageSampler;
		poolSizes[0].DescriptorCount = ( TextureSets * MaterialTextures ) + 64;

		//Three per frame set - the bones, the frame, and the blend shape weights - and a set per
		//frame in flight, per instanced draw that points the bone slot at a buffer of its own.
		poolSizes[1].Type = DescriptorType.StorageBufferDynamic;
		poolSizes[1].DescriptorCount = 24;

		poolSizes[2].Type = DescriptorType.UniformBuffer;
		poolSizes[2].DescriptorCount = TextureSets;

		//One per binding per object that pulls its own vertices. A voxel world is the first thing to want
		//these and wants one per chunk, so the room is a world's worth of chunks rather than a handful.
		poolSizes[3].Type = DescriptorType.StorageBuffer;
		poolSizes[3].DescriptorCount = ( ObjectBindings * ObjectSets ) + 32 + ( 64 * PassBuffers );

		//The depth pyramid's levels and the glow's, written as storage images - a level a binding, which the pool never said it had room for.
		poolSizes[4].Type = DescriptorType.StorageImage;
		poolSizes[4].DescriptorCount = 64 * ( PyramidLevelsAtOnce + FoldLevelsAtOnce + PassImages );

		//Every descriptor type any layout uses must be listed, or RADV corrupts the pool instead of failing
		var sizeCount = 5u;

		var poolInfo = new DescriptorPoolCreateInfo
		{
			SType = StructureType.DescriptorPoolCreateInfo,
			Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
			MaxSets = TextureSets + 32 + ObjectSets,
			PoolSizeCount = sizeCount,
			PPoolSizes = poolSizes,
		};

		DescriptorPool made = default;

		if ( vk.CreateDescriptorPool( device, &poolInfo, null, &made ) != Result.Success )
			return default;

		//Numbered, because which one a set came out of is the question a pool that has run out raises.
		NameHandle( ObjectType.DescriptorPool, made.Handle, "descriptor pool", (ulong)descriptorPools.Count );

		return made;
	}

	//Allocates a set from whichever pool has room, growing a new pool when all are full
	internal static bool AllocateDescriptorSet( DescriptorSetLayout layout, out DescriptorSet outSet )
	{
		outSet = default;

		if ( layout.Handle == 0 || device.Handle == 0 ) return false;

		var kind = KindOf( layout );
		var room = RoomFor( kind );

		var wanted = layout;

		var info = new DescriptorSetAllocateInfo
		{
			SType = StructureType.DescriptorSetAllocateInfo,
			DescriptorSetCount = 1,
			PSetLayouts = &wanted,
		};

		DescriptorSet set = default;

		//Oldest first, so that room given back by a destroyed object is used again before another pool
		//is made. The walk is one step in every frame that is not the one where a pool filled up.
		for ( var i = 0; i < descriptorPools.Count; ++i )
		{
			if ( descriptorPools[i].Taken[(int)kind] >= room ) continue;

			info.DescriptorPool = descriptorPools[i].Pool;

			var result = vk.AllocateDescriptorSets( device, &info, &set );

			if ( result == Result.Success )
			{
				descriptorPools[i].Taken[(int)kind]++;
				setPools[set.Handle] = new SetHome( i, (int)kind );
				outSet = set;
				return true;
			}

			set = default;

			//The counts said there was room and the driver disagreed - a pool fragmented by frees, or one
			//whose sizes this build worked out differently. Either way it is full as far as this kind goes.
			if ( result == Result.ErrorOutOfPoolMemory || result == Result.ErrorFragmentedPool )
			{
				descriptorPools[i].Taken[(int)kind] = room;
				continue;
			}

			MarkLostIf( result );
			SetText( "no memory for a descriptor set" );
			return false;
		}

		var grown = MakeDescriptorPool();

		if ( grown.Handle == 0 )
		{
			SetText( "no memory for another descriptor pool" );
			return false;
		}

		descriptorPools.Add( new DescriptorPoolSlot { Pool = grown } );

		info.DescriptorPool = grown;

		if ( vk.AllocateDescriptorSets( device, &info, &set ) != Result.Success )
		{
			//A set that will not come out of a pool made for it is a set no pool can hold: the layout wants more than one of these has room for.
			SetText( "a descriptor set larger than a pool" );
			return false;
		}

		var last = descriptorPools.Count - 1;

		descriptorPools[last].Taken[(int)kind]++;
		setPools[set.Handle] = new SetHome( last, (int)kind );

		outSet = set;
		return true;
	}

	//Gives one back to the pool it came from, which is why it is not free to call vkFreeDescriptorSets
	//directly any more: with several pools, the set alone no longer says where it belongs.
	internal static void FreeDescriptorSet( DescriptorSet set )
	{
		if ( set.Handle == 0 || device.Handle == 0 ) return;

		//Unknown set means a double free or foreign set; leak it rather than corrupt a pool
		if ( !setPools.TryGetValue( set.Handle, out var home ) )
		{
			Say( $"a descriptor set was given back twice, or to no pool of ours: 0x{set.Handle:x}" );

			return;
		}

		if ( home.Pool < descriptorPools.Count )
		{
			var pool = descriptorPools[home.Pool];

			vk.FreeDescriptorSets( device, pool.Pool, 1, &set );

			if ( pool.Taken[home.Kind] > 0 ) pool.Taken[home.Kind]--;
		}

		setPools.Remove( set.Handle );
	}

	private static void DestroyDescriptorPools()
	{
		//The sets go with their pools, so the map of one to the other goes too.
		setPools.Clear();

		for ( var i = 0; i < descriptorPools.Count; ++i )
		{
			if ( descriptorPools[i].Pool.Handle != 0 )
				vk.DestroyDescriptorPool( device, descriptorPools[i].Pool, null );
		}

		descriptorPools.Clear();
		descriptorPool = default;
	}
}
