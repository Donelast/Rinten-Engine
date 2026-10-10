using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rinten;

public sealed partial class MaterialDefinition
{
	//Version 1: how a surface is drawn is its shader's features, not three fields of the material's own. Version 2: no
	//packed rough/metal/occlusion map - roughness, metalness and occlusion are a picture each, asked for by the shader.
	[Hide, JsonIgnore] public override int ResourceVersion => 2;

	//Pre-v1 Blend/TwoSided/Filtering become features (F_ALPHA, F_TRANSLUCENT, F_BACKFACES); hand-set wins
	[Expose, JsonUpgrader( typeof( MaterialDefinition ), 1 )]
	static void Upgrader_v1( JsonObject json )
	{
		var existing = json["Features"] as JsonObject;
		var features = existing;

		void Add( string name )
		{
			features ??= new JsonObject();
			if ( !features.ContainsKey( name ) ) features[name] = 1;
		}

		if ( json.TryGetPropertyValue( "Blend", out var blend ) )
		{
			switch ( LegacyBlend( blend?.ToJsonString().Trim( '"' ) ) )
			{
				case 1: Add( "F_ALPHA" ); break;
				case 2: Add( "F_TRANSLUCENT" ); break;
			}

			json.Remove( "Blend" );
		}

		if ( json.TryGetPropertyValue( "TwoSided", out var sided ) )
		{
			if ( sided is JsonValue value && value.TryGetValue<bool>( out var on ) && on ) Add( "F_BACKFACES" );

			json.Remove( "TwoSided" );
		}

		json.Remove( "Filtering" );

		if ( features is not null && existing is null ) json["Features"] = features;
	}

	//Pre-v2 packed rough/metal/AO map is dropped; nothing sampled it (fill_material_maps restores maps)
	[Expose, JsonUpgrader( typeof( MaterialDefinition ), 2 )]
	static void Upgrader_v2( JsonObject json ) => json.Remove( "RoughMetalAmbient" );

	//What an old Blend said: nothing, a cutout, or see-through.
	static int LegacyBlend( string text ) => text?.Trim().ToLowerInvariant() switch
	{
		"1" or "masked" => 1,
		"2" or "3" or "translucent" or "additive" => 2,
		_ => 0,
	};

	//The same, for a reader holding the file's JSON rather than the resource - the renderer and Material.Load
	//both read a .mat themselves, and a file nobody has saved since keeps its old keys until somebody does.
	internal static void LegacyFeatures( JsonElement root, System.Collections.Generic.IDictionary<string, int> into )
	{
		void Add( string name )
		{
			if ( !into.ContainsKey( name ) ) into[name] = 1;
		}

		if ( root.TryGetProperty( "Blend", out var blend ) )
		{
			var text = blend.ValueKind switch
			{
				JsonValueKind.String => blend.GetString(),
				JsonValueKind.Number => blend.GetRawText(),
				_ => null,
			};

			switch ( LegacyBlend( text ) )
			{
				case 1: Add( "F_ALPHA" ); break;
				case 2: Add( "F_TRANSLUCENT" ); break;
			}
		}

		if ( root.TryGetProperty( "TwoSided", out var sided ) && sided.ValueKind == JsonValueKind.True )
			Add( "F_BACKFACES" );
	}
}
