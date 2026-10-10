using Rinten.Rendering;

namespace Rinten;

/// <summary>Applies color adjustments to the camera.</summary>
[Title( "Color Adjustments" )]
[Category( "Post Processing" )]
[Icon( "palette" )]
public sealed class ColorAdjustments : BasePostProcess<ColorAdjustments>
{
	[Range( 0, 1 ), Property] public float Blend { get; set; } = 1.0f;
	[Range( 0, 2 ), Property] public float Saturation { get; set; } = 1.0f;
	[Range( 0, 360 ), Property] public float HueRotate { get; set; } = 0.0f;
	[Range( 0, 2 ), Property] public float Brightness { get; set; } = 1.0f;
	[Range( 0, 2 ), Property] public float Contrast { get; set; } = 1.0f;

	/// <summary>A point effect in front of the curve - see coloradjust.shader.</summary>
	public override void Render()
	{
		var blend = GetWeighted( x => x.Blend, 0 );
		if ( blend <= 0.0f ) return;

		Attributes.Set( "Blend", blend );
		Attributes.Set( "Saturation", GetWeighted( x => x.Saturation, 1 ) );
		Attributes.Set( "Hue", GetWeighted( x => x.HueRotate, 0, true ) * ( MathF.PI / 180.0f ) );
		Attributes.Set( "Brightness", GetWeighted( x => x.Brightness, 1 ) );
		Attributes.Set( "Contrast", GetWeighted( x => x.Contrast, 1 ) );

		Apply( Shader, Stage.Tonemapping, -100, "Color adjustments" );
	}

	private static Material Shader => field ??= Material.FromShader( "shaders/postprocess/coloradjust.shader" );

}
