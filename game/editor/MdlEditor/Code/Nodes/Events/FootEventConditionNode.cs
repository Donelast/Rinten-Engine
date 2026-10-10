using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Tests the sampled foot animation event against the selected foot phase. <br/><br/><b>Use when:</b>&#160;Gating transitions or animation logic from the current foot-contact phase. <br/><b>Important:</b>&#160;<b>Left Phase</b> groups Right Foot Down with Left Foot Passing; <b>Right Phase</b> groups Left Foot Down with Right Foot Passing.</summary>
[Title( "Footstep Condition" ), Group( "Events" ), Icon( "do_not_step" )]
public class FootEventConditionNode : Node
{
	[Property]
	public AnimGraphBuilder.FootPhase PhaseCondition { get; set; } = AnimGraphBuilder.FootPhase.LeftFootDown;

	/// <summary>When used in a transition, limit the search to the source state only. Useful to ignore events from lower layers.</summary>
	[Property]
	public bool LimitSearchToSourceState { get; set; }

	/// <summary>Ignore any events from states that we are transitioning away from.</summary>
	[Property]
	public bool IgnoreInactiveBranchEvents { get; set; }

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler ) => compiler.Builder.FootstepCondition(
		PhaseCondition,
		new AnimGraphBuilder.EventQuerySettings
		{
			SourceState = compiler.SourceState,
			LimitSearchToSourceState = LimitSearchToSourceState,
			IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
		} );
}
