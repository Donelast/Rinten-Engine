using System.Collections.Generic;

namespace Rinten;

/// <summary>A model mid-compile, as exposed to a BuildStep: geometry to read and shapes to add</summary>
public sealed class ModelBuild
{
	private readonly CoreAssets.ModelBuilder builder;
	private readonly Logger log;

	internal ModelBuild( CoreAssets.ModelBuilder builder, string name )
	{
		this.builder = builder;

		Name = name ?? "";
		log = new Logger( "Model" );
	}

	/// <summary>What is being compiled, for a step that wants to say which model it is talking about.</summary>
	public string Name { get; }

	/// <summary>How many vertices the model has so far.</summary>
	public int VertexCount => builder?.VertexCount ?? 0;

	/// <summary>One of them, in the model's own space.</summary>
	public Vertex GetVertex( int index ) => builder is null ? default : builder.VertexAt( index );

	//A shape added here is not subject to the definition's Morphs list: that list picks from what the art brought in, and nobody can list a shape that does not exist until this runs.
	/// <summary>Add a blend shape, or add more to one already there by using its name again.</summary>
	public bool AddMorph( string name, List<int> vertices, List<Vector3> positions, List<Vector3> normals = null )
	{
		if ( builder is null ) return false;

		return builder.AddMorph( name, vertices, positions, normals ) >= 0;
	}

	/// <summary>Say something about what this step did.</summary>
	public void Info( string message ) => log.Info( $"{Name}: {message}" );

	/// <summary>Say something about what it could not do.</summary>
	public void Warning( string message ) => log.Warning( $"{Name}: {message}" );
}
