using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Compares the source state's elapsed seconds, normalized state progress, or progress through its current sync event against a fixed or driven value. <br/><br/><b>Use when:</b>&#160;Gating a transition by playback progress or elapsed time. <br/><b>Important:</b>&#160;Only valid inside a conduit transition graph; elsewhere it returns false.</summary>
[Title( "Time Condition" ), Group( "State Machine" ), Icon( "schedule" )]
[AvailableIn( GraphKind.TransitionConduit )]
public class TimeConditionNode : Node, ITransitionScopedNode
{
	[Property]
	public AnimGraphBuilder.StateMachineBuilder.TimeComparisonType Type { get; set; } = AnimGraphBuilder.StateMachineBuilder.TimeComparisonType.ElapsedTime;

	[Property]
	public AnimGraphBuilder.StateMachineBuilder.TimeComparison Operator { get; set; } = AnimGraphBuilder.StateMachineBuilder.TimeComparison.GreaterThanEqual;

	[Property]
	public float Value { get; set; } = 1.0f;

	/// <summary>Optional float overriding the compare value.</summary>
	[Input( typeof( FloatLink ) )]
	public NodeInput ValueInput { get; set; }

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		if ( compiler.SourceState is null )
		{
			compiler.Error( "Time Condition is only usable in a conduit's transition graph" );
			return compiler.ConstBool( false );
		}

		return compiler.Builder.TimeCondition(
			compiler.SourceState,
			new AnimGraphBuilder.StateMachineBuilder.TimeConditionSettings
			{
				Type = Type,
				Comparison = Operator,
				Value = Value,
				InputValue = compiler.EmitValue( this, ValueInput ),
			} );
	}
}
