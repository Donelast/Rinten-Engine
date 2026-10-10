namespace Editor.Inspectors;

using System.Reflection;

/// <summary>What it takes to keep a resource inside the thing that uses it, rather than in a file.</summary>
public static class EmbeddedResources
{
	/// <summary>The compiler name that marks a resource as one somebody edits in place.</summary>
	public const string Compiler = "embed";

	/// <summary>Whether a resource of this type may be embedded at all.</summary>
	public static bool CanEmbed( Type propertyType )
	{
		if ( propertyType is null ) return false;
		if ( !propertyType.IsSubclassOf( typeof( GameResource ) ) ) return false;

		var attribute = propertyType.GetCustomAttribute<AssetTypeAttribute>();

		return attribute is not null && !attribute.Flags.Contains( AssetTypeFlags.NoEmbedding );
	}

	/// <summary>Whether what a property holds is embedded rather than loaded from a file.</summary>
	public static bool IsEmbedded( Resource resource )
	{
		return resource?.EmbeddedResource is { ResourceCompiler: Compiler };
	}

	/// <summary>Says that this resource is embedded, and which type it is where that isn't the type of the property holding it.</summary>
	public static void MarkEmbedded( SerializedProperty property, Resource resource )
	{
		if ( !resource.IsValid() ) return;

		var type = Game.TypeLibrary.GetType( resource.GetType() );
		var typeName = property.PropertyType.Equals( type?.TargetType ) ? null : type?.Name;

		resource.EmbeddedResource = new()
		{
			ResourceCompiler = Compiler,
			TypeName = typeName
		};
	}

	/// <summary>The embedded resource a property holds, making an empty one where it holds nothing.</summary>
	public static Resource EnsureResource( SerializedProperty property )
	{
		var resource = property.GetValue<Resource>( null );

		if ( resource.IsValid() )
		{
			MarkEmbedded( property, resource );
			return resource;
		}

		if ( property.PropertyType.IsAbstract ) return null;

		resource = EditorTypeLibrary.Create<Resource>( property.PropertyType.Name );

		if ( !resource.IsValid() ) return null;

		MarkEmbedded( property, resource );
		property.SetValue( resource );

		return resource;
	}

	/// <summary>A fresh embedded resource carrying everything the file it came from had, so switching to embedded keeps the work rather than starting over.</summary>
	public static GameResource CreateFromFile( SerializedProperty target, GameResource source )
	{
		if ( target.PropertyType.IsAbstract ) return null;

		var data = source.Serialize();
		var resource = EditorTypeLibrary.Create<GameResource>( target.PropertyType.Name );

		resource.CopyFrom( source );

		resource.EmbeddedResource = new()
		{
			ResourceCompiler = Compiler,
			Data = data
		};

		target.SetValue( resource );

		return resource;
	}

	/// <summary>Throws away what is there and starts from an empty resource of the same type.</summary>
	public static void Reset( SerializedProperty property )
	{
		if ( property.PropertyType.IsAbstract ) return;

		var resource = EditorTypeLibrary.Create<Resource>( property.PropertyType.Name );

		if ( !resource.IsValid() ) return;

		MarkEmbedded( property, resource );
		property.SetValue( resource );
	}

	/// <summary>Swaps the resource for an empty one of another type it is allowed to be.</summary>
	public static void SetType( SerializedProperty property, TypeDescription type )
	{
		var resource = EditorTypeLibrary.Create<Resource>( type.Name );

		if ( !resource.IsValid() ) return;

		MarkEmbedded( property, resource );
		property.SetValue( resource );
	}

	/// <summary>The types this property could hold instead of the one it does.</summary>
	public static List<TypeDescription> SupportedTypes( SerializedProperty property, Resource current )
	{
		return EditorTypeLibrary.GetTypes( property.PropertyType )
			.Where( x => x.TargetType.IsPublic && !x.IsAbstract && x.TargetType.IsSubclassOf( typeof( GameResource ) ) )
			.Where( x => x.TargetType != current?.GetType() )
			.OrderBy( x => x.Name )
			.ToList();
	}
}
