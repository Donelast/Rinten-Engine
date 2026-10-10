using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Caches an ID: On Entry samples once, On Exit locks the last active ID</summary>
[Title( "Cached ID" ), Group( "Values" ), Icon( "cached" )]
public class CachedIDNode : CachedValueNode
{
	[Input( typeof( IDLink ) )]
	public NodeInput Value { get; set; }

	[Output( typeof( IDLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var source = compiler.EmitValue( this, Value );
		if ( source is null )
		{
			compiler.Warn( "Cached ID has no Value connected" );
			source = compiler.ConstID();
		}

		return compiler.Builder.Cached( source, CacheOnInit );
	}
}
