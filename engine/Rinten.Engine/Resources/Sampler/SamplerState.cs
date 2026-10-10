using NativeEngine;
using System.Collections.Concurrent;

namespace Rinten.Rendering;

/// <summary>How textures are sampled in shaders: filtering, address modes and anisotropy.</summary>
[Expose]
public record struct SamplerState
{
	/// <summary>The texture filtering mode used for sampling (e.g., point, bilinear, trilinear).</summary>
	[KeyProperty]
	public FilterMode Filter { get; set; } = FilterMode.Bilinear;

	/// <summary>The addressing mode used for the U (X) texture coordinate.</summary>
	public TextureAddressMode AddressModeU { get; set; } = TextureAddressMode.Wrap;

	/// <summary>The addressing mode used for the V texture coordinate.</summary>
	public TextureAddressMode AddressModeV { get; set; } = TextureAddressMode.Wrap;

	/// <summary>The addressing mode used for the W texture coordinate.</summary>
	public TextureAddressMode AddressModeW { get; set; } = TextureAddressMode.Wrap;

	/// <summary>The bias applied to the calculated mip level during texture sampling. Positive values make textures appear blurrier; negative values sharpen.</summary>
	public float MipLodBias { get; set; } = 0;

	/// <summary>The maximum anisotropy level used for anisotropic filtering. Higher values improve texture quality at oblique viewing angles.</summary>
	public int MaxAnisotropy { get; set; } = 8;

	/// <summary>Border color to use if <see cref="TextureAddressMode.Border"/> is specified for AddressU, AddressV, or AddressW.</summary>
	public Color BorderColor { get; set; } = Color.Transparent;

	public SamplerState() { }

	//Bindless index for this sampler state, for use with Bindless::GetSampler( int nIndex ) in shaders.
	[Hide] public int Index => GetBindlessIndex( this );

	static internal ConcurrentDictionary<SamplerState, int> _cachedSamplerIndex = [];

	//Gets or creates a bindless sampler index for this SamplerState.
	internal static int GetBindlessIndex( SamplerState samplerState )
	{
		if ( _cachedSamplerIndex.TryGetValue( samplerState, out int value ) )
		{
			return value; //We already know the bindless index
		}

		//The index is into their device's table of samplers. Ours binds a sampler with the texture on every
		//draw and has no table, so nought - which is what a shader of ours reads as "the ordinary one".
		return 0;
	}
}
