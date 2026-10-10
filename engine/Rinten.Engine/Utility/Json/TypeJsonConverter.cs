using Facepunch.ActionGraphs;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rinten;

//Writes a Type by its type library name, generic args in angle brackets; empty slots are kept
internal sealed class TypeJsonConverter : JsonConverter<Type>
{
	private readonly ITypeLoader _typeLoader;

	public TypeJsonConverter( ITypeLoader typeLoader )
	{
		_typeLoader = typeLoader;
	}

	//Types we can name without asking the type library, which doesn't list all of them.
	private static Dictionary<string, Type> WellKnown { get; } = new[]
	{
		typeof(bool), typeof(byte), typeof(sbyte), typeof(char), typeof(short), typeof(ushort),
		typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double),
		typeof(decimal), typeof(string), typeof(object), typeof(void), typeof(nint), typeof(nuint),
		typeof(Guid), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan)
	}.ToDictionary( x => x.FullName!, x => x );

	//A type's own type is RuntimeType , not Type, so it reaches us as that whenever it was passed along as an object.
	public override bool CanConvert( Type typeToConvert )
	{
		return typeof( Type ).IsAssignableFrom( typeToConvert );
	}

	public override Type Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		switch ( reader.TokenType )
		{
			case JsonTokenType.Null:
				return null;

			case JsonTokenType.String:
				return Resolve( reader.GetString() );

			default:
				//Not something we ever wrote. Skipping it loses one value rather than the file.
				Log.Warning( $"Expected a type name, got {reader.TokenType}." );
				reader.Skip();
				return null;
		}
	}

	public override void Write( Utf8JsonWriter writer, Type value, JsonSerializerOptions options )
	{
		if ( value is null )
		{
			writer.WriteNullValue();
			return;
		}

		writer.WriteStringValue( Identify( value ) );
	}

	//The string we store for a type. An unbound generic argument is an empty slot.
	internal string Identify( Type type )
	{
		if ( type is null || type.IsGenericParameter )
		{
			return "";
		}

		if ( type.IsArray )
		{
			var rank = type.GetArrayRank();
			return $"{Identify( type.GetElementType() )}[{new string( ',', rank - 1 )}]";
		}

		if ( !type.IsGenericType )
		{
			return _typeLoader.TypeToIdentifier( type );
		}

		var builder = new StringBuilder();

		builder.Append( _typeLoader.TypeToIdentifier( type.GetGenericTypeDefinition() ) );
		builder.Append( '<' );

		var arguments = type.GetGenericArguments();

		for ( var i = 0; i < arguments.Length; i++ )
		{
			if ( i > 0 ) builder.Append( ',' );
			builder.Append( Identify( arguments[i] ) );
		}

		builder.Append( '>' );

		return builder.ToString();
	}

	//The type a stored string names, or null when nothing answers to it.
	internal Type Resolve( string identifier )
	{
		if ( string.IsNullOrWhiteSpace( identifier ) )
		{
			return null;
		}

		identifier = identifier.Trim();

		if ( identifier.EndsWith( ']' ) )
		{
			var opener = OpenerOf( identifier, '[', ']' );
			var rank = opener > 0 ? identifier[(opener + 1)..^1] : null;

			//Only commas in there is an array. Anything else is a legacy assembly qualified name, which the type library reads for us.
			if ( rank is not null && rank.All( x => x == ',' ) )
			{
				var elementType = Resolve( identifier[..opener] );

				if ( elementType is null ) return null;

				return rank.Length == 0
					? elementType.MakeArrayType()
					: elementType.MakeArrayType( rank.Length + 1 );
			}
		}

		if ( identifier.EndsWith( '>' ) )
		{
			var opener = OpenerOf( identifier, '<', '>' );

			if ( opener > 0 )
			{
				return ResolveGeneric( identifier[..opener], SplitArguments( identifier[(opener + 1)..^1] ) );
			}
		}

		return ResolveName( identifier );
	}

	//Builds the generic type, leaving the definition's own parameter in any slot that was stored empty or that no longer names a type.
	private Type ResolveGeneric( string name, List<string> arguments )
	{
		if ( ResolveName( name ) is not { } definition )
		{
			return null;
		}

		if ( !definition.IsGenericType )
		{
			Log.Warning( $"'{name}' isn't generic, but was stored with {arguments.Count} generic argument(s)." );
			return definition;
		}

		definition = definition.GetGenericTypeDefinition();

		var parameters = definition.GetGenericArguments();

		if ( parameters.Length != arguments.Count )
		{
			Log.Warning( $"'{name}' takes {parameters.Length} generic argument(s), but {arguments.Count} were stored." );
			return definition;
		}

		var resolved = new Type[parameters.Length];
		var anyBound = false;

		for ( var i = 0; i < parameters.Length; i++ )
		{
			var argument = Resolve( arguments[i] );

			resolved[i] = argument ?? parameters[i];
			anyBound |= argument is not null;
		}

		if ( !anyBound )
		{
			return definition;
		}

		try
		{
			return definition.MakeGenericType( resolved );
		}
		catch ( Exception )
		{
			//Constraints the arguments no longer satisfy. The definition is still the type somebody picked, so keep that rather than nothing.
			return definition;
		}
	}

	private Type ResolveName( string name )
	{
		if ( WellKnown.TryGetValue( name, out var wellKnown ) )
		{
			return wellKnown;
		}

		try
		{
			if ( _typeLoader.TypeFromIdentifier( name ) is { } fromLibrary )
			{
				return fromLibrary;
			}
		}
		catch ( Exception )
		{
			//Not one the type library lists - the core library might still know it.
		}

		//Generic definitions like System.Collections.Generic.List`1 aren't in the type library, and are safe to name: they are already reachable from anywhere.
		if ( Type.GetType( name, throwOnError: false ) is { } fromCoreLib
			&& fromCoreLib.Assembly == typeof( object ).Assembly )
		{
			return fromCoreLib;
		}

		Log.Warning( $"Unable to find type '{name}'." );

		return null;
	}

	//Where the bracket that the string's last character closes was opened, or -1 when the string doesn't end in a matched pair.
	private static int OpenerOf( string identifier, char open, char close )
	{
		var depth = 0;

		for ( var i = identifier.Length - 1; i >= 0; i-- )
		{
			var c = identifier[i];

			if ( c == close ) depth++;
			else if ( c == open && --depth == 0 ) return i;
		}

		return -1;
	}

	//The comma separated arguments of one generic, ignoring commas inside nested ones.
	private static List<string> SplitArguments( string arguments )
	{
		var result = new List<string>();
		var depth = 0;
		var start = 0;

		for ( var i = 0; i < arguments.Length; i++ )
		{
			switch ( arguments[i] )
			{
				case '<':
				case '[':
					depth++;
					break;

				case '>':
				case ']':
					depth--;
					break;

				case ',' when depth == 0:
					result.Add( arguments[start..i] );
					start = i + 1;
					break;
			}
		}

		result.Add( arguments[start..] );

		return result;
	}
}
