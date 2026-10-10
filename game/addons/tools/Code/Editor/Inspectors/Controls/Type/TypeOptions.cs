namespace Editor.Inspectors;

using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Rinten.ActionGraphs;

/// <summary>What a type is allowed to be, wherever one is picked.</summary>
public sealed record TypeConstraint
{
	/// <summary>Only types that derive from or implement this one.</summary>
	public Type BaseType { get; init; }

	/// <summary>The generic parameter being filled in, whose constraints a type must satisfy.</summary>
	public Type GenericParameter { get; init; }

	/// <summary>When set, the only types on offer.</summary>
	public IReadOnlyList<Type> Options { get; init; }

	/// <summary>Only types a value of this one converts to.</summary>
	public Type ConvertibleFrom { get; init; }

	/// <summary>Whether a generic type nobody has filled in yet may be picked.</summary>
	public bool AllowOpenGeneric { get; init; } = true;

	/// <summary>What a property says its type may be, from the attributes it carries.</summary>
	public static TypeConstraint For( SerializedProperty property )
	{
		if ( property is null ) return new TypeConstraint();

		property.TryGetAttribute<TargetTypeAttribute>( out var targetType );

		var constraint = new TypeConstraint
		{
			BaseType = targetType?.Type,
			ConvertibleFrom = property.GetAttributes<HasConversionFromAttribute>().FirstOrDefault()?.Type
		};

		//A property built in code can know more than an attribute could say - an action graph node
		//fills in a generic parameter, and a value that may be one of a handful of types names them.
		if ( property is not ITypeConstraintSource source || source.TypeConstraint is not { } extra )
			return constraint;

		return constraint with
		{
			BaseType = constraint.BaseType ?? extra.BaseType,
			GenericParameter = extra.GenericParameter ?? constraint.GenericParameter,
			Options = extra.Options ?? constraint.Options,
			ConvertibleFrom = constraint.ConvertibleFrom ?? extra.ConvertibleFrom,
			AllowOpenGeneric = constraint.AllowOpenGeneric && extra.AllowOpenGeneric
		};
	}

	/// <summary>What one generic argument of <paramref name="definition"/> may be.</summary>
	public static TypeConstraint For( Type definition, int argument )
	{
		return new TypeConstraint { GenericParameter = definition.GetGenericArguments()[argument] };
	}
}

//Implemented by properties an editor builds rather than reflects - what they know comes from the thing they were built for, and there is no attribute on anything to carry it.
/// <summary>A property that knows what its type may be beyond what its attributes say.</summary>
public interface ITypeConstraintSource
{
	TypeConstraint TypeConstraint { get; }
}

/// <summary>The types a picker offers, and the rules that decide which of them it may.</summary>
public static class TypeOptions
{
	/// <summary>One type on offer, and where it sits in the list.</summary>
	public record Option( Type Type, string Title, string Path, string Description, string Icon );

	//Types that are always worth offering, whether or not the type library lists them.
	private static Type[] SystemTypes { get; } =
	[
		typeof( int ),
		typeof( float ),
		typeof( string ),
		typeof( bool ),
		typeof( GameObject ),
		typeof( GameTransform ),
		typeof( Color ),
		typeof( Vector2 ),
		typeof( Vector3 ),
		typeof( Vector4 ),
		typeof( Angles ),
		typeof( Rotation ),
		typeof( object )
	];

	//Containers people reach for, which the type library has no reason to list.
	private static Type[] CommonGenerics { get; } =
	[
		typeof( List<> ),
		typeof( Dictionary<,> ),
		typeof( HashSet<> ),
		typeof( Queue<> ),
		typeof( Stack<> )
	];

