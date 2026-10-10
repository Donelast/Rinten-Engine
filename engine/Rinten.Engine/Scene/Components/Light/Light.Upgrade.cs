using System.Text.Json.Nodes;

namespace Rinten;

public abstract partial class Light
{
	public override int ComponentVersion => 1;

	//v1 - Mode enum became Static; Mixed/Baked map to Static, missing Mode (old default) to Realtime
	[Expose, JsonUpgrader( typeof( Light ), 1 )]
	static void Upgrader_v1( JsonObject obj )
	{
		var mode = obj.TryGetPropertyValue( "Mode", out var value )
			? value?.GetValue<string>()
			: "Realtime";

		obj["Static"] = !string.Equals( mode, "Realtime", System.StringComparison.OrdinalIgnoreCase );

		obj.Remove( "Mode" );
	}
}
