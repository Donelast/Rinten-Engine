using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Caches a vector: On Entry samples once, On Exit locks the last active vector</summary>
[Title( "Cached Vector" ), Group( "Values" ), Icon( "cached" )]
public class CachedVectorNode : CachedValueNode
{
	[Input( typeof( VectorLink ) )]
	public NodeInput Value { get; set; }

	[Output( typeof( VectorLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var source = compiler.EmitValue( this, Value );
		if ( source is null )
		{
			compiler.Warn( "Cached Vector has no Value connected" );
			source = compiler.ConstVector( Vector3.Zero );
		}

		return compiler.Builder.Cached( source, CacheOnInit );
	}
}
