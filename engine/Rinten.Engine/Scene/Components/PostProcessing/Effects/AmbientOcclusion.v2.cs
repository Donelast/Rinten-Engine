using System.Text.Json.Nodes;

namespace Rinten;

public sealed partial class AmbientOcclusion
{
	[JsonUpgrader( typeof( AmbientOcclusion ), 2 )]
	static void Upgrader_v2( JsonObject obj )
	{
		obj.Remove( "Quality" );
	}

	[Expose, JsonUpgrader( typeof( AmbientOcclusion ), 1 )]
	static void Upgrader_v1( JsonObject obj )
	{
		//Remove old settings, we just want to use the defaults no point trying to find equivilents here
		obj.Remove( "Radius" );
		obj.Remove( "Intensity" );
	}
}
