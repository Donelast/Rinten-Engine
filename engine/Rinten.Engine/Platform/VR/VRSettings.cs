namespace Rinten.VR;

/// <summary>What a game may ask of the headset, and what it may ask about one.</summary>
public static class VRSettings
{
	/// <summary>Whether there is a headset to be entered, session or no. On a page this is false until the browser has answered and the player has allowed it, so it is read each frame rather than once.</summary>
	public static bool HeadsetAvailable => VRSystem.HeadsetAvailable;

	/// <summary>Whether the game is being drawn to a headset right now.</summary>
	public static bool InHeadset => VRSystem.IsActive;

	/// <summary>What the headset has to say: why there is none, or how a session that opened came to close. Worth putting on the screen - a browser's console cannot be read with a headset on.</summary>
	public static string Message => VRSystem.HeadsetMessage;

	/// <summary>Ask for the headset, for a game drawing its own way in. In a page a session may only be opened during a gesture, so a wish made outside one is remembered and taken on the next click. Does nothing where a session is not the player's to ask for, which is every desktop runtime.</summary>
	public static void Enter() => VRSystem.RequestHeadset();

	/// <summary>Leave the headset and go back to the screen.</summary>
	public static void Leave() => VRSystem.DismissHeadset();

	/// <summary>The headset's refresh, or nought where it has not said.</summary>
	public static float RefreshRate => VRSystem.RefreshRate;

	/// <summary>How many pixels each eye is drawn at.</summary>
	public static Vector2 EyeSize => VRSystem.EyeRenderTargetSize;

	/// <summary>How a page's eyes reach the headset (layer type, single or multi-pass); empty outside a web session</summary>
	public static string DrawPath => !OperatingSystem.IsBrowser() || !VRSystem.IsActive ? ""
		: ( BrowserNative.XrLayered() ? "projection layer" : "copied into a layer" )
			+ ( CoreRender.Multiview ? ", both eyes in one pass" : ", an eye at a time" );

	/// <summary>Between the eyes, in metres.</summary>
	public static float EyeDistance => VRSystem.IPDMetres;

	public static float RenderScale
	{
		get => VRSystem.RenderScale;
		set => VRSystem.RenderScale = value;
	}

	public static float Foveation
	{
		get => VRSystem.Foveation;
		set => VRSystem.Foveation = value;
	}

	public static bool MirrorToScreen
	{
		get => VRSystem.MirrorToScreen;
		set => VRSystem.MirrorToScreen = value;
	}
}
