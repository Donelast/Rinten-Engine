using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rinten;

public partial class ModelDefinition
{
	[Hide, JsonIgnore] public override int ResourceVersion => 2;

	//v2 - The animation graph moves into the model: a named .ag file is read into Graph
	[Expose, JsonUpgrader( typeof( ModelDefinition ), 2 )]
	static void Upgrader_v2( JsonObject json )
	{
		if ( !json.TryGetPropertyValue( "AnimationGraph", out var named ) ) return;

		json.Remove( "AnimationGraph" );

		if ( json.TryGetPropertyValue( nameof( Graph ), out var carried ) && carried is JsonObject ) return;

		var path = named is JsonValue value && value.TryGetValue<string>( out var text ) ? text : null;
		if ( string.IsNullOrWhiteSpace( path ) ) return;

		var full = CoreModel.ResolveOnDisk( path.Replace( '\\', '/' ) );

		if ( full is null )
		{
			Log.Warning( $"{path}: the graph file a model named is not there - the model comes across without a graph" );
			return;
		}

		try
		{
			json[nameof( Graph )] = JsonNode.Parse( CompiledFile.TextOf( full ),
				documentOptions: new System.Text.Json.JsonDocumentOptions { MaxDepth = 512 } );
		}
		catch ( Exception e ) when ( e is System.IO.IOException or System.Text.Json.JsonException )
		{
			Log.Warning( $"{path}: the graph file a model named would not read - {e.Message}" );
		}
	}

	//v1 - Levels of detail are gone. What a model listed as its coarser levels becomes the list of
	//meshes not to compile in, so a model exported with three copies of itself still draws one.
	[Expose, JsonUpgrader( typeof( ModelDefinition ), 1 )]
	static void Upgrader_v1( JsonObject json )
	{
		if ( !json.TryGetPropertyValue( "Lods", out var node ) ) return;

		json.Remove( "Lods" );

		if ( node is not JsonArray levels || levels.Count < 2 ) return;

		var nearest = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var coarse = new List<string>();

		for ( var i = 0; i < levels.Count; i++ )
		{
			if ( levels[i] is not JsonObject level ) continue;
			if ( !level.TryGetPropertyValue( "Meshes", out var meshesNode ) ) continue;
			if ( meshesNode is not JsonArray meshes ) continue;

			foreach ( var mesh in meshes )
			{
				var name = mesh?.GetValue<string>();
				if ( string.IsNullOrWhiteSpace( name ) ) continue;

				if ( i == 0 ) nearest.Add( name );
				else coarse.Add( name );
			}
		}

		//A mesh the nearest level draws is drawn whatever else claims it.
		coarse.RemoveAll( nearest.Contains );

		if ( coarse.Count == 0 ) return;

		var array = new JsonArray();
		foreach ( var name in coarse.Distinct() ) array.Add( name );

		json["CoarseMeshes"] = array;
	}
}