	/// <summary>Every type that satisfies <paramref name="constraint"/>, ready to be listed.</summary>
	public static IReadOnlyList<Option> For( TypeConstraint constraint )
	{
		constraint ??= new TypeConstraint();

		if ( constraint.Options is { Count: > 0 } explicitOptions )
		{
			return explicitOptions.Select( OptionFor ).ToArray();
		}

		var listed = new HashSet<Type>();
		var result = new List<Option>();

		foreach ( var type in SystemTypes )
		{
			if ( listed.Add( type ) && Satisfies( type, constraint ) )
				result.Add( OptionFor( type ) );
		}

		if ( constraint.AllowOpenGeneric )
		{
			foreach ( var type in CommonGenerics )
			{
				if ( listed.Add( type ) && Satisfies( type, constraint ) )
					result.Add( OptionFor( type ) );
			}
		}

		foreach ( var typeDesc in Listable() )
		{
			var type = typeDesc.TargetType;

			if ( type is null ) continue;
			if ( typeDesc.IsStatic ) continue;
			if ( typeDesc.HasAttribute<CompilerGeneratedAttribute>() ) continue;
			if ( typeDesc.Name.StartsWith( '<' ) || typeDesc.Name.StartsWith( '_' ) ) continue;
			if ( type.IsGenericType && !(constraint.AllowOpenGeneric && type.IsGenericTypeDefinition) ) continue;
			if ( !listed.Add( type ) ) continue;
			if ( !Satisfies( type, constraint ) ) continue;

			result.Add( OptionFor( type ) );
		}

		return result;
	}

	private static IEnumerable<TypeDescription> Listable()
	{
		var typeLibrary = Game.TypeLibrary;

		if ( typeLibrary is null ) return [];

		return typeLibrary.GetTypes<Component>()
			.Union( typeLibrary.GetTypes<GameResource>() )
			.Union( typeLibrary.GetTypes().Where( x => x.TargetType?.Assembly.IsPackage() ?? false ) );
	}

	/// <summary>Whether a type may be picked where <paramref name="constraint"/> holds.</summary>
	public static bool Satisfies( Type type, TypeConstraint constraint )
	{
		if ( type is null ) return false;
		if ( constraint is null ) return true;

		if ( constraint.Options is { Count: > 0 } explicitOptions && !explicitOptions.Contains( type ) )
			return false;

		if ( !CanSatisfy( type, constraint.BaseType ) )
			return false;

		if ( !SatisfiesConstraints( type, constraint.GenericParameter ) )
			return false;

		if ( constraint.ConvertibleFrom is { } from && !type.ContainsGenericParameters && !CanConvert( from, type ) )
			return false;

		return true;
	}

	/// <summary>How a type reads in a field: <c>Dictionary&lt;string, ?&gt;</c>, with a question mark for a generic argument nobody has filled in.</summary>
	public static string DisplayName( Type type )
	{
		if ( type is null ) return "None";
		if ( type.IsGenericParameter ) return "?";

		if ( type.IsArray )
		{
			var commas = new string( ',', type.GetArrayRank() - 1 );
			return $"{DisplayName( type.GetElementType() )}[{commas}]";
		}

		var name = ShortName( type );

		if ( !type.IsGenericType ) return name;

		return $"{name}<{string.Join( ", ", type.GetGenericArguments().Select( DisplayName ) )}>";
	}

	/// <summary>The generic arguments of a type, with <see langword="null"/> for each slot nobody has filled in yet. Empty for a type that isn't generic.</summary>
	public static Type[] ArgumentsOf( Type type )
	{
		if ( type is null || !type.IsGenericType ) return [];

		return type.GetGenericArguments()
			.Select( x => x.IsGenericParameter ? null : x )
			.ToArray();
	}

	/// <summary>The same generic type with one argument replaced - <see langword="null"/> empties that slot, leaving a type that is only half filled in but still a type.</summary>
	public static Type WithArgument( Type type, int index, Type argument )
	{
		if ( type is null || !type.IsGenericType ) return type;

		var definition = type.GetGenericTypeDefinition();
		var parameters = definition.GetGenericArguments();
		var arguments = type.GetGenericArguments();

		arguments[index] = argument ?? parameters[index];

		//Nothing filled in at all is the definition itself, which is how it was picked.
		if ( arguments.SequenceEqual( parameters ) ) return definition;

		try
		{
			return definition.MakeGenericType( arguments );
		}
		catch ( Exception )
		{
			//Constraints this argument doesn't satisfy. Keep what was there rather than throwing away the arguments somebody already chose.
			return type;
		}
	}

