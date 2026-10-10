using System.Text.Json.Nodes;
namespace Rinten;

public partial class SceneFile
{
	//Version 3 to 4 - rename AnimationGraph2 key to AnimGraph everywhere, or graphs are dropped
	[Expose, JsonUpgrader( typeof( SceneFile ), 4 )]
	internal static void Upgrader_v4( JsonObject obj )
	{
		RenameGraphKey( obj );
	}

	//Walk an object, its children and its components, moving the old key onto the new one wherever it is found.
	internal static void RenameGraphKey( JsonObject obj )
	{
		if ( obj["Components"] is JsonArray components )
		{
			foreach ( var component in components )
			{
				if ( component is not JsonObject jso ) continue;

				if ( !jso.TryGetPropertyValue( "AnimationGraph2", out var graph ) ) continue;

				jso.Remove( "AnimationGraph2" );

				//A renderer saved after the rename wins: it already says what it plays, and the old key is whatever was there before.
				if ( jso.ContainsKey( "AnimGraph" ) ) continue;

				jso["AnimGraph"] = graph?.DeepClone();
			}
		}

		if ( obj["RootObject"] is JsonObject root )
		{
			RenameGraphKey( root );
		}

		foreach ( var key in new[] { "Children", "GameObjects" } )
		{
			if ( obj[key] is not JsonArray list ) continue;

			foreach ( var child in list )
			{
				if ( child is not JsonObject jso ) continue;

				RenameGraphKey( jso );
			}
		}
	}
}
