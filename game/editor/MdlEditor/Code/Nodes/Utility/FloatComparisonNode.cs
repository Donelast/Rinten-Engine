using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Compares <b>Value</b> with the connected Comparand, or with <b>Comparison Value</b> when disconnected. Near-equal uses the configured Epsilon. <br/><br/><b>Use when:</b>&#160;Converting a numeric threshold or approximate equality test into a boolean condition. <br/><b>Important:</b>&#160;A disconnected Value uses 0.</summary>
[Title( "Float Comparison" ), Group( "Values" ), Icon( "compare_arrows" )]
public class FloatComparisonNode : Node
{
	[Input( typeof( FloatLink ) )]
	public NodeInput Value { get; set; }

	/// <summary>Optional float to compare against. When unconnected, Comparison Value is used.</summary>
	[Input( typeof( FloatLink ) )]
	public NodeInput Comparand { get; set; }

	[Property]
	public AnimGraphBuilder.FloatComparison Comparison { get; set; } = AnimGraphBuilder.FloatComparison.GreaterThanEqual;

	/// <summary>Constant to compare against when no comparand is connected.</summary>
	[Property]
	public float ComparisonValue { get; set; }

	/// <summary>Tolerance used by the near-equal comparison.</summary>
	[Property]
	public float Epsilon { get; set; }

	/// <summary>True when the test does <i>not</i> hold rather than when it does.</summary>
	[Property]
	public bool Invert { get; set; }

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var value = compiler.EmitValue( this, Value );
		if ( value is null )
			value = compiler.ConstFloat( 0.0f );

		var comparand = compiler.EmitValue( this, Comparand );

		var held = comparand is not null
			? compiler.Builder.Compare( value, Comparison, comparand, Epsilon )
			: compiler.Builder.Compare( value, Comparison, ComparisonValue, Epsilon );

		return Invert ? compiler.Builder.Not( held ) : held;
	}
}
