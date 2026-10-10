using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rinten;

//JSON converter that handles BinaryBlob serialization
internal class BinaryConvert : JsonConverterFactory
{
	public override bool CanConvert( Type typeToConvert )
	{
		return typeof( BlobData ).IsAssignableFrom( typeToConvert );
	}

	public override JsonConverter CreateConverter( Type typeToConvert, JsonSerializerOptions options )
	{
		return (JsonConverter)Activator.CreateInstance(
			typeof( BinaryBlobConverter<> ).MakeGenericType( typeToConvert ) );
	}

	private class BinaryBlobConverter<T> : JsonConverter<T> where T : BlobData
	{
		public override T Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			//Read the $blob reference
			if ( reader.TokenType != JsonTokenType.StartObject )
				return null;

			Guid? blobGuid = null;
			byte[] inline = null;

			while ( reader.Read() )
			{
				if ( reader.TokenType == JsonTokenType.EndObject )
					break;

				if ( reader.TokenType == JsonTokenType.PropertyName )
				{
					var propertyName = reader.GetString();
					reader.Read();

					if ( propertyName == "$blob" && reader.TokenType == JsonTokenType.String )
					{
						if ( Guid.TryParse( reader.GetString(), out var guid ) )
						{
							blobGuid = guid;
						}
					}
					else if ( propertyName == "$data" && reader.TokenType == JsonTokenType.String )
					{
						//Written where no blob context was open - see Write.
						try { inline = reader.GetBytesFromBase64(); }
						catch ( FormatException ) { inline = null; }
					}
					else
					{
						//Skip any other properties (e.g. the "$refs" array used by the compiler).
						reader.Skip();
					}
				}
			}

			if ( blobGuid.HasValue && blobGuid.Value != Guid.Empty )
			{
				if ( BlobDataSerializer.ReadBlob( blobGuid.Value, typeToConvert ) is T fromContext )
					return fromContext;
			}

			if ( inline is not null )
			{
				return BlobDataSerializer.FromBytes( inline, typeToConvert ) as T;
			}

			return null;
		}

		public override void Write( Utf8JsonWriter writer, T value, JsonSerializerOptions options )
		{
			writer.WriteStartObject();

			//Into the blob context where one is open (a resource's save), and referenced by id. With none open - a prefab's cached instance json, a diff
			//patch, a copy - there is nowhere to put it, and a bare empty id lost the data: the blob goes into the json itself, as PolygonMesh does.
			if ( BlobDataSerializer.IsActive )
			{
				var guid = BlobDataSerializer.RegisterBlob( value );
				writer.WriteString( "$blob", guid.ToString() );
			}
			else
			{
				writer.WriteBase64String( "$data", BlobDataSerializer.ToBytes( value ) );
			}

			//Emit asset references for resource compiler detection.
			if ( value is IBlobReferences references )
			{
				writer.WriteStartArray( "$refs" );
				foreach ( var path in references.GetReferencedResources() )
				{
					if ( !string.IsNullOrWhiteSpace( path ) )
						writer.WriteStringValue( path );
				}
				writer.WriteEndArray();
			}

			writer.WriteEndObject();
		}
	}
}
