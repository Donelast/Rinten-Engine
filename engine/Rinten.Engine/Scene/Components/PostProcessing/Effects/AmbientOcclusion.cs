using Rinten.Rendering;

namespace Rinten;

//Draws "Occlusion" after the depth pre-pass and surfaces darken by it; fixed 35cm reach
/// <summary>Screen space ambient occlusion: darkens corners, crevices and surfaces close to each other.</summary>
[Expose]
[Title( "Ambient Occlusion (SSAO)" )]
[Category( "Post Processing" )]
[Icon( "contrast" )]
public sealed partial class AmbientOcclusion : BasePostProcess<AmbientOcclusion>
{
	public override int ComponentVersion => 1;

	[ConVar( "r_ao_quality", Min = 0, Max = 3, Help = "Ambient occlusion quality (0: off, 1: low, 2: med, 3: high)" )]
	internal static int UserQuality { get; set; } = 3;

	/// <summary>The intensity of the darkening effect. Has no impact on performance.</summary>
	[Property, Range( 0, 1 ), Category( "Properties" )]
	public float Intensity { get; set; } = 1.0f;

	/// <summary>Run ambient occlusion at a reduced resolution to save GPU time. The AO texture is sampled with bilinear filtering when applied to the scene.</summary>
	[ConVar( "r_ao_resolution", Min = 1, Max = 8, Help = "Ambient occlusion resolution scale divisor (1: full, 2: half, 4: quarter, 8: eighth)" )]
	internal static int UserResolution { get; set; } = 2;

	/// <summary>Number of same-frame spatial (À-Trous) denoise passes. Each pass doubles the sample step (1, 2, 4).</summary>
	[ConVar( "r_ao_denoise_passes", Min = 1, Max = 3, Help = "Number of spatial (À-Trous) denoise passes (1-3)" )]
	internal static int UserDenoisePasses { get; set; } = 2;

	private static Material Occlusion => field ??= Material.FromShader( "shaders/postprocess/ambient_occlusion.shader" );

	public override void Render()
	{
		if ( UserQuality <= 0 ) return;

		var intensity = Math.Clamp( GetWeighted( x => x.Intensity, 1.0f ), 0.0f, 1.0f );
		if ( intensity <= 0.0f ) return;

		var cl = NextList( "Ambient Occlusion" );
		var scale = Math.Clamp( UserResolution, 1, 8 );

		//Occlusion and bent normal from depth, packed into four 8-bit channels
		var raw = cl.GetPicture( "OcclusionRaw", PictureFormat.Colour, sizeFactor: scale );

		cl.Attributes.Set( "Radius", 0.5f );
		cl.Attributes.Set( "Slices", (float)( UserQuality switch { 1 => 1, 2 => 2, _ => 2 } ) );
		cl.Attributes.Set( "Steps", (float)( UserQuality switch { 1 => 3, 2 => 3, _ => 4 } ) );
		cl.SetRenderTarget( raw );
		cl.Blit( Occlusion );
		cl.ClearRenderTarget();

		//Softened: the first run averages the four-by-four square the directions' turns repeat over, which is all the pattern needs,
		//in four reads; each further run reaches twice as far. The last writes the picture the world reads, with the intensity in it.
		var passes = Math.Clamp( UserDenoisePasses, 1, 3 );
		var soft = passes > 1 ? cl.GetPicture( "OcclusionSoft", PictureFormat.Colour, sizeFactor: scale ) : raw;
		var result = cl.GetPicture( "Occlusion", PictureFormat.Colour, sizeFactor: scale );

		var from = raw;

		for ( var pass = 0; pass < passes; pass++ )
		{
			var last = pass == passes - 1;
			var into = last ? result : ( pass & 1 ) == 0 ? soft : raw;

			cl.Attributes.Set( "Source", from.ColorTexture );
			cl.Attributes.Set( "Reach", (float)( 1 << pass ) );
			cl.Attributes.Set( "Strength", last ? intensity : 1.0f );
			cl.SetRenderTarget( into );
			cl.Blit( Occlusion, entry: "SoftenPs" );
			cl.ClearRenderTarget();

			from = into;
		}

		InsertCommandList( cl, Stage.AfterDepthPrepass, 0, "Ambient Occlusion" );
	}
}
