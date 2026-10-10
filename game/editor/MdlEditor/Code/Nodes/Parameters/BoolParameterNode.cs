using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Outputs the current value of a named boolean graph parameter. <br/><br/><b>Use when:</b>&#160;Reading an externally controlled true or false state. <br/><b>Important:</b>&#160;A missing, unknown, or type-mismatched name produces a compiler warning.</summary>
[Title( "Bool Parameter" ), Group( "Parameters" ), Icon( "toggle_on" )]
public class BoolParameterNode : ParameterNode
{
	public override AnimParameterType ParameterType => AnimParameterType.Bool;

	//A condition on a flag being false is one node in their editor and was two in ours, because reading the parameter and inverting it were separate. A page of "not aiming", "not crouched", "not empty" carried a Not beside every one of them.
	/// <summary>Read the parameter the other way round - true while the flag is clear.</summary>
	[Property]
	public bool Invert { get; set; }

	[Output( typeof( BoolLink ) ), JsonIgnore]
	public object Value => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var read = base.Compile( compiler );

		return Invert && read is not null ? compiler.Builder.Not( read ) : read;
	}
}
