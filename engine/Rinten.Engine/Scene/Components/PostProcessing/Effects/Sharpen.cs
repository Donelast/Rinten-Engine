using Rinten.Rendering;

namespace Rinten;

/// <summary>Applies a sharpen effect to the camera</summary>
[Title( "Sharpen" )]
[Category( "Post Processing" )]
[Icon( "deblur" )]
public sealed class Sharpen : BasePostProcess<Sharpen>
{
	[Range( 0, 5 )]
	[Property] public float Scale { get; set; } = 2;

	[Range( 0, 5 )]
	[Property] public float TexelSize { get; set; } = 1;
	/// <summary>A pass over the finished picture at the window's size - see sharpen.shader.</summary>
	public override void Render()
	{
		float scale = GetWeighted( x => x.Scale );

		if ( scale <= 0f )
			return;

		Attributes.Set( "Strength", scale );
		Attributes.Set( "TexelSize", GetWeighted( x => x.TexelSize ) );

		Blit( BlitMode.Simple( Shader, Stage.AfterPostProcess, -50 ), "Sharpen" );
	}

	private static Material Shader => field ??= Material.FromShader( "shaders/postprocess/sharpen.shader" );

}
