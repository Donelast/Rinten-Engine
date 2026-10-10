using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Applies the selected math operation to A and B; divide by near zero returns 0</summary>
[Title( "Float Math" ), Group( "Values" ), Icon( "calculate" )]
public class FloatMathNode : Node
{
	[Input( typeof( FloatLink ) )]
	public NodeInput A { get; set; }

	/// <summary>Optional second operand. When unconnected, Value B is used.</summary>
	[Input( typeof( FloatLink ) )]
	public NodeInput B { get; set; }

	[Property]
	public AnimGraphBuilder.FloatMathOperation Operator { get; set; } = AnimGraphBuilder.FloatMathOperation.Add;

	/// <summary>Constant second operand when B isn't connected.</summary>
	[Property]
	public float ValueB { get; set; }

	/// <summary>Return the absolute value of the result.</summary>
	[Property]
	public bool AbsoluteResult { get; set; }

	/// <summary>Negate the result.</summary>
	[Property]
	public bool NegateResult { get; set; }

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var a = compiler.EmitValue( this, A );
		if ( a is null )
			a = compiler.ConstFloat( 0.0f );
		var b = compiler.EmitValue( this, B );
		return compiler.Builder.FloatMath( Operator, a, new AnimGraphBuilder.FloatMathSettings
		{
			B = b,
			ValueB = ValueB,
			AbsoluteResult = AbsoluteResult,
			NegateResult = NegateResult,
		} );
	}
}
