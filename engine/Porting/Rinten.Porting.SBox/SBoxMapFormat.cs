using System.IO;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Rinten.Porting.SBox;

//One DMX element of a Hammer .vmap; getters accept both binary and keyvalues2 text encodings
internal sealed class SBoxMapElement
{
	private readonly List<KeyValuePair<string, object>> attributes = new();

	public SBoxMapElement( string typeName, string name = "" )
	{
		TypeName = typeName;
		Name = name;
	}

	/// <summary>What Hammer calls this kind of node - <c>CMapMesh</c>, <c>CMapEntity</c>.</summary>
	public string TypeName { get; }

	/// <summary>The element's own name, which is usually empty and never unique.</summary>
	public string Name { get; }

	/// <summary>The document this belongs to, for resolving the references a text file writes as a uuid rather than as the element itself.</summary>
	public SBoxMapDocument Document { get; internal set; }

	public void Add( string name, object value ) => attributes.Add( new( name, value ) );

	private object Raw( string name )
	{
		foreach ( var (key, value) in attributes )
		{
			if ( string.Equals( key, name, StringComparison.OrdinalIgnoreCase ) )
				return value;
		}

		return null;
	}

	public bool Has( string name ) => Raw( name ) is not null;

	//Every attribute this node carries, in the order it was written. For finding out what a node of theirs actually holds. Nothing in the port
	//reads by this - a porter that walked attributes it did not name would be guessing - but a format nobody documents is read by looking first.
	internal IEnumerable<string> AttributeNames()
	{
		foreach ( var (key, _) in attributes ) yield return key;
	}

	public string GetString( string name, string fallback = null )
		=> Raw( name ) switch
		{
			string text => text,
			null => fallback,
			var other => System.Convert.ToString( other, CultureInfo.InvariantCulture ),
		};

	//Text keeps everything as a string, so an int and a float arrive the same way and the caller cannot tell which it asked for. Both go through here.
	/// <summary>A number, whichever way it was written.</summary>
	public bool TryGetFloat( string name, out float value )
	{
		value = 0.0f;

		switch ( Raw( name ) )
		{
			case float single: value = single; return true;
			case int integer: value = integer; return true;
			case bool boolean: value = boolean ? 1.0f : 0.0f; return true;
			case string text: return float.TryParse( text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value );
			default: return false;
		}
	}

	public float GetFloat( string name, float fallback = 0.0f )
		=> TryGetFloat( name, out var value ) ? value : fallback;

	/// <summary>A flag. Hammer writes these as <c>0</c> and <c>1</c> in text and as a byte in binary, and neither is a word bool.TryParse knows.</summary>
	public bool TryGetBool( string name, out bool value )
	{
		value = false;

		switch ( Raw( name ) )
		{
			case bool boolean: value = boolean; return true;
			case int integer: value = integer != 0; return true;
			case float single: value = single != 0.0f; return true;

			case string text:
				var trimmed = text.Trim();
				if ( trimmed == "1" ) { value = true; return true; }
				if ( trimmed == "0" ) { value = false; return true; }
				return bool.TryParse( trimmed, out value );

			default: return false;
		}
	}

	public bool GetBool( string name, bool fallback = false )
		=> TryGetBool( name, out var value ) ? value : fallback;

	public Vector3 GetVector3( string name, Vector3 fallback = default )
		=> Raw( name ) switch
		{
			Vector3 vector => vector,
			string text => ParseVector3( text, fallback ),
			_ => fallback,
		};

	//Binary points by index and is wired up as it is read. Text points by uuid, so the reference is still a string here and the document is what turns it back into an element.
	/// <summary>One element this points at, or null.</summary>
	public SBoxMapElement GetElement( string name )
		=> Resolve( Raw( name ) );

	/// <summary>Every element under an attribute, with the ones that did not resolve dropped.</summary>
	public List<SBoxMapElement> GetElements( string name )
	{
		var result = new List<SBoxMapElement>();

		if ( Raw( name ) is not List<object> items ) return result;

		foreach ( var item in items )
		{
			if ( Resolve( item ) is { } element )
				result.Add( element );
		}

		return result;
	}

	private SBoxMapElement Resolve( object value )
		=> value switch
		{
			SBoxMapElement element => element,
			string uuid => Document?.Find( uuid ),
			_ => null,
		};

