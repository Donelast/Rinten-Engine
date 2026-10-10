namespace Rinten.Rendering;

//Max uploaded texture size; bigger mipped textures start at the first mip that fits. New loads only
internal static partial class TextureSettings
{
	//A float because the quality profile writes it as one - it came from an engine where this was a streaming resolution rather than a cap.
	/// <summary>The biggest edge a texture may be uploaded at. Zero is no limit.</summary>
	[ConVar( "r_texture_stream_max_resolution", Min = 0, Max = 16384,
		Help = "The biggest edge a texture is uploaded at; 0 is no limit." )]
	internal static float MaxResolution { get; set; } = 8192.0f;

	/// <summary>Whether images compiled from PNG/JPEG are BC-compressed; imported content passes through</summary>
	[ConVar( "r_texture_compress", Help = "Pack compiled images into blocks (0 keeps them as pixels)." )]
	internal static bool Compress { get; set; } = true;

	/// <summary>Anisotropic filtering taps (1 is off); applies to loaded materials, clamped to the device</summary>
	[ConVar( "r_max_anisotropy", Min = 1, Max = 16,
		Help = "How many taps a material's sampler spends across the angle; 1 is off." )]
	internal static float Anisotropy { get; set; } = 16.0f;
}
