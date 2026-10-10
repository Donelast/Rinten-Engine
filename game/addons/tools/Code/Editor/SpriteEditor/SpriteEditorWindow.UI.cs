namespace Editor.SpriteEditor;

/// <summary>How the preview samples the sprite.</summary>
public sealed partial class SpriteEditorWindow
{
	//Read from TextureFilterLevels.Cookie when the window opens and written back to it when the window closes. The preview's own button changes it.
	/// <summary>How the preview samples the sprite - an index into <see cref="TextureFilterLevels.All"/>.</summary>
	public int Antialiasing
	{
		get => _scene.FilterLevel;
		set => _scene.FilterLevel = value;
	}
}
