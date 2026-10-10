using Rinten.Rendering;

namespace Rinten;

//Traces mirror rays through a nearest-depth chain into "Reflection", before sky and glass
/// <summary>Reflects what is on the screen in the surfaces that are mirrors, traced through the depth.</summary>
[Title( "Screen-Space Reflections" )]
[Category( "Post Processing" )]
[Icon( "local_mall" )]
public class ScreenSpaceReflections : BasePostProcess<ScreenSpaceReflections>
{
	[ConVar( "r_ssr_downsample_ratio", Help = "Default SSR resolution scale (0 = Disabled, 1 = Full, 2 = Quarter, 4 = Sixteeneth)." )]
	internal static int DownsampleRatio { get; set; } = 2;

	/// <summary>Whether the world reflects itself off screen-space rays.</summary>
	[ConVar( "r_ssr", Help = "Screen-space reflections (0 off)." )]
	internal static bool Reflections { get; set; } = true;

	//Each read covers a depth patch; 128 closes holes along grazed edges at little extra cost
	/// <summary>How many times a reflected ray reads the depth at most.</summary>
	[ConVar( "r_ssr_steps", Min = 8, Max = 128,
		Help = "How many times a reflected ray reads the depth at most." )]
	internal static int ReflectionSteps { get; set; } = 128;

	//The names the chain of the nearest depth and the copy of the solid world are found under - see CoreSceneDrawer.
	internal const string DepthPicture = "ReflectionDepth";
	internal const string ScenePicture = "ReflectionScene";

	//Half-size copy of the lit solid world for water/glass rays; only run when something reads it
	internal static Rendering.CommandList SceneCopy
	{
		get
		{
			if ( field is not null ) return field;

			var copy = new Rendering.CommandList( "Reflections scene" );
			var scene = copy.GetPicture( ScenePicture, PictureFormat.Hdr, sizeFactor: 2 );

			copy.SetRenderTarget( scene );
			copy.Blit( Trace, entry: "CopyPs" );
			copy.ClearRenderTarget();

			return field = copy;
		}
	}

	private static Material Trace => field ??= Material.FromShader( "shaders/postprocess/reflection.shader" );
	private static Material Lay => field ??= Material.FromShader( "shaders/postprocess/reflection_lay.shader" );

	public override void Render()
	{
		if ( DownsampleRatio < 1 || !Reflections ) return;

		var cl = NextList( "Reflections" );

		//The depth, then its chain of the nearest of every four - what the ray passes over whole patches of. Begun at half its size, the nearest
		//of each four already, as the reflection is drawn at half: a full copy and a chain from it were a quarter of a millisecond before a ray.
		var depths = cl.GetPicture( DepthPicture, PictureFormat.SingleFull, sizeFactor: 2, levels: 16 );

		cl.SetRenderTarget( depths );
		cl.Blit( Trace, entry: "NearestPs" );
		cl.ClearRenderTarget();
		cl.GenerateMipMaps( depths, Graphics.DownsampleMethod.Min );

		var reflection = cl.GetPicture( "Reflection", PictureFormat.Hdr,
			sizeFactor: Math.Clamp( DownsampleRatio, 1, 8 ) );

		cl.Attributes.Set( "Depths", depths.ColorTexture );
		cl.Attributes.Set( "Bias", 0.02f );
		cl.Attributes.Set( "Steps", (float)ReflectionSteps );
		cl.SetRenderTarget( reflection );
		cl.Blit( Trace );
		cl.ClearRenderTarget();

		InsertCommandList( cl, Stage.AfterOpaque, 50, "Reflections" );

		var lay = NextList( "Reflections lay" );

		lay.Attributes.Set( "Reflection", reflection.ColorTexture );
		lay.Attributes.Set( "Strength", 1.0f );
		lay.Blit( Lay );

		InsertCommandList( lay, Stage.AfterOpaque, 200, "Reflections lay" );
	}
}
