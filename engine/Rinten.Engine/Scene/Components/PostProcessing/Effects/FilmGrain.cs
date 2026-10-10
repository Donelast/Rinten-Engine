using Rinten.Rendering;

namespace Rinten;

/// <summary>Applies a film grain effect to the camera</summary>
[Title( "FilmGrain" )]
[Category( "Post Processing" )]
[Icon( "grain" )]
public sealed class FilmGrain : BasePostProcess<FilmGrain>
{
	/// <summary>Not in a headset: noise that differs between the eyes shimmers. See <see cref="BasePostProcess.AllowedInHeadset"/>.</summary>
	public override bool AllowedInHeadset => false;

	[Range( 0, 1 )]
	[Property] public float Intensity { get; set; } = 0.1f;

	[Range( 0, 1 )]
	[Property] public float Response { get; set; } = 0.5f;

	/// <summary>A point effect laid last, over the finished picture - see filmgrain.shader.</summary>
	public override void Render()
	{
		float intensity = GetWeighted( x => x.Intensity );
		if ( intensity.AlmostEqual( 0.0f ) ) return;

		Attributes.Set( "Intensity", intensity );
		Attributes.Set( "Response", GetWeighted( x => x.Response, 1 ) );

		//A different pattern every frame, and a small number, for the hash's sake.
		Attributes.Set( "Time", (float)( RealTime.Now % 1000.0 ) );

		Apply( Shader, Stage.Tonemapping, 200, "Film grain" );
	}

	private static Material Shader => field ??= Material.FromShader( "shaders/postprocess/filmgrain.shader" );

}
