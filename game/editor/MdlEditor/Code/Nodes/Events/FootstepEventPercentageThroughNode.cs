using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Returns the percentage through a sampled foot event matching the selected phase, or -1 when none match. <br/><br/><b>Use when:</b>&#160;Driving values from progress through a foot-contact phase. <br/><b>Important:</b>&#160;When several events match, the priority rule chooses by greatest weight or greatest percentage through.</summary>
[Title( "Footstep Percentage Through" ), Group( "Events" ), Icon( "percent" )]
public class FootstepEventPercentageThroughNode : Node
{
	[Property]
	public AnimGraphBuilder.FootPhase PhaseCondition { get; set; } = AnimGraphBuilder.FootPhase.LeftFootDown;

	[Property]
	public AnimGraphBuilder.EventPriority PriorityRule { get; set; } = AnimGraphBuilder.EventPriority.HighestWeight;

	/// <summary>When used in a transition, limit the search to the source state only. Useful to ignore events from lower layers.</summary>
	[Property]
	public bool LimitSearchToSourceState { get; set; }

	/// <summary>Ignore any events from states that we are transitioning away from.</summary>
	[Property]
	public bool IgnoreInactiveBranchEvents { get; set; }

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler ) => compiler.Builder.FootstepPercentage(
		PhaseCondition,
		PriorityRule,
		new AnimGraphBuilder.EventQuerySettings
		{
			SourceState = compiler.SourceState,
			LimitSearchToSourceState = LimitSearchToSourceState,
			IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
		} );
}
