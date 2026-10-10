namespace Rinten;

//The frame's shape on this device (full or portable), independent of which backend draws it
internal enum RenderTier
{
	/// <summary>Everything: the world drawn on the card through clusters and compute, a table of every picture indexed from the shader, shadow maps, the frame's own pictures and the passes over them. Vulkan 1.3, which is every desktop and every phone of the last few years.</summary>
	Full = 0,

	/// <summary>The minimal frame every device can draw. WebGPU, and Vulkan from 1.1</summary>
	Portable = 1,
}
