using System.Collections.Generic;
using System.Globalization;

namespace Rinten;

//Reader for text KeyValues3, used by model metadata blocks and graphs; rejects what it does not know
internal sealed partial class CoreImportedKeyValues3
{
	/// <summary>Read a text document, or null where it is not one.</summary>
	public static CoreImportedKeyValues3 ReadText( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return null;

		try
		{
			var at = 0;

			Skip( text, ref at );

			var value = ParseValue( text, ref at );

			return value;
		}
		catch ( System.Exception e ) when ( e is System.FormatException or System.IndexOutOfRangeException or System.ArgumentOutOfRangeException )
		{
			log.Warning( $"key values written as text would not read ({e.Message})" );
			return null;
		}
	}

	//Whitespace, the header they put at the top, and comments - none of which is a value.
	private static void Skip( string text, ref int at )
	{
		while ( at < text.Length )
		{
			var c = text[at];

			if ( char.IsWhiteSpace( c ) || c == ',' ) { at++; continue; }

			if ( c == '<' && text.AsSpan( at ).StartsWith( "<!--" ) )
			{
				var end = text.IndexOf( "-->", at, System.StringComparison.Ordinal );
				at = end < 0 ? text.Length : end + 3;
				continue;
			}

			if ( c == '/' && at + 1 < text.Length )
			{
				if ( text[at + 1] == '/' )
				{
					var end = text.IndexOf( '\n', at );
					at = end < 0 ? text.Length : end + 1;
					continue;
				}

				if ( text[at + 1] == '*' )
				{
					var end = text.IndexOf( "*/", at, System.StringComparison.Ordinal );
					at = end < 0 ? text.Length : end + 2;
					continue;
				}
			}

			return;
		}
	}

	private static CoreImportedKeyValues3 ParseValue( string text, ref int at )
	{
		Skip( text, ref at );

		if ( at >= text.Length ) throw new System.FormatException( "there is no value here" );

		var c = text[at];

		if ( c == '{' ) return ParseObject( text, ref at );
		if ( c == '[' ) return ParseArray( text, ref at );
		if ( c == '"' ) return new CoreImportedKeyValues3( ParseString( text, ref at ) );

		//A value that says what kind of thing it names - a resource, a subclass - and then the value itself.
		if ( char.IsLetter( c ) || c == '_' )
		{
			var word = ParseWord( text, ref at );

			switch ( word )
			{
				case "true": return new CoreImportedKeyValues3( true );
				case "false": return new CoreImportedKeyValues3( false );
				case "null": return new CoreImportedKeyValues3( null );
			}

			Skip( text, ref at );

			//resource:"models/thing.vmdl" and its like: the marker is not the value, and what follows is.
			if ( at < text.Length && text[at] == ':' )
			{
				at++;
				return ParseValue( text, ref at );
			}

			return new CoreImportedKeyValues3( word );
		}

		return ParseNumber( text, ref at );
	}

	private static CoreImportedKeyValues3 ParseObject( string text, ref int at )
	{
		var fields = new Dictionary<string, CoreImportedKeyValues3>();

		at++; //{

		while ( true )
		{
			Skip( text, ref at );

			if ( at >= text.Length ) throw new System.FormatException( "an object does not end" );

			if ( text[at] == '}' ) { at++; break; }

			var name = text[at] == '"' ? ParseString( text, ref at ) : ParseWord( text, ref at );

			Skip( text, ref at );

			if ( at >= text.Length || text[at] != '=' ) throw new System.FormatException( $"'{name}' is a name with no value" );

			at++;

			fields[name] = ParseValue( text, ref at );
		}

		return new CoreImportedKeyValues3( fields );
	}

	private static CoreImportedKeyValues3 ParseArray( string text, ref int at )
	{
		var items = new List<CoreImportedKeyValues3>();

		at++; //[

		while ( true )
		{
			Skip( text, ref at );

			if ( at >= text.Length ) throw new System.FormatException( "an array does not end" );

			if ( text[at] == ']' ) { at++; break; }

			items.Add( ParseValue( text, ref at ) );
		}

		return new CoreImportedKeyValues3( items );
	}

	private static string ParseString( string text, ref int at )
	{
		//Three quotes means the string runs over lines until three more.
		if ( text.AsSpan( at ).StartsWith( "\"\"\"" ) )
		{
			var from = at + 3;
			var end = text.IndexOf( "\"\"\"", from, System.StringComparison.Ordinal );

			if ( end < 0 ) throw new System.FormatException( "a string over lines does not end" );

			at = end + 3;

			//The first line break after the opening is part of the syntax rather than of the string.
			var value = text[from..end];

			return value.StartsWith( "\r\n" ) ? value[2..] : value.StartsWith( "\n" ) ? value[1..] : value;
		}

		var built = new System.Text.StringBuilder();

		at++; //"

		while ( at < text.Length && text[at] != '"' )
		{
			if ( text[at] == '\\' && at + 1 < text.Length )
			{
				at++;

				built.Append( text[at] switch
				{
					'n' => '\n',
					'r' => '\r',
					't' => '\t',
					_ => text[at],
				} );

				at++;
				continue;
			}

			built.Append( text[at++] );
		}

		if ( at >= text.Length ) throw new System.FormatException( "a string does not end" );

		at++; //"

		return built.ToString();
	}

	//A bare name, which is what their keys are written as.
	private static string ParseWord( string text, ref int at )
	{
		var from = at;

		while ( at < text.Length && ( char.IsLetterOrDigit( text[at] ) || text[at] is '_' or '.' or '-' ) ) at++;

		if ( at == from ) throw new System.FormatException( $"'{text[at]}' is not the start of a name" );

		return text[from..at];
	}

	private static CoreImportedKeyValues3 ParseNumber( string text, ref int at )
	{
		var from = at;

		if ( at < text.Length && ( text[at] == '-' || text[at] == '+' ) ) at++;

		while ( at < text.Length && ( char.IsDigit( text[at] ) || text[at] is '.' or 'e' or 'E' or '+' or '-' ) ) at++;

		var word = text[from..at];

		if ( word.Length == 0 ) throw new System.FormatException( $"'{text[from]}' is not a value" );

		//Whole where it is written whole, so a count reads as a count.
		if ( !word.Contains( '.' ) && !word.Contains( 'e' ) && !word.Contains( 'E' )
			&& long.TryParse( word, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole ) )
		{
			return new CoreImportedKeyValues3( whole );
		}

		if ( double.TryParse( word, NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) )
			return new CoreImportedKeyValues3( number );

		throw new System.FormatException( $"'{word}' is not a number" );
	}
}
