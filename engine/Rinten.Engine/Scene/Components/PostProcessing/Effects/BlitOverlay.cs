using Rinten.Rendering;

namespace Rinten;

/// <summary>Draw a material over the screen</summary>
[Title( "Blit Overlay" )]
[Category( "Post Processing" )]
[Icon( "grain" )]
public sealed class BlitOverlay : BasePostProcess<BlitOverlay>
{
	[Range( 0, 1 )]
	[Property] public float Blend { get; set; } = 0.1f;

	//Three of the four are the blend the hardware does - see PassBlend, and the remarks there on why an effect that adds light cannot simply read the frame and add. Multiply has no such blend here and is left to the shader, which is handed the frame as SceneColour and can do what it likes with it.
	/// <summary>How it lands on the frame.</summary>
	[Property] public BlendMode BlendMode { get; set; } = BlendMode.Normal;

	[Property] public Material Material { get; set; }

	[Property] public int Order { get; set; }

	/// <summary>Over the finished, tonemapped frame.</summary>
	public override void Render()
	{
		if ( !Material.IsValid() ) return;

		float blend = GetWeighted( x => x.Blend );
		if ( blend.AlmostEqual( 0.0f ) ) return;

		Attributes.Set( "Blend", blend );
		Attributes.Set( "blend", blend );

		Blit( new BlitMode
		{
			Material = Material,
			RenderStage = Stage.AfterPostProcess,
			Order = Order,
			Blend = BlendMode switch
			{
				BlendMode.Lighten => PassBlend.Additive,
				BlendMode.PremultipliedAlpha => PassBlend.Screen,
				BlendMode.Multiply => PassBlend.Multiply,
				_ => PassBlend.Replace,
			},
		}, $"Overlay ({Material.Name})" );
	}

}
