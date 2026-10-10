using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>True when the source state is close enough to its end to start the transition</summary>
[Title( "State Completed" ), Group( "State Machine" ), Icon( "task_alt" )]
[AvailableIn( GraphKind.TransitionConduit )]
public class StateCompletedConditionNode : Node, ITransitionScopedNode
{
	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	/// <summary>Fire at the exact end of the clip instead of early by the transition's blend duration.</summary>
	[Property]
	public bool AtClipEnd { get; set; }

	/// <summary>True when the test does <i>not</i> hold rather than when it does.</summary>
	[Property]
	public bool Invert { get; set; }

	public override Color PrimaryColor => Theme.Pink;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		if ( compiler.SourceState is null )
		{
			compiler.Error( "State Completed is only usable in a conduit's transition graph" );
			return compiler.ConstBool( false );
		}

		var done = compiler.Builder.StateCompletedCondition(
			compiler.SourceState,
			AtClipEnd ? 0.0f : compiler.TransitionDuration,
			AtClipEnd ? default : compiler.TransitionDurationOverride );

		return Invert ? compiler.Builder.Not( done ) : done;
	}
}
