using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Outputs a constant float value. <br/><br/><b>Use when:</b>&#160;Supplying a fixed numeric value to another node.</summary>
[Title( "Float" ), Group( "Values" ), Icon( "looks_one" )]
public class ConstFloatNode : Node
{
	[Property]
	public float Value { get; set; }

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
		=> compiler.Builder.Constant( Value );
}
