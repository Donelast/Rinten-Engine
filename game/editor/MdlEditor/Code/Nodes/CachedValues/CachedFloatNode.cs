using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Caches a float: On Entry samples once, On Exit locks the last active value</summary>
[Title( "Cached Float" ), Group( "Values" ), Icon( "cached" )]
public class CachedFloatNode : CachedValueNode
{
	[Input( typeof( FloatLink ) )]
	public NodeInput Value { get; set; }

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var source = compiler.EmitValue( this, Value );
		if ( source is null )
		{
			compiler.Warn( "Cached Float has no Value connected" );
			source = compiler.ConstFloat( 0.0f );
		}

		return compiler.Builder.Cached( source, CacheOnInit );
	}
}
