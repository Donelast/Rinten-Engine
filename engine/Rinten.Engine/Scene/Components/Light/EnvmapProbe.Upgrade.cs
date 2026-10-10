using System.Text.Json.Nodes;

namespace Rinten;

public partial class EnvmapProbe
{
	public override int ComponentVersion => 1;

	//v1 - Changed RenderDynamically bool to Mode enum.
	[Expose, JsonUpgrader( typeof( EnvmapProbe ), 1 )]
	static void Upgrader_v1( JsonObject obj )
	{
		if ( obj.TryGetPropertyValue( "RenderDynamically", out var renderDynamically ) )
		{
			var isDynamic = (bool)renderDynamically;
			obj["Mode"] = isDynamic ? "Realtime" : "CustomTexture";
		}
	}
}
