using System.Text.Json.Serialization;

namespace Rinten.Services;

/// <summary>Server-computed package metadata, polymorphic on "$type"; unknown types fall back to this base</summary>
[JsonPolymorphic( TypeDiscriminatorPropertyName = "$type", IgnoreUnrecognizedTypeDiscriminators = true )]
[JsonDerivedType( typeof( ModelMetaData ), "model" )]
[JsonDerivedType( typeof( MaterialMetaData ), "material" )]
[JsonDerivedType( typeof( ClothingMetaData ), "clothing" )]
public class BaseMetaData
{
	/// <summary>Sets or clears this type's derived facet tags, which search uses as internal filter tags</summary>
	public virtual void GetAutoTags( HashSet<string> tags )
	{
	}

	//Adds or removes tag, so derived facets can't be faked by users or left stale
	protected static void SetTag( HashSet<string> tags, string tag, bool present )
	{
		if ( present ) tags.Add( tag );
		else tags.Remove( tag );
	}

	/// <summary>Mutates a package's category id list in place for the search index: removes the bucket categories this metadata type manages (so stale ones don't linger) and adds the ones the current metadata maps to. Raw category ids - the concrete type owns the id constants. Default is a no-op.</summary>
	public virtual void UpdateCategories( List<int> categories )
	{
	}
}
