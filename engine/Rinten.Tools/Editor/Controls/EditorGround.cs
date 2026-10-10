using Rinten;

namespace Editor;

/// <summary>Window clear colour read from the stylesheet's $ground-bottom so it matches the panel background</summary>
public static class EditorGround
{
	/// <summary>What to clear a window to: the ground its panels lie on.</summary>
	public static Color ClearColour => Rinten.UI.EditorTokens.Colour( "ground", Color.Black );
}
