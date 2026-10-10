namespace Rinten;

/// <summary>Says that this asset type can be loaded as some other kind of resource.</summary>
[AttributeUsage( AttributeTargets.Class )]
public sealed class ProvidesResourceAttribute : System.Attribute
{
	public System.Type ResourceType { get; }

	public ProvidesResourceAttribute( System.Type resourceType )
	{
		ResourceType = resourceType;
	}
}
