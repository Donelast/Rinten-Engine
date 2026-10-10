using NativeEngine;

namespace Rinten;

public static partial class Graphics
{
	/// <summary>Which method to use when downsampling a texture</summary>
	public enum DownsampleMethod
	{
		/// <summary>Uses a box filter to downsample the texture</summary>
		Box,
		/// <summary>Uses a gaussian filter to downsample the texture</summary>
		GaussianBlur,
		/// <summary>Uses a gaussian filter to downsample the texture, applies border to not oversample edges</summary>
		GaussianBorder,
		/// <summary>Downsamples the texture using a max operator filter ( brightest pixel )</summary>
		Max,
		/// <summary>Downsamples the texture using a min operator filter ( darkest pixel )</summary>
		Min,
		/// <summary>Downsamples the texture in red and green channels using a Min/Max filter ( darkest and brightest pixel )</summary>
		MinMax,
		Default = 0,
		None = -1
	};

	/// <summary>Generates mips on the GPU by linear 2x2 blits from initialMip; numMips &lt; 1 means all</summary>
	public static void GenerateMipMaps( Texture texture, DownsampleMethod downsampleMethod = DownsampleMethod.Default, int initialMip = 0, int numMips = -1 )
	{
		ArgumentNullException.ThrowIfNull( texture );
		AssertRenderBlock();

		var device = texture.core?.Device() ?? 0;
		if ( device == 0 ) return;

		CoreRenderDevice.GenerateTextureMips( device, Math.Max( initialMip, 0 ), numMips );
	}
}
