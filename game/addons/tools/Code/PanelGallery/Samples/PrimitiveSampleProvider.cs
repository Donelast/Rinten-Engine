using System.Collections;

namespace Editor.Gallery;

//A number is never zero and text is never empty, so a control showing one is visibly showing something. Containers ask the root chain for their elements, so a list of materials gets the table's material and not this provider's null.
/// <summary>Numbers, text, enums, and the containers made of them.</summary>
public sealed class PrimitiveSampleProvider : ISampleValueProvider
{
	public bool TryCreate( Type type, ISampleValueProvider root, out object value )
	{
		value = Scalar( type );
		if ( value is not null ) return true;

		if ( Nullable.GetUnderlyingType( type ) is { } inner )
			return root.TryCreate( inner, root, out value );

		if ( type.IsEnum )
		{
			var values = Enum.GetValues( type );
			value = values.Length > 1 ? values.GetValue( 1 ) : values.Length > 0 ? values.GetValue( 0 ) : null;
			return value is not null;
		}

		if ( type.IsArray && type.GetArrayRank() == 1 )
		{
			var element = type.GetElementType();
			var array = Array.CreateInstance( element, 2 );

			for ( var i = 0; i < 2; i++ )
			{
				if ( root.TryCreate( element, root, out var item ) ) array.SetValue( item, i );
			}

			value = array;
			return true;
		}

		if ( !type.IsGenericType ) return false;

		var definition = type.GetGenericTypeDefinition();
		var arguments = type.GetGenericArguments();

		if ( definition == typeof( List<> ) || definition == typeof( IList<> ) || definition == typeof( IReadOnlyList<> ) || definition == typeof( IEnumerable<> ) )
		{
			var list = (IList)Activator.CreateInstance( typeof( List<> ).MakeGenericType( arguments[0] ) );

			for ( var i = 0; i < 2; i++ )
			{
				if ( root.TryCreate( arguments[0], root, out var item ) ) list.Add( item );
			}

			value = list;
			return true;
		}

		if ( definition == typeof( HashSet<> ) || definition == typeof( ISet<> ) )
		{
			var set = Activator.CreateInstance( typeof( HashSet<> ).MakeGenericType( arguments[0] ) );
			var add = set.GetType().GetMethod( "Add" );

			if ( root.TryCreate( arguments[0], root, out var item ) ) add?.Invoke( set, [item] );

			value = set;
			return true;
		}

		if ( definition == typeof( Dictionary<,> ) || definition == typeof( IDictionary<,> ) || definition == typeof( IReadOnlyDictionary<,> ) )
		{
			var dictionary = (IDictionary)Activator.CreateInstance( typeof( Dictionary<,> ).MakeGenericType( arguments ) );

			foreach ( var key in Keys( arguments[0], root ) )
			{
				if ( dictionary.Contains( key ) ) continue;
				if ( root.TryCreate( arguments[1], root, out var item ) ) dictionary[key] = item;
			}

			value = dictionary;
			return true;
		}

		return false;
	}

	//Two keys that differ, which a single sample never gives.
	static IEnumerable<object> Keys( Type keyType, ISampleValueProvider root )
	{
		if ( keyType == typeof( string ) ) return ["first", "second"];
		if ( keyType == typeof( int ) ) return [1, 2];

		if ( keyType.IsEnum )
		{
			var values = Enum.GetValues( keyType );
			return values.Cast<object>().Take( 2 );
		}

		return root.TryCreate( keyType, root, out var one ) ? [one] : [];
	}

	static object Scalar( Type type )
	{
		if ( type == typeof( bool ) ) return true;
		if ( type == typeof( int ) ) return 42;
		if ( type == typeof( uint ) ) return 42u;
		if ( type == typeof( long ) ) return 1234567L;
		if ( type == typeof( ulong ) ) return 1234567UL;
		if ( type == typeof( short ) ) return (short)42;
		if ( type == typeof( ushort ) ) return (ushort)42;
		if ( type == typeof( byte ) ) return (byte)42;
		if ( type == typeof( sbyte ) ) return (sbyte)42;
		if ( type == typeof( float ) ) return 3.5f;
		if ( type == typeof( double ) ) return 3.5;
		if ( type == typeof( decimal ) ) return 3.5m;
		if ( type == typeof( string ) ) return "Hello there";
		if ( type == typeof( char ) ) return 'R';
		if ( type == typeof( object ) ) return "an object";

		return null;
	}
}