	public string[] GetStrings( string name )
	{
		if ( Raw( name ) is not List<object> items ) return Array.Empty<string>();

		var result = new string[items.Count];

		for ( int i = 0; i < items.Count; i++ )
			result[i] = items[i] as string ?? "";

		return result;
	}

	public int[] GetInts( string name )
	{
		if ( Raw( name ) is not List<object> items ) return Array.Empty<int>();

		var result = new int[items.Count];

		for ( int i = 0; i < items.Count; i++ )
		{
			result[i] = items[i] switch
			{
				int integer => integer,
				float single => (int)single,
				string text => int.TryParse( text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed ) ? parsed : 0,
				_ => 0,
			};
		}

		return result;
	}

	public Vector2[] GetVector2s( string name ) => GetArray( name, ParseVector2, value => value as Vector2? );
	public Vector3[] GetVector3s( string name ) => GetArray( name, text => ParseVector3( text ), value => value as Vector3? );
	public Vector4[] GetVector4s( string name ) => GetArray( name, ParseVector4, value => value as Vector4? );

	private T[] GetArray<T>( string name, Func<string, T> parse, Func<object, T?> cast ) where T : struct
	{
		if ( Raw( name ) is not List<object> items ) return Array.Empty<T>();

		var result = new T[items.Count];

		for ( int i = 0; i < items.Count; i++ )
		{
			result[i] = items[i] switch
			{
				string text => parse( text ),
				var other => cast( other ) ?? default,
			};
		}

		return result;
	}

	//-- the text spellings ---------------------------------------------------

	private static float[] Numbers( string text, int count )
	{
		var result = new float[count];

		if ( string.IsNullOrWhiteSpace( text ) ) return result;

		var parts = text.Trim().Split( ' ', StringSplitOptions.RemoveEmptyEntries );

		for ( int i = 0; i < count && i < parts.Length; i++ )
			float.TryParse( parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i] );

		return result;
	}

	private static Vector2 ParseVector2( string text )
	{
		var n = Numbers( text, 2 );
		return new Vector2( n[0], n[1] );
	}

	private static Vector3 ParseVector3( string text, Vector3 fallback = default )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return fallback;

		var n = Numbers( text, 3 );
		return new Vector3( n[0], n[1], n[2] );
	}

	private static Vector4 ParseVector4( string text )
	{
		var n = Numbers( text, 4 );
		return new Vector4( n[0], n[1], n[2], n[3] );
	}
}

//A whole Hammer map, read natively in both encodings so no dmxconvert binary is needed
internal sealed class SBoxMapDocument
{
	private readonly Dictionary<string, SBoxMapElement> byId = new( StringComparer.OrdinalIgnoreCase );

	public List<SBoxMapElement> Elements { get; } = new();

	/// <summary>The <c>CMapRootElement</c>, which is what everything hangs off.</summary>
	public SBoxMapElement Root
		=> Elements.FirstOrDefault( x => x.TypeName == "CMapRootElement" ) ?? Elements.FirstOrDefault();

	/// <summary>The element a text file names by uuid, or null.</summary>
	public SBoxMapElement Find( string id )
	{
		if ( string.IsNullOrWhiteSpace( id ) ) return null;

		byId.TryGetValue( id, out var element );
		return element;
	}

	private void Register( SBoxMapElement element )
	{
		element.Document = this;
		Elements.Add( element );

		if ( element.GetString( "id" ) is { Length: > 0 } id )
			byId[id] = element;
	}

	/// <summary>Read a map. Throws with what was wrong where, which is what the porter puts in its report.</summary>
	public static SBoxMapDocument Read( byte[] bytes )
	{
		if ( bytes is null || bytes.Length < 8 )
			throw new InvalidDataException( "the file is empty" );

		var (encoding, version, headerLength) = ReadHeader( bytes );

		return encoding switch
		{
			"binary" => ReadBinary( bytes, headerLength, version ),
			"keyvalues2" or "keyvalues2_flat" or "keyvalues2_noids"
				=> ReadText( Encoding.UTF8.GetString( bytes ) ),
			_ => throw new InvalidDataException( $"'{encoding}' is not an encoding this reads - Hammer writes binary or keyvalues2" ),
		};
	}

