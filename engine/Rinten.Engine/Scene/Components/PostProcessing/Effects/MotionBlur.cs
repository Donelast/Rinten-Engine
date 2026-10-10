using Rinten.Rendering;

namespace Rinten;

/// <summary>Applies a motion blur effect to the camera</summary>
[Title( "Motion Blur" )]
[Category( "Post Processing" )]
[Icon( "animation" )]
public sealed class MotionBlur : BasePostProcess<MotionBlur>
{
	/// <summary>Not in a headset: blur that follows the head turning makes people sick. See <see cref="BasePostProcess.AllowedInHeadset"/>.</summary>
	public override bool AllowedInHeadset => false;

	[ConVar( "r_motionblur_scale", ConVarFlags.Saved, Help = "Enable or disable motion blur effect." )]
	internal static float UserScale { get; set; } = 1.0f;

	[ConVar( "r_motionblur_quality", Min = 0, Max = 3, Help = "0: low, 1: medium, 2: high (defualt), 3: very high" )]
	internal static int Quality { get; set; } = 2;

	[Range( 0, 1 ), Property] public float Scale { get; set; } = 0.05f;

	/// <summary>How much to hold the middle of the frame sharp and drive the smear out to the edges, which is the shape a lens gives it.</summary>
	[Range( 0, 1 ), Property] public float EdgeBias { get; set; } = 0.0f;

	/// <summary>The longest smear, in pixels of the frame. Without a ceiling a fast turn asks for a streak across the screen rather than a blur.</summary>
	[Range( 4, 256 ), Property] public float MaxBlur { get; set; } = 64.0f;

	/// <summary>Nearer than this, in metres, the frame is held sharp - a first person cabin, a viewmodel, hands on a wheel. Nothing at all at zero.</summary>
	[Range( 0, 20 ), Property] public float NearSharp { get; set; } = 0.0f;

	/// <summary>The pass that does it. A project may put its own material here; what it is handed is the frame, the depth and the velocities - see motionblur.shader, which is the one to start from.</summary>
	[Property] public Material Shader { get; set; }

	private static Material Default => field ??= Material.FromShader( "shaders/postprocess/motionblur.shader" );

	//Taps per quality level; runs first in the chain so bloom and tonemap see the smeared frame
	private static readonly int[] Samples = [4, 8, 12, 16];

	public override void Render()
	{
		var scale = GetWeighted( x => x.Scale ) * UserScale;

		if ( scale <= 0.0f || Quality <= 0 ) return;

		var material = Shader.IsValid() ? Shader : Default;

		if ( !material.IsValid() ) return;

		//The property is a fraction of a second of shutter, and one frame of travel is what the velocities hold - so the number the shader wants is
		//how many frames of travel to smear over. Twenty is what the old scale of 0.05 meant, which keeps a scene tuned against it looking the same.
		Attributes.Set( "Strength", scale * 20.0f );
		Attributes.Set( "Samples", (float)Samples[Math.Clamp( Quality, 0, 3 )] );
		Attributes.Set( "EdgeBias", GetWeighted( x => x.EdgeBias, 0.0f, onlyLerpBetweenVolumes: true ) );
		Attributes.Set( "MaxBlur", GetWeighted( x => x.MaxBlur, 64.0f, onlyLerpBetweenVolumes: true ) );
		Attributes.Set( "NearSharp", GetWeighted( x => x.NearSharp, 0.0f, onlyLerpBetweenVolumes: true ) );

		Blit( new BlitMode { Material = material, RenderStage = Stage.BeforePostProcess, Order = 50, WantsMotion = true }, "MotionBlur" );
	}
}
