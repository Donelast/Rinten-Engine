using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Maps a float from the input range to the output range, clamped</summary>
[Title( "Remap" ), Group( "Values" ), Icon( "swap_calls" )]
public class FloatRemapNode : Node
{
	[Input( typeof( FloatLink ) )]
	public NodeInput Value { get; set; }

	[Property]
	public float InputMin { get; set; }

	[Property]
	public float InputMax { get; set; } = 1.0f;

	[Property]
	public float OutputMin { get; set; }

	[Property]
	public float OutputMax { get; set; } = 1.0f;

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var value = compiler.EmitValue( this, Value );
		if ( value is null )
			value = compiler.ConstFloat( 0.0f );
		return compiler.Builder.Remap(
			value,
			new Vector2( InputMin, InputMax ),
			new Vector2( OutputMin, OutputMax ) );
	}
}
