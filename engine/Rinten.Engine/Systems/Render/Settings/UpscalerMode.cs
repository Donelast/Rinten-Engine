namespace Rinten.Engine.Settings;

/// <summary>Upscaler choice, mirrors r_upscaling; temporal ones also anti-alias and need the full frame</summary>
public enum UpscalerMode
{
	/// <summary>Native resolution rendering, no upscaler.</summary>
	Off = 0,

	/// <summary>Render at lower resolution, bilinear blit to display resolution.</summary>
	Stretch = 1,

	/// <summary>AMD FidelityFX Super Resolution 1 — spatial upscaler (EASU + RCAS).</summary>
	FSR1 = 2,

	/// <summary>AMD FidelityFX Super Resolution 3 — temporal upscaler.</summary>
	FSR3 = 3,

	/// <summary>NVIDIA Deep Learning Super Sampling — temporal AI upscaler. NVIDIA hardware only.</summary>
	DLSS = 4,

	/// <summary>The engine's own temporal upscaler. At a render scale of one it is temporal anti-aliasing. Works on every device the full frame does.</summary>
	Temporal = 5,
}
