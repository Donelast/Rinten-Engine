namespace Rinten.Rendering;

/// <summary>Gives global access to the texture streaming system.</summary>
public static class TextureStreaming
{
	/// <summary>Runs the action with streaming off so new textures load whole; streamed ones keep their mips</summary>
	public static void ExecuteWithDisabled( Action action ) => CoreTextureStreaming.Disabled( action );
}
