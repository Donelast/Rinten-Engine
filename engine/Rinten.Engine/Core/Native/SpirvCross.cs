using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rinten.Native.SpirvCross;

//SPIRV-Cross C API to turn SPIR-V into GLSL ES for WebGL2; editor only, no UnmanagedCallConv
internal static unsafe partial class Spvc
{
	private const string Library = "spirv-cross";

	public const int Success = 0;

	[LibraryImport( Library )]
	public static partial int spvc_context_create( nint* context );

	[LibraryImport( Library )]
	public static partial void spvc_context_destroy( nint context );

	[LibraryImport( Library )]
	public static partial byte* spvc_context_get_last_error_string( nint context );

	[LibraryImport( Library )]
	public static partial int spvc_context_parse_spirv( nint context, uint* spirv, nuint wordCount, nint* parsed );

	[LibraryImport( Library )]
	public static partial int spvc_context_create_compiler( nint context, SpvcBackend backend, nint parsed,
		CaptureMode mode, nint* compiler );

	[LibraryImport( Library )]
	public static partial int spvc_compiler_create_compiler_options( nint compiler, nint* options );

	[LibraryImport( Library )]
	public static partial int spvc_compiler_options_set_bool( nint options, Option option, byte value );

	[LibraryImport( Library )]
	public static partial int spvc_compiler_options_set_uint( nint options, Option option, uint value );

	[LibraryImport( Library )]
	public static partial int spvc_compiler_install_compiler_options( nint compiler, nint options );

	[LibraryImport( Library )]
	public static partial int spvc_compiler_compile( nint compiler, byte** source );

	[LibraryImport( Library )]
	public static partial int spvc_compiler_create_shader_resources( nint compiler, nint* resources );

	[LibraryImport( Library )]
	public static partial int spvc_resources_get_resource_list_for_type( nint resources, ResourceType type,
		ReflectedResource** list, nuint* count );

	[LibraryImport( Library )]
	public static partial void spvc_compiler_set_name( nint compiler, uint id, byte* name );

	[LibraryImport( Library )]
	public static partial uint spvc_compiler_get_decoration( nint compiler, uint id, Decoration decoration );

	[LibraryImport( Library )]
	public static partial int spvc_compiler_build_dummy_sampler_for_combined_images( nint compiler, uint* id );

	[LibraryImport( Library )]
	public static partial int spvc_compiler_build_combined_image_samplers( nint compiler );

	[LibraryImport( Library )]
	public static partial int spvc_compiler_get_combined_image_samplers( nint compiler, CombinedSampler** samplers,
		nuint* count );
}

//spvc_backend.
internal enum SpvcBackend
{
	Glsl = 1,
}

//spvc_capture_mode.
internal enum CaptureMode
{
	Copy = 0,
	TakeOwnership = 1,
}

//spvc_resource_type: the ones asked about.
internal enum ResourceType
{
	UniformBuffer = 1,
	StorageBuffer = 2,
	StageInput = 3,
	StageOutput = 4,
	SampledImage = 7,
	PushConstant = 9,
	SeparateImage = 10,
	SeparateSamplers = 11,
}

//SpvDecoration: the ones read.
internal enum Decoration
{
	Location = 30,
	Binding = 33,
	DescriptorSet = 34,
}

//spvc_compiler_option: the ones set.
internal enum Option
{
	FixupDepthConvention = 3 | CommonBit,
	FlipVertexY = 4 | CommonBit,
	GlslVersion = 8 | GlslBit,
	GlslEs = 9 | GlslBit,
	GlslVulkanSemantics = 10 | GlslBit,
	GlslEsDefaultFloatPrecisionHighp = 11 | GlslBit,
	GlslEsDefaultIntPrecisionHighp = 12 | GlslBit,
	GlslEmitPushConstantAsUniformBuffer = 33 | GlslBit,

	/// <summary>How many views a stage draws at once: two for a headset's eyes, which writes <c>GL_OVR_multiview2</c>, <c>layout(num_views)</c> and <c>gl_ViewID_OVR</c>. Nought, the default, is one view and no extension.</summary>
	GlslOvrMultiviewViewCount = 77 | GlslBit,

	CommonBit = 0x1000000,
	GlslBit = 0x2000000,
}

//spvc_reflected_resource.
[StructLayout( LayoutKind.Sequential )]
internal unsafe struct ReflectedResource
{
	public uint Id;
	public uint BaseTypeId;
	public uint TypeId;
	public byte* Name;
}

//spvc_combined_image_sampler.
[StructLayout( LayoutKind.Sequential )]
internal struct CombinedSampler
{
	public uint CombinedId;
	public uint ImageId;
	public uint SamplerId;
}
