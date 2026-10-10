using System.Collections.Generic;

namespace Rinten.Rendering;

/// <summary>Which of the debug views this renderer can actually draw, and what they belong with.</summary>
public static class DebugViews
{
	//What each view is about, in the order the menu should show them.
	private static readonly (string Group, SceneCameraDebugMode Mode)[] known =
	{
		( null, SceneCameraDebugMode.Normal ),

		( "Lighting", SceneCameraDebugMode.FullBright ),
		( "Lighting", SceneCameraDebugMode.Diffuse ),
		( "Lighting", SceneCameraDebugMode.Reflect ),
		( "Lighting", SceneCameraDebugMode.AmbientOcclusion ),
		( "Lighting", SceneCameraDebugMode.SunShadowLevels ),

		( "Surface", SceneCameraDebugMode.Albedo ),
		( "Surface", SceneCameraDebugMode.Roughness ),
		( "Surface", SceneCameraDebugMode.NormalMap ),
		( "Surface", SceneCameraDebugMode.ShowUV ),
	};

	/// <summary>The views that draw something, each with the heading it belongs under - null for the ordinary picture, which belongs under nothing.</summary>
	public static IReadOnlyList<(string Group, SceneCameraDebugMode Mode)> Working => known;

	/// <summary>Whether this renderer knows how to draw this view.</summary>
	public static bool Works( SceneCameraDebugMode mode )
	{
		foreach ( var (_, one) in known )
		{
			if ( one == mode ) return true;
		}

		return false;
	}
}
