using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Rinten;

public sealed partial class ModelDefinition
{
	//Graphs live only in models; compiled beside the model as ModelGraphFile, which is what runs
	/// <summary>The graph a character built on this model is animated by - the graph editor's document, kept in the model.</summary>
	[Hide]
	public ModelGraph Graph { get; set; }

	//Leaves a graph the model does not carry out of the file, rather than writing it down as nothing. Nearly every
	//model has none, and a "Graph": null in each of them is a line added to every model the next time anything saves it.
	protected override void OnJsonSerialize( JsonObject node )
	{
		base.OnJsonSerialize( node );

		if ( Graph is null )
			node.Remove( nameof( Graph ) );
	}

	/// <summary>A graph as text: the document the graph editor wrote, which nothing here reads.</summary>
	[JsonConverter( typeof( Converter ) )]
	public sealed class ModelGraph : Json.IAlreadyJson
	{
		/// <summary>The graph editor's document, or null where it was only skimmed.</summary>
		public string Document { get; }

		public ModelGraph( string document )
		{
			Document = document;
		}

		//What a reader that stepped over the document puts in its place.
		internal static readonly ModelGraph Skimmed = new( null );

		//Options need a resolver to be frozen; SkipHotload since the hotload upgrader nulls JsonTypeInfo
		[SkipHotload]
		private static readonly JsonTypeInfo<ModelGraph> Info
			= JsonMetadataServices.CreateValueInfo<ModelGraph>( new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }, new Converter() );

		//A node that writes the document as it is, rather than a tree of nodes parsed out of it.
		JsonNode Json.IAlreadyJson.ToJsonNode() => JsonValue.Create( this, Info );

		//The document as it is written in the file, both ways.
		internal sealed class Converter : JsonConverter<ModelGraph>
		{
			public override ModelGraph Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
			{
				using var document = JsonDocument.ParseValue( ref reader );

				return new ModelGraph( document.RootElement.GetRawText() );
			}

			public override void Write( Utf8JsonWriter writer, ModelGraph value, JsonSerializerOptions options )
			{
				if ( value?.Document is not { } document )
					throw new InvalidOperationException( "a graph that was only skimmed has nothing to write" );

				//Ours, and written by a serialiser - there is nothing to check.
				writer.WriteRawValue( document, skipInputValidation: true );
			}
		}

		//Skips over the graph document without parsing it, for readers that don't need the graph
		internal sealed class Skim : JsonConverter<ModelGraph>
		{
			public override ModelGraph Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
			{
				reader.Skip();

				return Skimmed;
			}

			public override void Write( Utf8JsonWriter writer, ModelGraph value, JsonSerializerOptions options )
				=> throw new InvalidOperationException( "a definition read by skimming is not one to write back" );
		}
	}
}
