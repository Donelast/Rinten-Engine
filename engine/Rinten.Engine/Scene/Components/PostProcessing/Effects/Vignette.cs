using Rinten.Rendering;

namespace Rinten;

/// <summary>Applies a vignette to the camera</summary>
[Title( "Vignette" )]
[Category( "Post Processing" )]
[Icon( "vignette" )]
public sealed class Vignette : BasePostProcess<Vignette>
{
	/// <summary>Not in a headset: centred on the picture, which in an eye is not where the lens is. See <see cref="BasePostProcess.AllowedInHeadset"/>.</summary>
	public override bool AllowedInHeadset => false;

	/// <summary>The color of the vignette or the "border"</summary>
	[Property] public Color Color { get; set; } = Color.Black;

	/// <summary>How strong the vignette is. This is a value between 0 -> 1</summary>
	[Property, Range( 0, 1 )] public float Intensity { get; set; } = 1.0f;

	/// <summary>How much fall off or how blurry the vignette is</summary>
	[Property, Range( 0, 1 )] public float Smoothness { get; set; } = 1.0f;

	/// <summary>How circular or round the vignette is</summary>
	[Property, Range( 0, 1 )] public float Roundness { get; set; } = 1.0f;

	/// <summary>The center of the vignette in relation to UV space. This means a value of {0.5, 0.5} is the center of the screen</summary>
	[Property] public Vector2 Center { get; set; } = new Vector2( 0.5f, 0.5f );
	/// <summary>A point effect after the curve - see vignette.shader - which the engine folds into the compose on its way past.</summary>
	public override void Render()
	{
		float intensity = GetWeighted( x => x.Intensity );
		if ( intensity.AlmostEqual( 0.0f ) ) return;

		var color = GetWeighted( x => x.Color );
		if ( color.a.AlmostEqual( 0.0f ) ) return;

		Attributes.Set( "Colour", new Vector3( color.r, color.g, color.b ) );
		Attributes.Set( "Intensity", intensity );
		Attributes.Set( "Smoothness", GetWeighted( x => x.Smoothness, onlyLerpBetweenVolumes: true ) );
		Attributes.Set( "Roundness", GetWeighted( x => x.Roundness, onlyLerpBetweenVolumes: true ) );
		Attributes.Set( "Center", GetWeighted( x => x.Center, onlyLerpBetweenVolumes: true ) );

		Apply( Shader, Stage.Tonemapping, 100, "Vignette" );
	}

	private static Material Shader => field ??= Material.FromShader( "shaders/postprocess/vignette.shader" );

}
