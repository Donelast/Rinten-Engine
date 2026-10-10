using Rinten;

namespace Editor;

//One shared whole-window opacity for every editor window; Vulkan can't do per-pixel alpha here
internal static class EditorGlass
{
	//For windows without an alpha of their own. It thins the whole window, writing included, so it is kept close to solid; a window that wants to be seen through properly asks for glass when it is opened and gets it per pixel instead.
	/// <summary>How solid a window is - one for all of them.</summary>
	public const float Opacity = 0.96f;

	//The rounding is a shape cut out of the window by the window system and cut to the size the window was when it was asked for - so a window somebody can resize is left square rather than clipped to a shape it has outgrown.
	/// <summary>How round the corners are, where a window is a fixed size.</summary>
	public const float Radius = 10.0f;

	/// <summary>Makes a window glass. Square, because most windows can be resized.</summary>
	public static void Apply( CoreUiWindow shell, float radius = 0.0f )
	{
		if ( shell is null ) return;

		//A window that is already see-through per pixel wants nothing from this. One transparency over the whole
		//of it would be a second glass on top of the first, and the only thing it would add is thinner writing.
		if ( shell.Composited ) return;

		shell.MakeGlass( Opacity, radius );
	}

	//No Opacity. This one is see-through per pixel rather than as a whole, so the writing on it stays as solid as it was written - which is the thing the single-transparency version cannot do.
	/// <summary>The whole window is glass: only what it draws is there, and it draws nothing where it clears to black.</summary>
	public static void ApplySheet( CoreUiWindow shell, float radius = 0.0f )
	{
		shell?.MakeSheet( radius );
	}

}
