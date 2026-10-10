using Rinten.Rendering;

namespace Rinten;

/// <summary>Applies a chromatic aberration effect to the camera</summary>
[Title( "Chromatic Aberration" )]
[Category( "Post Processing" )]
[Icon( "zoom_out_map" )]
public sealed class ChromaticAberration : BasePostProcess<ChromaticAberration>
{
	/// <summary>Not in a headset: centred on the picture, which in an eye is not where the lens is - and the lens adds its own. See <see cref="BasePostProcess.AllowedInHeadset"/>.</summary>
	public override bool AllowedInHeadset => false;

	/// <summary>Strength of the chromatic aberration effect</summary>
	[Property, Range( 0, 1 )] public float Scale { get; set; } = 0.33f;

	/// <summary>The pixel offset for each color channel. These values should be very small as it's in UV space. (0.004 for example) X = Red Y = Green Z = Blue</summary>
	[Property] public Vector3 Offset { get; set; } = new Vector3( 6f, 2f, 4.0f );
	/// <summary>A pass over the finished picture - see chromatic.shader: each channel is read somewhere else, which a point effect may not do.</summary>
	public override void Render()
	{
		float scale = GetWeighted( x => x.Scale );
		Vector3 offset = GetWeighted( x => x.Offset );

		if ( scale <= 0f )
			return;

		Attributes.Set( "Shift", offset * ( scale / 1000.0f ) );

		Blit( BlitMode.Simple( Shader, Stage.AfterPostProcess, -100 ), "Chromatic aberration" );
	}

	private static Material Shader => field ??= Material.FromShader( "shaders/postprocess/chromatic.shader" );

}
