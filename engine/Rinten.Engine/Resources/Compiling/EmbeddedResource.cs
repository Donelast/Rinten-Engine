using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rinten.Resources;

/// <summary>JSON for a resource that can be standalone or embedded in a GameResource, compiled in C#</summary>
public struct EmbeddedResource
{
	/// <summary>The name of the ResourceCompiler to use</summary>
	[JsonPropertyName( "$compiler" )]
	public string ResourceCompiler { get; set; }

	/// <summary>The name of the ResourceGenerator that created this resource. This is basically a sub-compiler.</summary>
	[JsonPropertyName( "$source" )]
	public string ResourceGenerator { get; set; }

	/// <summary>Sometimes we'll want to embed a child class of a resource</summary>
	[JsonPropertyName( $"$type" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public string TypeName { get; set; }

	/// <summary>Data that is serialized/deserialized from the ResourceGenerator</summary>
	[JsonPropertyName( "data" )]
	public JsonObject Data { get; set; }

	/// <summary>If this resource has been compiled to disk then this is the path to that resource. This avoids the need to generate this resource again.</summary>
	[JsonPropertyName( "compiled" )]
	public string CompiledPath { get; set; }
}
