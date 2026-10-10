namespace Rinten;

/// <summary>A texture in our own editable format: an image plus how it is meant to be used.</summary>
[AssetType( Name = "Texture", Extension = "tex", Category = "Rendering", IconColor = "#d68f5c", Icon = "image" )]
public sealed class TextureDefinition : GameResource
{
	/// <summary>The image this is built from, left where the artist put it.</summary>
	[Property, TextureImagePath]
	public string Source { get; set; } = "";

	/// <summary>Whether the image is gamma-encoded: true for colours, false for normals, roughness, masks.</summary>
	[Property, Title( "Is Color" )]
	public bool Srgb { get; set; } = true;

	/// <summary>Build the smaller versions. Without them a surface seen at an angle sparkles, and every one of them together costs a third again as much memory as the largest.</summary>
	[Property]
	public bool GenerateMips { get; set; } = true;

	//-- sampling -------------------------------------------------------------

	[Property, Group( "Sampling" )]
	public TextureFilter Filter { get; set; } = TextureFilter.Trilinear;

	[Property, Group( "Sampling" )]
	public TextureWrap Wrap { get; set; } = TextureWrap.Repeat;

	/// <summary>How a texture is read between its pixels.</summary>
	public enum TextureFilter
	{
		/// <summary>Nearest pixel. For pixel art and lookup tables.</summary>
		Point,

		/// <summary>Blended within a level.</summary>
		Bilinear,

		/// <summary>Blended between levels as well. The usual answer.</summary>
		Trilinear,

		/// <summary>Trilinear, and more samples on surfaces seen at an angle.</summary>
		Anisotropic,
	}

	/// <summary>What happens past the edge.</summary>
	public enum TextureWrap
	{
		Repeat,
		Clamp,
		Mirror,
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
		=> EssResource.DrawGlyph( "image", width, height, "#ffc38f" );
}
