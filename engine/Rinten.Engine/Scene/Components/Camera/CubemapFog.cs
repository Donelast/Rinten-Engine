namespace Rinten;

//The same fog as the gradient one, with its colour taken from the sky - one fog in the frame, whichever of the two set it last. Rather than a blurred sky cube, this reads the table the sky itself is drawn from, which is smooth already.
/// <summary>Fog the colour of the sky behind it: distance and height fog like <see cref="GradientFog"/>, but each pixel is fogged towards the sky in the direction it is looked at through, so distant geometry dissolves into the horizon instead of into a flat colour.</summary>
[Expose]
[Title( "Cubemap Fog" )]
[Category( "Rendering" )]
[Icon( "foggy" )]
public class CubemapFog : Component, Component.ExecuteInEditor
{
	/// <summary>How far from the eye the fog starts, in metres.</summary>
	[Property]
	public float StartDistance { get; set; } = 0.25f;

	/// <summary>How far from the eye it reaches its full depth, in metres.</summary>
	[Property]
	public float EndDistance { get; set; } = 104.0f;

	/// <summary>How the distance fades it in - one is even, two is the square.</summary>
	[Property]
	public float FalloffExponent { get; set; } = 1.0f;

	/// <summary>How far above <see cref="HeightStart"/> the fog thins out to nothing, in metres. Nought is fog at every height.</summary>
	[Property]
	public float HeightWidth { get; set; } = 0.0f;

	/// <summary>The height the fog starts thinning from, in metres.</summary>
	[Property]
	public float HeightStart { get; set; } = 50.0f;

	/// <summary>How the height fades it out.</summary>
	[Property]
	public float HeightExponent { get; set; } = 2.0f;

	/// <summary>What the sky's colour is multiplied by; its alpha is how thick the fog gets.</summary>
	[Property]
	public Color Tint { get; set; } = Color.White;

	protected override void OnPreRender()
	{
		var world = Scene?.SceneWorld;
		if ( !world.IsValid() ) return;

		//No height band is fog at every height: a band far above anything there is, which the shader reads as below its start everywhere.
		var banded = HeightWidth > 0.0f;

		world.GradientFog = new Rendering.GradientFogSetup
		{
			Enabled = true,
			ColorFromSky = true,
			StartDistance = StartDistance,
			EndDistance = EndDistance,
			DistanceFalloffExponent = FalloffExponent,
			Color = Tint.WithAlpha( 1 ),
			MaximumOpacity = Tint.a,
			StartHeight = banded ? HeightStart : 1e9f,
			EndHeight = banded ? HeightStart + HeightWidth : 2e9f,
			VerticalFalloffExponent = HeightExponent,
		};
	}
}
