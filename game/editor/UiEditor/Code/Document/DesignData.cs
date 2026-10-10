using System.Collections;
using System.IO;
using System.Text.Json.Nodes;
using Rinten.UI;

namespace Editor.UiEditor;

/// <summary>Design-time values for a document's parameters, saved in Name.razor.design.json; games ignore it</summary>
public sealed class DesignData
{
	public string Path { get; }

	JsonObject _values = new();

	public DesignData( string razorPath )
	{
		Path = razorPath + ".design.json";
		Load();
	}

	public IEnumerable<string> Names => _values.Select( x => x.Key );

	public bool Has( string name ) => _values.ContainsKey( name );

	public JsonNode Get( string name ) => _values[name];

	void Load()
	{
		_values = new JsonObject();

		if ( !File.Exists( Path ) ) return;

		try
		{
			_values = JsonNode.Parse( File.ReadAllText( Path ) ) as JsonObject ?? new JsonObject();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"UI editor: could not read {Path}: {e.Message}" );
		}
	}

	void Save()
	{
		try
		{
			if ( _values.Count == 0 )
			{
				if ( File.Exists( Path ) ) File.Delete( Path );
				return;
			}

			File.WriteAllText( Path, _values.ToJsonString( new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"UI editor: could not write {Path}: {e.Message}" );
		}
	}

	/// <summary>Remember a value for a member, as JSON, and save.</summary>
	public void Set( string name, object value, Type type )
	{
		_values[name] = value is null ? null : Json.ToNode( value, type );
		Save();
	}

	/// <summary>Put a value back as it was kept - what an undo has of it.</summary>
	public void SetNode( string name, JsonNode node )
	{
		_values[name] = node?.DeepClone();
		Save();
	}

	/// <summary>Forget a member's value, so the instance keeps its own default.</summary>
	public void Clear( string name )
	{
		_values.Remove( name );
		Save();
	}

	/// <summary>Put every remembered value on an instance - the component, or the panel - that has a member of that name.</summary>
	public void Apply( object instance )
	{
		if ( instance is null || _values.Count == 0 ) return;

		var type = TypeLibrary.GetType( instance.GetType() );
		if ( type is null ) return;

		foreach ( var (name, node) in _values.ToList() )
		{
			if ( Member( type, name ) is not { } member ) continue;

			try
			{
				var value = node is null ? null : Json.FromNode( node, member.type );
				member.set( instance, value );
			}
			catch ( Exception e )
			{
				Log.Warning( $"UI editor: {name} could not take {node}: {e.Message}" );
			}
		}
	}

	/// <summary>The members of a type a designer may want to set: public properties and fields that can be written, a parameter or a property or plain.</summary>
	public static IEnumerable<(string name, Type type, Func<object, object> get, Action<object, object> set, DesignDataAttribute sample)> Members( TypeDescription type )
	{
		var engine = typeof( Panel ).Assembly;

		foreach ( var property in type.Properties )
		{
			if ( property.IsStatic || !property.CanWrite || !property.CanRead ) continue;
			if ( !property.IsPublic && !property.HasAttribute<ParameterAttribute>() && !property.HasAttribute<PropertyAttribute>() ) continue;
			if ( property.HasAttribute<HideAttribute>() ) continue;
			if ( property.PropertyType.IsAssignableTo( typeof( Panel ) ) || property.PropertyType.IsAssignableTo( typeof( Delegate ) ) ) continue;

			//The document's own, not the engine's: a component's transform and a panel's tooltip are not what a designer came to set.
			if ( DeclaredIn( type.TargetType.GetProperty( property.Name ), engine ) && !property.HasAttribute<ParameterAttribute>() ) continue;

			var p = property;
			yield return ( p.Name, p.PropertyType, o => p.GetValue( o ), ( o, v ) => p.SetValue( o, v ), p.GetCustomAttribute<DesignDataAttribute>() );
		}

		foreach ( var field in type.Fields )
		{
			if ( field.IsStatic || !field.IsPublic ) continue;
			if ( field.HasAttribute<HideAttribute>() ) continue;
			if ( DeclaredIn( type.TargetType.GetField( field.Name ), engine ) ) continue;
			if ( field.FieldType.IsAssignableTo( typeof( Panel ) ) || field.FieldType.IsAssignableTo( typeof( Delegate ) ) ) continue;

			var f = field;
			yield return ( f.Name, f.FieldType, o => f.GetValue( o ), ( o, v ) => f.SetValue( o, v ), f.GetCustomAttribute<DesignDataAttribute>() );
		}
	}

	//Whether a member was declared by a type of an assembly - through reflection, since the library's own answer is not always there.
	static bool DeclaredIn( System.Reflection.MemberInfo member, System.Reflection.Assembly assembly )
		=> member?.DeclaringType?.Assembly == assembly;

	static (Type type, Action<object, object> set)? Member( TypeDescription type, string name )
	{
		foreach ( var member in Members( type ) )
		{
			if ( string.Equals( member.name, name, StringComparison.Ordinal ) ) return ( member.type, member.set );
		}

		return null;
	}

	//Samples

	/// <summary>A value for a member with nothing in it yet: what the factory makes when there is one, else a list of so many defaults, else a default.</summary>
	public static object Sample( Type type, DesignDataAttribute attribute, int count )
	{
		if ( attribute?.Factory is { } factoryType && Activator.CreateInstance( factoryType ) is IDesignDataFactory factory )
		{
			if ( !IsList( type, out var itemType ) ) return factory.Make( 0 );

			var list = MakeList( type, itemType );
			for ( var i = 0; i < count; i++ ) list.Add( factory.Make( i ) );
			return list;
		}

		if ( IsList( type, out var item ) )
		{
			var list = MakeList( type, item );
			for ( var i = 0; i < count; i++ ) list.Add( Default( item, i ) );
			return list;
		}

		return Default( type, 0 );
	}

	/// <summary>Whether a type is a list the editor can fill, and of what.</summary>
	public static bool IsList( Type type, out Type item )
	{
		item = null;

		if ( type.IsArray ) { item = type.GetElementType(); return true; }

		if ( type.IsGenericType && ( type.GetGenericTypeDefinition() == typeof( List<> ) || type.GetGenericTypeDefinition() == typeof( IList<> ) || type.GetGenericTypeDefinition() == typeof( IEnumerable<> ) || type.GetGenericTypeDefinition() == typeof( IReadOnlyList<> ) ) )
		{
			item = type.GetGenericArguments()[0];
			return true;
		}

		return false;
	}

	public static IList MakeList( Type type, Type item )
	{
		if ( type.IsArray ) return new List<object>();

		return Activator.CreateInstance( typeof( List<> ).MakeGenericType( item ) ) as IList;
	}

	/// <summary>Something to see for a type: a number, some words, a colour, or a fresh object with its strings filled.</summary>
	public static object Default( Type type, int index )
	{
		if ( type == typeof( string ) ) return $"Item {index + 1}";
		if ( type == typeof( int ) || type == typeof( long ) ) return Convert.ChangeType( index + 1, type );
		if ( type == typeof( float ) || type == typeof( double ) ) return Convert.ChangeType( index + 1, type );
		if ( type == typeof( bool ) ) return index % 2 == 0;
		if ( type == typeof( Color ) ) return Color.Random;
		if ( type.IsEnum ) return Enum.GetValues( type ).GetValue( 0 );

		try
		{
			var made = Activator.CreateInstance( type );

			//Strings inside a sample object say which one it is.
			if ( made is not null && TypeLibrary.GetType( type ) is { } description )
			{
				foreach ( var property in description.Properties )
				{
					if ( property.CanWrite && property.PropertyType == typeof( string ) && !property.IsStatic )
						property.SetValue( made, $"{property.Name} {index + 1}" );
				}
			}

			return made;
		}
		catch
		{
			return null;
		}
	}
}