	/// <summary>What the definition calls each of its generic parameters.</summary>
	public static string[] ParameterNamesOf( Type type )
	{
		if ( type is null || !type.IsGenericType ) return [];

		return type.GetGenericTypeDefinition()
			.GetGenericArguments()
			.Select( x => x.Name )
			.ToArray();
	}

	/// <summary>The name without the arity mark a generic type carries.</summary>
	public static string ShortName( Type type )
	{
		var name = Game.TypeLibrary?.GetType( type )?.Title ?? type.Name;
		var arity = name.IndexOf( '`' );

		return arity > 0 ? name[..arity] : name;
	}

	private static Option OptionFor( Type type )
	{
		var typeDesc = Game.TypeLibrary?.GetType( type );

		if ( typeDesc is null )
			return new Option( type, ShortName( type ), "System", null, IconFor( type ) );

		return new Option( type, ShortName( type ), PathOf( typeDesc ), typeDesc.Description, typeDesc.Icon ?? IconFor( type ) );
	}

	private static string IconFor( Type type )
	{
		if ( type.IsAssignableTo( typeof( Resource ) ) ) return "description";
		if ( type.IsAssignableTo( typeof( Component ) ) ) return "category";
		if ( type.IsGenericTypeDefinition ) return "data_array";

		return "check_box_outline_blank";
	}

	//Where a type sits in the list, as a path of groups.
	private static string PathOf( TypeDescription typeDesc )
	{
		var type = typeDesc.TargetType;

		if ( type.DeclaringType is { } declaring && Game.TypeLibrary?.GetType( declaring ) is { } parent )
		{
			return $"{PathOf( parent )}/{ShortName( declaring )}";
		}

		var prefix = typeDesc.Group ?? AssemblyQualifiedPath( typeDesc );

		if ( type.IsAssignableTo( typeof( Resource ) ) ) return $"Resource/{prefix}";
		if ( type.IsAssignableTo( typeof( Component ) ) ) return $"Component/{prefix}";
		if ( SystemTypes.Contains( type ) ) return typeDesc.Group ?? typeDesc.Namespace?.Replace( '.', '/' ) ?? "Rinten";

		return prefix;
	}

	private static string AssemblyQualifiedPath( TypeDescription typeDesc )
	{
		return !string.IsNullOrEmpty( typeDesc.Namespace )
			? typeDesc.Namespace.Replace( '.', '/' )
			: FormatAssemblyName( typeDesc.TargetType.Assembly );
	}

	private static string FormatAssemblyName( Assembly assembly )
	{
		var name = assembly.GetName().Name!;

		if ( name.StartsWith( "package.", StringComparison.OrdinalIgnoreCase ) )
			name = name["package.".Length..];

		if ( name.StartsWith( "local.", StringComparison.OrdinalIgnoreCase ) )
			name = name["local.".Length..];

		return name.ToTitleCase();
	}

	//Whether a type is, or could still become, one that fits baseType. A generic nobody has filled in yet is
	//judged on its definition: List<> can end up an IEnumerable<int> , so it is offered and its arguments decide.
	private static bool CanSatisfy( Type type, Type baseType )
	{
		if ( baseType is null ) return true;
		if ( type.IsAssignableTo( baseType ) ) return true;
		if ( !type.ContainsGenericParameters ) return false;

		var wanted = DefinitionOf( baseType );

		for ( var candidate = type; candidate is not null; candidate = candidate.BaseType )
		{
			if ( DefinitionOf( candidate ) == wanted ) return true;

			foreach ( var iface in candidate.GetInterfaces() )
			{
				if ( DefinitionOf( iface ) == wanted ) return true;
			}
		}

		return false;
	}

