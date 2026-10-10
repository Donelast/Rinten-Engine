using Rinten.UI;
using System;
using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor.MdlEditor;

//Copies model nodes via the system clipboard as JSON tagged with their kind
static class ModelDocClipboard
{
	//Marks the clipboard as ours, and says what is on it.
	const string KindKey = "__mdlnode";
	const string BodyKey = "__value";

	static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = true,
		IncludeFields = true,
		WriteIndented = false,
		Converters = { new JsonStringEnumConverter() },
	};

	//The list's element type, so polymorphic items like hulls keep their type discriminator
	static Type ElementType( IList list )
	{
		foreach ( var contract in list.GetType().GetInterfaces() )
		{
			if ( contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof( System.Collections.Generic.IList<> ) )
				return contract.GetGenericArguments()[0];
		}

		return null;
	}

	/// <summary>A node copied from a list, as json, or null if it cannot be.</summary>
	public static string Write( IList list, int index, ModelDocKind kind )
	{
		if ( list is null || index < 0 || index >= list.Count ) return null;
		if ( ElementType( list ) is not { } type ) return null;

		var value = list[index];
		if ( value is null ) return null;

		try
		{
			var payload = new JsonObject
			{
				[KindKey] = kind.ToString(),
				[BodyKey] = JsonNode.Parse( JsonSerializer.Serialize( value, type, Options ) ),
			};

			return payload.ToJsonString();
		}
		catch ( JsonException )
		{
			return null;
		}
	}

	/// <summary>Which kind of node is on the clipboard, if one of ours is.</summary>
	public static ModelDocKind? KindOn( string clipboard )
	{
		if ( string.IsNullOrWhiteSpace( clipboard ) ) return null;
		if ( !clipboard.TrimStart().StartsWith( '{' ) ) return null;

		try
		{
			if ( JsonNode.Parse( clipboard ) is not JsonObject payload ) return null;
			if ( payload[KindKey]?.GetValue<string>() is not { } name ) return null;

			return Enum.TryParse<ModelDocKind>( name, out var kind ) ? kind : null;
		}
		catch ( Exception e ) when ( e is JsonException or FormatException or InvalidOperationException )
		{
			return null;
		}
	}

	/// <summary>The node on the clipboard, built as the list wants it.</summary>
	public static object Read( string clipboard, IList list )
	{
		if ( list is null ) return null;
		if ( ElementType( list ) is not { } type ) return null;

		try
		{
			if ( JsonNode.Parse( clipboard ) is not JsonObject payload ) return null;
			if ( payload[BodyKey] is not { } body ) return null;

			return JsonSerializer.Deserialize( body.ToJsonString(), type, Options );
		}
		catch ( Exception e ) when ( e is JsonException or FormatException or InvalidOperationException )
		{
			return null;
		}
	}

	//Through json rather than by hand: these are plain data objects with lists inside them, and a duplicate that shared a list with its original would be two nodes that quietly edit each other.
	/// <summary>A copy of what is at <paramref name="index"/>, deep enough that editing one does not edit the other.</summary>
	public static object Clone( IList list, int index )
	{
		if ( list is null || index < 0 || index >= list.Count ) return null;
		if ( ElementType( list ) is not { } type ) return null;

		var value = list[index];
		if ( value is null ) return null;

		try
		{
			return JsonSerializer.Deserialize( JsonSerializer.Serialize( value, type, Options ), type, Options );
		}
		catch ( JsonException )
		{
			return null;
		}
	}
}
