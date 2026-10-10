using Facepunch.ActionGraphs;

namespace Rinten.ActionGraphs;

/// <summary>A <see cref="ISourceLocation"/> that provides <see cref="Facepunch.ActionGraphs.SerializationOptions"/>.</summary>
public interface ISerializationOptionProvider : ISourceLocation
{
	SerializationOptions SerializationOptions { get; }
}

/// <summary>Source location for action graphs that belong to a <see cref="GameResource"/>. These can include scenes and prefabs, or custom resources. This is used for stack traces, and for knowing which asset to save when editing a graph.</summary>
public record GameResourceSourceLocation( GameResource Resource ) : ISourceLocation
{
	public override string ToString()
	{
		return Resource.ToString();
	}
}
