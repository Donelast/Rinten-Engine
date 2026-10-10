using Rinten.Rendering;

namespace Rinten;

/// <summary>Applies a blur effect to the camera.</summary>
[Title( "Blur" )]
[Category( "Post Processing" )]
[Icon( "lens_blur" )]
public sealed class Blur : BasePostProcess<Blur>
{
	[Range( 0, 1 ), Property] public float Size { get; set; } = 1.0f;

	//Four taps against the frame's mip chain, see blur.shader
	private static Material Shader => field ??= Material.FromShader( "shaders/postprocess/blur.shader" );

	public override void Render()
	{
		float size = GetWeighted( x => x.Size );
		if ( size <= 0f ) return;

		Attributes.Set( "size", size );

		//The chain of halvings is what this reads, so the picture it is handed has to have one built - that is what the flag asks for.
		Blit( new BlitMode
		{
			Material = Shader,
			RenderStage = Stage.BeforePostProcess,
			Order = 4000,
			WantsFrameMips = true,
		}, "Blur" );
	}
}
