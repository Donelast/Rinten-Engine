using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Caches a bool: On Entry samples once, On Exit locks the last active value</summary>
[Title( "Cached Bool" ), Group( "Values" ), Icon( "cached" )]
public class CachedBoolNode : CachedValueNode
{
	[Input( typeof( BoolLink ) )]
	public NodeInput Value { get; set; }

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var source = compiler.EmitValue( this, Value );
		if ( source is null )
		{
			compiler.Warn( "Cached Bool has no Value connected" );
			source = compiler.ConstBool( false );
		}

		return compiler.Builder.Cached( source, CacheOnInit );
	}
}