	//The one line every DMX file starts with: <!-- dmx encoding binary 9 format vmap 30 --> , and then a null.
	private static (string Encoding, int Version, int Length) ReadHeader( byte[] bytes )
	{
		var end = Array.IndexOf( bytes, (byte)0 );

		//A text file has no null in it at all, and its header ends at the newline.
		var searchLength = end >= 0 ? end : Math.Min( bytes.Length, 256 );
		var header = Encoding.ASCII.GetString( bytes, 0, searchLength );

		var open = header.IndexOf( "<!--", StringComparison.Ordinal );
		var close = header.IndexOf( "-->", StringComparison.Ordinal );

		if ( open < 0 || close < 0 )
			throw new InvalidDataException( "no dmx header - this is not a Hammer map" );

		var parts = header[( open + 4 )..close]
			.Split( ' ', StringSplitOptions.RemoveEmptyEntries );

		var encoding = "";
		var version = 0;

		for ( int i = 0; i < parts.Length - 1; i++ )
		{
			if ( parts[i] != "encoding" ) continue;

			encoding = parts[i + 1];

			if ( i + 2 < parts.Length )
				int.TryParse( parts[i + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out version );

			break;
		}

		if ( string.IsNullOrEmpty( encoding ) )
			throw new InvalidDataException( "the dmx header does not say what encoding it is" );

		//Binary starts straight after the null that terminates the header.
		return (encoding, version, end >= 0 ? end + 1 : close + 3);
	}

	//-- binary ---------------------------------------------------------------

	//What one attribute is. The numbers are theirs and are not going to change: a map saved by any Hammer since Source 2 shipped writes these.
	private enum AttributeType
	{
		Element = 1, Int = 2, Float = 3, Bool = 4, String = 5, Binary = 6,
		Time = 7, Color = 8, Vector2 = 9, Vector3 = 10, Vector4 = 11,
		QAngle = 12, Quaternion = 13, Matrix = 14, UInt64 = 15, Byte = 16,
	}

	//Added to a type id to make its array type; verified by reading a real map to the last byte
	private const int ArrayOffset = 32;

	private static SBoxMapDocument ReadBinary( byte[] bytes, int at, int version )
	{
		var document = new SBoxMapDocument();
		var reader = new BinaryCursor( bytes, at );

		//Version 9 puts a small document of its own in front of the real one - the thumbnail Hammer draws in its asset browser and the list of models
		//and materials the map names. Its strings are written out in full, because the table they would be indexed into has not been read yet.
		if ( version >= 9 )
			SkipPrefix( ref reader );

		var strings = ReadStringTable( ref reader, version );

		//Names and types first for every element, then every element's attributes. That is what
		//lets an attribute point at an element that has not been read yet, which most of them do.
		var count = reader.Int();

		if ( count < 0 || count > 4_000_000 )
			throw new InvalidDataException( $"{count} elements is not a number a map has" );

		var elements = new SBoxMapElement[count];

		for ( int i = 0; i < count; i++ )
		{
			var typeName = ReadString( ref reader, strings, version >= 2 );
			var name = ReadString( ref reader, strings, version >= 4 );

			reader.Skip( 16 ); //The element's guid, which nothing here points by.

			elements[i] = new SBoxMapElement( typeName, name );
		}

		foreach ( var element in elements )
		{
			var attributeCount = reader.Int();

			for ( int i = 0; i < attributeCount; i++ )
			{
				var name = ReadString( ref reader, strings, version >= 2 );
				var type = reader.Byte();

				element.Add( name, ReadValue( ref reader, type, strings, elements, version ) );
			}
		}

		foreach ( var element in elements )
			document.Register( element );

		return document;
	}

	private static void SkipPrefix( ref BinaryCursor reader )
	{
		var count = reader.Int();

		for ( int i = 0; i < count; i++ )
		{
			var attributes = reader.Int();

			for ( int a = 0; a < attributes; a++ )
			{
				reader.String();
				var type = reader.Byte();

				//Read rather than skipped by a table of sizes: the value has to be walked anyway
				//to know where the next one starts, and there is nothing in here the port wants.
				ReadValue( ref reader, type, null, null, 1 );
			}
		}
	}

	private static string[] ReadStringTable( ref BinaryCursor reader, int version )
	{
		if ( version < 2 ) return Array.Empty<string>();

		var count = version >= 4 ? reader.Int() : reader.Short();

		if ( count < 0 || count > 4_000_000 )
			throw new InvalidDataException( $"{count} strings is not a number a map has" );

		var strings = new string[count];

		for ( int i = 0; i < count; i++ )
			strings[i] = reader.String();

		return strings;
	}

	//A string that is either written where it is used or is an index into the table at the front.
	private static string ReadString( ref BinaryCursor reader, string[] strings, bool indexed )
	{
		if ( !indexed || strings is null || strings.Length == 0 )
			return reader.String();

		var index = reader.Int();

		return index >= 0 && index < strings.Length ? strings[index] : "";
	}

	private static object ReadValue( ref BinaryCursor reader, int type, string[] strings, SBoxMapElement[] elements, int version )
	{
		if ( type > ArrayOffset )
		{
			var itemType = type - ArrayOffset;
			var count = reader.Int();

			if ( count < 0 )
				throw new InvalidDataException( $"an array of {count} is not one" );

			var items = new List<object>( Math.Min( count, 4096 ) );

			for ( int i = 0; i < count; i++ )
			{
				//Strings inside an array are written out in full even where a lone string of the same type would have been an index.
				//Reading one as an index is four bytes taken out of the middle of the next value, and everything after it is nonsense.
				items.Add( itemType == (int)AttributeType.String
					? reader.String()
					: ReadScalar( ref reader, itemType, strings, elements, version ) );
			}

			return items;
		}

		return ReadScalar( ref reader, type, strings, elements, version );
	}

	private static object ReadScalar( ref BinaryCursor reader, int type, string[] strings, SBoxMapElement[] elements, int version )
	{
		switch ( (AttributeType)type )
		{
			case AttributeType.Element:
				var index = reader.Int();

				//Minus two is one of theirs from another file, named by the string after it. Nothing in a
				//map the porter reads points out of the file, but the bytes still have to be walked past.
				if ( index == -2 )
				{
					reader.String();
					return null;
				}

				return elements is not null && index >= 0 && index < elements.Length ? elements[index] : null;

			case AttributeType.Int: return reader.Int();
			case AttributeType.Float: return reader.Float();
			case AttributeType.Bool: return reader.Byte() != 0;
			case AttributeType.String: return ReadString( ref reader, strings, version >= 5 );

			case AttributeType.Binary:
				reader.Skip( reader.Int() );
				return null;

			case AttributeType.Time: return reader.Int() / 10000.0f;

			case AttributeType.Color:
				var r = reader.Byte();
				var g = reader.Byte();
				var b = reader.Byte();
				var a = reader.Byte();
				return new Color( r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f );

			case AttributeType.Vector2:
				return new Vector2( reader.Float(), reader.Float() );

			case AttributeType.Vector3:
			case AttributeType.QAngle:
				return new Vector3( reader.Float(), reader.Float(), reader.Float() );

			case AttributeType.Vector4:
			case AttributeType.Quaternion:
				return new Vector4( reader.Float(), reader.Float(), reader.Float(), reader.Float() );

			case AttributeType.Matrix:
				reader.Skip( 64 );
				return null;

			case AttributeType.UInt64:
				reader.Skip( 8 );
				return null;

			case AttributeType.Byte:
				return (int)reader.Byte();

			default:
				throw new InvalidDataException( $"attribute type {type} is not one this reads" );
		}
	}

	//A position in the file, and the little-endian reads it does.
	private ref struct BinaryCursor
	{
		private readonly byte[] data;
		private int at;

		public BinaryCursor( byte[] data, int at )
		{
			this.data = data;
			this.at = at;
		}

		private void Need( int count )
		{
			if ( at + count > data.Length )
				throw new InvalidDataException( $"the file ends {at + count - data.Length} byte(s) into a value" );
		}

		public void Skip( int count )
		{
			Need( count );
			at += count;
		}

		public byte Byte()
		{
			Need( 1 );
			return data[at++];
		}

		public short Short()
		{
			Need( 2 );
			var value = BinaryPrimitives.ReadInt16LittleEndian( data.AsSpan( at ) );
			at += 2;
			return value;
		}

		public int Int()
		{
			Need( 4 );
			var value = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( at ) );
			at += 4;
			return value;
		}

		public float Float()
		{
			Need( 4 );
			var value = BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( at ) );
			at += 4;
			return value;
		}

