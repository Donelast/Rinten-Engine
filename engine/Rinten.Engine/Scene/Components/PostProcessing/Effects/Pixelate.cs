using Rinten.Rendering;

namespace Rinten;

/// <summary>Applies a pixelate effect to the camera</summary>
[Title( "Pixelate" )]
[Category( "Post Processing" )]
[Icon( "apps" )]
public sealed class Pixelate : BasePostProcess<Pixelate>
{
	[Range( 0, 1 )]
	[Property] public float Scale { get; set; } = 0.25f;
	/// <summary>A point effect that moves where the frame is read - see pixelate.shader.</summary>
	public override void Render()
	{
		float scale = GetWeighted( x => x.Scale );
		if ( scale.AlmostEqual( 0.0f ) ) return;

		//How many pixels a block is. At nothing it is one, which is the picture unchanged rather than a picture of one colour.
		Attributes.Set( "Block", MathF.Max( 1.0f, Math.Clamp( scale, 0.0f, 1.0f ) * 64.0f ) );

		Apply( Shader, Stage.Tonemapping, -200, "Pixelate" );
	}

	private static Material Shader => field ??= Material.FromShader( "shaders/postprocess/pixelate.shader" );

}
