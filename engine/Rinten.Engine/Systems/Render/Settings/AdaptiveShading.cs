namespace Rinten.Rendering;

//Variable rate shading where the last frame was smooth; needs VK_KHR_fragment_shading_rate
internal static class AdaptiveShading
{
	/// <summary>Shade the world coarser where the last frame was smooth.</summary>
	[ConVar( "r_adaptive_shading",
		Help = "Shade the world two pixels to a fragment wherever the last frame shows no difference the eye could see there." )]
	internal static bool Enabled { get; set; } = true;

	/// <summary>The largest change between neighbouring pixels a coarse fragment may hide, in steps of the display's eight bits. One is a step of the picture as it is stored - under what the eye tells apart anywhere.</summary>
	[ConVar( "r_adaptive_shading_step", Min = 0, Max = 16,
		Help = "How smooth a square of the last frame has to be to be shaded coarser, in 8-bit display steps between neighbours." )]
	internal static float Step { get; set; } = 1.0f;
}
