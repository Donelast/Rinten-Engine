using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;

namespace Rinten.Internal;

//Finds types with members wearing an attribute by reading assembly metadata instead of reflection
internal static class MemberAttributeScan
{
	//What one assembly's members wear, worked out the first time it is asked about.
	private sealed class Index
	{
		/// <summary>For each attribute type found on a member, the types declaring such a member. Null where the metadata would not read.</summary>
		public Dictionary<Type, HashSet<Type>> Owners;

		/// <summary>Types declaring a member with an attribute that would not resolve - nobody can say what it is.</summary>
		public HashSet<Type> Unresolved;

		/// <summary>The answer for each kind of attribute asked about.</summary>
		public readonly ConcurrentDictionary<Type, HashSet<Type>> ByKind = new();
	}

	//Weak per-assembly index, skipped by hotload so a new assembly is always read afresh
	[SkipHotload]
	private static readonly ConditionalWeakTable<Assembly, Index> indexes = new();

	//The types of assembly declaring a method, property, field or event wearing an attribute that is a kind - or null where
	//nobody can say, and every type has to be looked at. Types are as the assembly defines them: a generic one by its definition.
	internal static HashSet<Type> Declaring( Assembly assembly, Type kind )
	{
		if ( assembly is null ) return null;

		var index = indexes.GetValue( assembly, Build );

		if ( index.Owners is null ) return null;

		return index.ByKind.GetOrAdd( kind, k =>
		{
			var types = new HashSet<Type>( index.Unresolved );

			foreach ( var (attribute, owners) in index.Owners )
			{
				if ( attribute.IsAssignableTo( k ) ) types.UnionWith( owners );
			}

			return types;
		} );
	}

	//Whether a type, or any type it derives from, declares a member wearing an attribute that is a kind - true where nobody can say.
	internal static bool MayCarry( Type type, Type kind )
	{
		for ( var at = type; at is not null && at != typeof( object ); at = at.BaseType )
		{
			var definition = at.IsConstructedGenericType ? at.GetGenericTypeDefinition() : at;
			var declaring = Declaring( definition.Assembly, kind );

			if ( declaring is null || declaring.Contains( definition ) ) return true;
		}

		return false;
	}

	private static unsafe Index Build( Assembly assembly )
	{
		var index = new Index();

		if ( assembly.IsDynamic || !assembly.TryGetRawMetadata( out var blob, out var length ) ) return index;

		try
		{
			var reader = new MetadataReader( blob, length );
			var module = assembly.ManifestModule;

			//Properties and events do not say which type declares them; their types do.
			Dictionary<EntityHandle, TypeDefinitionHandle> declaring = null;

			var attributeTypes = new Dictionary<EntityHandle, Type>();
			var ownerTypes = new Dictionary<TypeDefinitionHandle, Type>();

			var owners = new Dictionary<Type, HashSet<Type>>();
			var unresolved = new HashSet<Type>();

			foreach ( var handle in reader.CustomAttributes )
			{
				var attribute = reader.GetCustomAttribute( handle );
				var parent = attribute.Parent;

				TypeDefinitionHandle owner;

				switch ( parent.Kind )
				{
					case HandleKind.MethodDefinition:
						owner = reader.GetMethodDefinition( (MethodDefinitionHandle)parent ).GetDeclaringType();
						break;

					case HandleKind.FieldDefinition:
						owner = reader.GetFieldDefinition( (FieldDefinitionHandle)parent ).GetDeclaringType();
						break;

					case HandleKind.PropertyDefinition:
					case HandleKind.EventDefinition:
						declaring ??= Declarers( reader );
						if ( !declaring.TryGetValue( parent, out owner ) ) continue;
						break;

					default:
						continue;
				}

				if ( !ownerTypes.TryGetValue( owner, out var ownerType ) )
				{
					ownerType = module.ResolveType( MetadataTokens.GetToken( owner ) );
					ownerTypes[owner] = ownerType;
				}

				if ( ownerType is null ) continue;

				if ( !attributeTypes.TryGetValue( attribute.Constructor, out var attributeType ) )
				{
					attributeType = AttributeTypeOf( module, attribute.Constructor );
					attributeTypes[attribute.Constructor] = attributeType;
				}

				if ( attributeType is null )
				{
					unresolved.Add( ownerType );
					continue;
				}

				if ( !owners.TryGetValue( attributeType, out var set ) ) owners[attributeType] = set = new HashSet<Type>();

				set.Add( ownerType );
			}

			index.Owners = owners;
			index.Unresolved = unresolved;
		}
		catch ( Exception )
		{
			//Metadata this cannot read is metadata every scan reads the slow way.
			index.Owners = null;
		}

		return index;
	}

	private static Dictionary<EntityHandle, TypeDefinitionHandle> Declarers( MetadataReader reader )
	{
		var declaring = new Dictionary<EntityHandle, TypeDefinitionHandle>();

		foreach ( var typeHandle in reader.TypeDefinitions )
		{
			var definition = reader.GetTypeDefinition( typeHandle );

			foreach ( var property in definition.GetProperties() ) declaring[property] = typeHandle;
			foreach ( var e in definition.GetEvents() ) declaring[e] = typeHandle;
		}

		return declaring;
	}

	//The type an attribute constructor belongs to, or null if it will not resolve.
	private static Type AttributeTypeOf( Module module, EntityHandle constructor )
	{
		try
		{
			return module.ResolveMethod( MetadataTokens.GetToken( constructor ) )?.DeclaringType;
		}
		catch ( Exception )
		{
			//A missing reference or a shape this cannot name - whatever it is, the members wearing it are looked at by every scan rather than by none.
			return null;
		}
	}
}