		public string String()
		{
			var end = Array.IndexOf( data, (byte)0, at );

			if ( end < 0 )
				throw new InvalidDataException( "a string runs off the end of the file" );

			var value = Encoding.UTF8.GetString( data, at, end - at );
			at = end + 1;
			return value;
		}
	}

	//-- keyvalues2 -----------------------------------------------------------

	//The same tree written as text, which is what Hammer saves when it is told to and what a map that has been through their dmxconvert looks like.
	private static SBoxMapDocument ReadText( string text )
	{
		var document = new SBoxMapDocument();
		var reader = new TextCursor( text, document );

		reader.SkipHeader();

		while ( reader.More )
			reader.ReadElement();

		return document;
	}

	private sealed class TextCursor
	{
		private readonly string text;
		private readonly SBoxMapDocument document;
		private int at;

		public TextCursor( string text, SBoxMapDocument document )
		{
			this.text = text;
			this.document = document;
		}

		public bool More
		{
			get
			{
				SkipWhitespace();
				return at < text.Length;
			}
		}

		public void SkipHeader()
		{
			SkipWhitespace();

			if ( at >= text.Length || text[at] != '<' ) return;

			while ( at < text.Length && text[at] != '>' ) at++;
			if ( at < text.Length ) at++;
		}

		private void SkipWhitespace()
		{
			while ( at < text.Length && char.IsWhiteSpace( text[at] ) ) at++;
		}

		private string Quoted()
		{
			SkipWhitespace();

			if ( at >= text.Length || text[at] != '"' )
				throw new InvalidDataException( $"expected a quoted string at {at}: '{Around()}'" );

			at++;

			var value = new StringBuilder();

			while ( at < text.Length )
			{
				var c = text[at++];

				if ( c == '"' ) break;

				if ( c == '\\' && at < text.Length )
				{
					var escaped = text[at++];
					value.Append( escaped == 'n' ? '\n' : escaped == 't' ? '\t' : escaped );
					continue;
				}

				value.Append( c );
			}

			return value.ToString();
		}

		private string Around()
			=> text.Substring( at, Math.Min( 40, text.Length - at ) );

		private void Expect( char c )
		{
			SkipWhitespace();

			if ( at < text.Length && text[at] == c )
			{
				at++;
				return;
			}

			throw new InvalidDataException( $"expected '{c}' at {at}: '{Around()}'" );
		}

		public SBoxMapElement ReadElement()
		{
			var typeName = Quoted();
			Expect( '{' );

			var element = new SBoxMapElement( typeName );
			ReadAttributes( element );
			document.Register( element );

			return element;
		}

		private void ReadAttributes( SBoxMapElement element )
		{
			while ( true )
			{
				SkipWhitespace();

				if ( at >= text.Length || text[at] == '}' ) break;

				var name = Quoted();
				var type = Quoted();

				SkipWhitespace();

				object value;

				if ( at < text.Length && text[at] == '{' )
				{
					at++;

					var nested = new SBoxMapElement( type );
					ReadAttributes( nested );
					document.Register( nested );
					value = nested;
				}
				else if ( at < text.Length && text[at] == '[' )
				{
					at++;
					value = ReadArray( type );
				}
				else
				{
					value = Quoted();
				}

				element.Add( name, value );
			}

			Expect( '}' );
		}

		private List<object> ReadArray( string type )
		{
			var items = new List<object>();
			var isElements = type == "element_array";

			while ( true )
			{
				SkipWhitespace();

				if ( at >= text.Length || text[at] == ']' ) break;

				if ( isElements )
				{
					//Either a whole element written here, or a uuid naming one written somewhere else in the file.
					var first = Quoted();

					SkipWhitespace();

					if ( at < text.Length && text[at] == '{' )
					{
						at++;

						var nested = new SBoxMapElement( first );
						ReadAttributes( nested );
						document.Register( nested );
						items.Add( nested );
					}
					else
					{
						items.Add( first );
					}
				}
				else
				{
					items.Add( Quoted() );
				}

				SkipWhitespace();

				if ( at < text.Length && text[at] == ',' ) at++;
			}

			Expect( ']' );

			return items;
		}
	}
}
