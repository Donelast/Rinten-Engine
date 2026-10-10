using Rinten.Rendering;
using System.Text.Json.Nodes;

namespace Rinten;

/// <summary>Applies a tonemapping effect to the camera.</summary>
[Title( "Tone Mapping" )]
[Category( "Post Processing" )]
[Icon( "exposure" )]
public class Tonemapping : BasePostProcess<Tonemapping>
{
	/// <summary>Options to select a tonemapping algorithm to use for color grading.</summary>
	public enum TonemappingMode
	{
		/// <summary>John Hable's filmic tonemapping algorithm. Uses the default curve based on Uncharted 2.</summary>
		HableFilmic = 1,
		/// <summary>The most realistic tonemapper at handling bright light, desaturating light as it becomes brighter. This is slightly more expensive than other options.</summary>
		ACES,
		/// <summary>Reinhard's tonemapper, which is a simple and fast tonemapper.</summary>
		ReinhardJodie,
		/// <summary>Linear: the colour as it was shaded, taken to the screen unchanged.</summary>
		Linear,
		/// <summary>Similar to ACES - very realistic, but handles lower and higher brightness ranges better. Uses the Punchy AgX look.</summary>
		AgX,

		//Uses the project's shaders/tonemap.slang EngineTonemap(colour, mode) if present, else the default
		/// <summary>Whatever the project's own curve does with it.</summary>
		Custom
	}

	/// <summary>Which tonemapping algorithm to use for color grading.</summary>
	[Property] public TonemappingMode Mode { get; set; } = TonemappingMode.HableFilmic;

	private static Material Shader => field ??= Material.FromShader( "shaders/postprocess/tonemapping.shader" );

	/// <summary>Lays tonemapping.shader at the tonemap stage with this curve; see CorePointChain.WithCurve</summary>
	public override void Render()
	{
		Attributes.Set( "Mode", (float)(int)Mode );

		Apply( Shader, Stage.Tonemapping, 0, "Tonemapping" );
	}

	public override int ComponentVersion => 3;

	//Remap the old "Legacy" mode to HableFilmic
	[Expose, JsonUpgrader( typeof( Tonemapping ), 2 )]
	static void Upgrader_v2( JsonObject obj )
	{
		//Remap the old "Legacy" mode to HableFilmic
		if ( obj.TryGetPropertyValue( "Mode", out var mode ) )
		{
			if ( mode.ToString() == "Legacy" )
				obj["Mode"] = (int)TonemappingMode.HableFilmic;
		}
	}

}
