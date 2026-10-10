using Rinten.Rendering;

namespace Editor.SpriteEditor;

/// <summary>The four ways the editor can sample a sprite, as the View menu lists them and as the cookie remembers them.</summary>
public static class TextureFilterLevels
{
	public const string Cookie = "SpriteEditor.Antialiasing";
	//Off: a sprite is most often pixel art, and a preview that smooths it over shows a picture the game does not draw.
	public const int Default = 0;

	public static readonly IReadOnlyList<(string Name, FilterMode Mode)> All =
	[
		( "Off", FilterMode.Point ),
		( "Bilinear", FilterMode.Bilinear ),
		( "Trilinear", FilterMode.Trilinear ),
		( "Anisotropic", FilterMode.Anisotropic ),
	];

	public static FilterMode ToFilter( int level ) => level >= 0 && level < All.Count ? All[level].Mode : All[Default].Mode;
}
