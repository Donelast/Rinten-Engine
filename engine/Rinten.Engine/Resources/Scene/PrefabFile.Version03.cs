using System.Text.Json.Nodes;

namespace Rinten;

public partial class PrefabFile
{
	//Version 2 to 3 - renderer graph property AnimationGraph2 renamed to AnimGraph, as in scenes
	[Expose, JsonUpgrader( typeof( PrefabFile ), 3 )]
	internal static void Upgrader_v3( JsonObject obj )
	{
		SceneFile.RenameGraphKey( obj );
	}
}