	private static Type DefinitionOf( Type type )
	{
		return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
	}

	//Whether a type could stand in for a generic parameter.
	private static bool SatisfiesConstraints( Type type, Type genericParam )
	{
		if ( genericParam is null ) return true;

		if ( !AttributesSatisfiedBy( genericParam.GenericParameterAttributes, type ) )
			return false;

		foreach ( var constraint in genericParam.GetGenericParameterConstraints() )
		{
			if ( ApplyGenericArgument( constraint, genericParam, type ) is not { } resolved )
				return false;

			if ( resolved.IsGenericParameter )
			{
				//A bit worried about a stack overflow here
				if ( resolved != genericParam && !SatisfiesConstraints( type, resolved ) )
					return false;
			}
			else if ( !CanSatisfy( type, resolved ) )
			{
				return false;
			}
		}

		foreach ( var hasImpl in genericParam.GetCustomAttributes<HasImplementationAttribute>() )
		{
			//Easy case
			if ( type.IsAssignableTo( hasImpl.BaseType ) ) continue;

			var anyImplementing = Game.TypeLibrary?
				.GetTypes( hasImpl.BaseType )
				.Where( x => !x.IsAbstract && !x.IsInterface )
				.Any( x => x.TargetType.IsAssignableTo( type ) ) ?? false;

			if ( !anyImplementing ) return false;
		}

		return true;
	}

	//The class , struct and new() part of a parameter's constraints.
	private static bool AttributesSatisfiedBy( GenericParameterAttributes attributes, Type type )
	{
		if ( attributes.HasFlag( GenericParameterAttributes.ReferenceTypeConstraint ) && type.IsValueType )
			return false;

		if ( attributes.HasFlag( GenericParameterAttributes.NotNullableValueTypeConstraint )
			&& (!type.IsValueType || Nullable.GetUnderlyingType( type ) is not null) )
			return false;

		if ( attributes.HasFlag( GenericParameterAttributes.DefaultConstructorConstraint )
			&& !type.IsValueType
			&& !type.ContainsGenericParameters
			&& type.GetConstructor( Type.EmptyTypes ) is null )
			return false;

		return true;
	}

	//Given a type Foo<T1,T2> , a genericParam T1 , and a genericArg Bar , replace references to T1 with Bar to
	//create the type Foo<Bar,T2> . Note that genericParam might come from some other method or type definition.
	private static Type ApplyGenericArgument( Type type, Type genericParam, Type genericArg )
	{
		if ( type == genericParam ) return genericArg;
		if ( !type.ContainsGenericParameters ) return type;

		if ( type.IsByRef || type.IsArray )
		{
			if ( ApplyGenericArgument( type.GetElementType(), genericParam, genericArg ) is not { } elemType )
				return null;

			if ( type.IsByRef ) return elemType.MakeByRefType();

			return type.GetArrayRank() == 1
				? elemType.MakeArrayType()
				: elemType.MakeArrayType( type.GetArrayRank() );
		}

		if ( !type.IsGenericType ) return null;

		try
		{
			return type.GetGenericTypeDefinition()
				.MakeGenericType( type.GetGenericArguments()
					.Select( x => ApplyGenericArgument( x, genericParam, genericArg ) )
					.ToArray() );
		}
		catch ( ArgumentException )
		{
			//Generic constraints not satisfied
			return null;
		}
	}

	private static bool CanConvert( Type from, Type to )
	{
		if ( from == typeof( object ) ) return true;
		if ( to == typeof( object ) || to == typeof( string ) ) return true;

		//Interfaces false positive for some reason
		if ( to.IsInterface && from.IsSealed && !from.IsAssignableTo( to ) )
			return false;

		try
		{
			_ = Expression.Convert( Expression.Throw( null, from ), to );
			return true;
		}
		catch
		{
			return false;
		}
	}
}
