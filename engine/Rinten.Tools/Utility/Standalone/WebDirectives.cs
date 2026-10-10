using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;

namespace Editor;

//Tells the AOT web compiler to make serializer generics over game value types, impossible at runtime
internal static class WebDirectives
{
	//An rd.xml for these closed generic types.
	internal static string For( IEnumerable<Type> made, ISet<string> inModule, IEnumerable<Type> kept = null )
	{
		var byAssembly = new SortedDictionary<string, SortedSet<string>>( StringComparer.Ordinal );
		var accessors = new SortedSet<string>( StringComparer.Ordinal );

		void Want( string assembly, string line )
		{
			if ( !byAssembly.TryGetValue( assembly, out var lines ) ) byAssembly[assembly] = lines = new( StringComparer.Ordinal );
			lines.Add( line );
		}

		foreach ( var type in made )
		{
			if ( !Valued( type ) || !Within( type, inModule ) ) continue;

			var definition = type.GetGenericTypeDefinition();
			Want( definition.Assembly.GetName().Name, $"   <Type Name=\"{Escape( Name( type ) )}\"><Method Name=\".ctor\" Dynamic=\"Required\" /></Type>" );

			var args = type.GetGenericArguments();

			//A property's getter and setter are made generic over its type by the serializer's member accessor.
			if ( definition.FullName == "System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1" )
			{
				foreach ( var method in new[] { "CreatePropertyGetter", "CreatePropertySetter", "CreateFieldGetter", "CreateFieldSetter" } )
					accessors.Add( $"    <Method Name=\"{method}\" Dynamic=\"Required\"><GenericArgument Name=\"{Escape( Qualified( args[0] ) )}\" /></Method>" );
			}

			//An immutable collection is filled through its factory's CreateRange<T>, found by reflection.
			if ( definition.FullName == "System.Text.Json.Serialization.Converters.ImmutableEnumerableOfTConverterWithReflection`2" && args[0].IsGenericType )
			{
				var collection = args[0].GetGenericTypeDefinition();
				var factory = collection.FullName.Split( '`' )[0];
				Want( collection.Assembly.GetName().Name, $"   <Type Name=\"{factory}\"><Method Name=\"CreateRange\" Dynamic=\"Required\"><GenericArgument Name=\"{Escape( Qualified( args[1] ) )}\" /></Method></Type>" );
			}
		}

		//Types made by name - what the engine's type library holds - kept whole: their constructors, their members, what reflection reads off them.
		foreach ( var type in kept ?? [] )
		{
			if ( type.ContainsGenericParameters || type.FullName is null || type.FullName.Contains( '<' ) ) continue;
			if ( !Within( type, inModule ) ) continue;

			Want( type.Assembly.GetName().Name, $"   <Type Name=\"{Escape( Name( type ) )}\" Dynamic=\"Required All\" />" );
		}

		var text = new StringBuilder();
		text.AppendLine( "<Directives xmlns=\"http://schemas.microsoft.com/netfx/2013/01/metadata\">" );
		text.AppendLine( " <Application>" );

		foreach ( var (assembly, lines) in byAssembly )
		{
			text.AppendLine( $"  <Assembly Name=\"{assembly}\">" );
			foreach ( var line in lines ) text.AppendLine( line );

			if ( assembly == "System.Text.Json" && accessors.Count > 0 )
			{
				text.AppendLine( "   <Type Name=\"System.Text.Json.Serialization.Metadata.ReflectionMemberAccessor\" Dynamic=\"Required All\">" );
				foreach ( var line in accessors ) text.AppendLine( line );
				text.AppendLine( "   </Type>" );
			}

			text.AppendLine( "  </Assembly>" );
		}

		text.AppendLine( " </Application>" );
		text.AppendLine( "</Directives>" );

		return text.ToString();
	}

	//Whether every assembly the type names - its own, its arguments' at any depth - is in the module.
	private static bool Within( Type type, ISet<string> inModule )
	{
		if ( type.IsArray ) return Within( type.GetElementType(), inModule );
		if ( !inModule.Contains( type.Assembly.GetName().Name ) ) return false;
		return !type.IsGenericType || type.GetGenericArguments().All( a => Within( a, inModule ) );
	}

	//Whether a value type is among the arguments, at any depth.
	private static bool Valued( Type type )
		=> type.IsGenericType && type.GetGenericArguments().Any( a => a.IsValueType || ( a.IsArray && a.GetElementType().IsValueType ) || Valued( a ) );

	//A type's name as a directive writes it: generic arguments in double brackets, assembly-qualified, no versions.
	private static string Name( Type type )
	{
		if ( type.IsArray ) return Name( type.GetElementType() ) + "[]";
		if ( !type.IsGenericType ) return type.FullName;

		return type.GetGenericTypeDefinition().FullName + "[" + string.Join( ",", type.GetGenericArguments().Select( a => "[" + Qualified( a ) + "]" ) ) + "]";
	}

	private static string Qualified( Type type )
		=> $"{Name( type )}, {( type.IsArray ? type.GetElementType() : type ).Assembly.GetName().Name}";

	private static string Escape( string text ) => SecurityElement.Escape( text );
}
