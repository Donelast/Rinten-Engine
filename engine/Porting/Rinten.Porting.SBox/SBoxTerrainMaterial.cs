using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rinten.Porting.SBox;

//Drops a terrain's override material while its shader is missing, so the painted layers show
internal static class SBoxTerrainMaterial
{
	//The property a terrain paints itself whole with.
	private const string Override = "MaterialOverride";

	/// <summary>Take the override off every terrain in this document whose material has no shader to draw with, and say how many were freed.</summary>
	public static int Strip( string scene, string assets, string named, PortReport report )
	{
		JsonNode root;

		try
		{
			root = JsonNode.Parse( File.ReadAllText( scene ) );
		}
		catch ( Exception )
		{
			//Whatever tried to convert this file has already said so.
			return 0;
		}

		if ( root is null ) return 0;

		var freed = 0;

		foreach ( var terrain in Terrains( root ) )
		{
			//A plain path, or {"Id": guid, "Path": path} as s&box writes a resource reference since
			//157c7fae9. GetValue<string> threw on the second and took the whole port down with it.
			var material = terrain[Override] switch
			{
				JsonValue value when value.TryGetValue<string>( out var text ) => text,
				JsonObject reference when reference["Path"] is JsonValue held && held.TryGetValue<string>( out var text ) => text,
				_ => null,
			};

			if ( string.IsNullOrWhiteSpace( material ) ) continue;
			if ( Draws( assets, material ) ) continue;

			terrain[Override] = "";

			report.Notes.Add( $"{named}: the terrain's own material {material} has no shader here yet, " +
				"so it is drawn with its layers until one is written" );

			++freed;
		}

		if ( freed == 0 ) return 0;

		try
		{
			File.WriteAllText( scene,
				root.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) );
		}
		catch ( IOException )
		{
			return 0;
		}

		return freed;
	}

	//Every terrain component in the document, whatever it is nested in.
	private static IEnumerable<JsonObject> Terrains( JsonNode node )
	{
		switch ( node )
		{
			case JsonObject o:
				if ( o["__type"]?.GetValue<string>() is { } type
					&& type.EndsWith( ".Terrain", StringComparison.OrdinalIgnoreCase ) )
					yield return o;

				foreach ( var (_, value) in o )
				{
					if ( value is null ) continue;

					foreach ( var found in Terrains( value ) ) yield return found;
				}

				yield break;

			case JsonArray a:
				foreach ( var value in a )
				{
					if ( value is null ) continue;

					foreach ( var found in Terrains( value ) ) yield return found;
				}

				yield break;
		}
	}

	//Whether the material's shader file exists and is ours; checked by content, both use .shader
	private static bool Draws( string assets, string material )
	{
		var file = Path.Combine( assets, material.Replace( '/', Path.DirectorySeparatorChar ) );

		if ( !File.Exists( file ) ) return false;

		string shader;

		try
		{
			shader = JsonNode.Parse( File.ReadAllText( file ) )?["Shader"]?.GetValue<string>();
		}
		catch ( Exception )
		{
			return false;
		}

		//Nothing named is the standard surface, which always draws.
		if ( string.IsNullOrWhiteSpace( shader ) ) return true;

		var written = Path.Combine( assets, shader.Replace( '/', Path.DirectorySeparatorChar ) );

		return File.Exists( written ) && !IsTheirs( written );
	}

	//Whether a shader file is their HLSL rather than ours; duplicated since the pipeline is editor-only
	private static bool IsTheirs( string file )
	{
		string text;

		try
		{
			text = File.ReadAllText( file );
		}
		catch ( Exception )
		{
			//Unreadable is not drawable either, whichever it is.
			return true;
		}

		//Ours, whatever else is in it: a Slang include is the one thing theirs never has.
		if ( text.Contains( ".slang\"", StringComparison.Ordinal ) ) return false;

		//As block headers - each on a line of its own, which is how their format writes them and how nothing of ours ever would.
		foreach ( var line in text.Split( '\n' ) )
		{
			if ( line.Trim() is "MODES" or "FEATURES" or "COMMON" ) return true;
		}

		return text.Contains( "#include \"common/", StringComparison.Ordinal );
	}
}
