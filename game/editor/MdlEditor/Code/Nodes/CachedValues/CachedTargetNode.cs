using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Caches a target: On Entry samples once, On Exit locks the last active target</summary>
[Title( "Cached Target" ), Group( "Values" ), Icon( "cached" )]
public class CachedTargetNode : CachedValueNode
{
	[Input( typeof( TargetLink ) )]
	public NodeInput Value { get; set; }

	[Output( typeof( TargetLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var source = compiler.EmitValue( this, Value );
		if ( source is null )
		{
			compiler.Warn( "Cached Target has no Value connected" );
			source = compiler.Builder.UnsetTarget();
		}

		return compiler.Builder.Cached( source, CacheOnInit );
	}
}
