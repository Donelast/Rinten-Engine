using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Percentage through the sampled ID event matching Event ID, or -1 if none</summary>
[Title( "ID Event Percentage Through" ), Group( "Events" ), Icon( "percent" )]
public class IDEventPercentageThroughNode : Node
{
	[Property]
	public string EventId { get; set; } = "";

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

	internal override AnimGraphBuilder.Node Compile( Compiler compiler ) => compiler.Builder.IdEventPercentage(
		EventId,
		PriorityRule,
		new AnimGraphBuilder.EventQuerySettings
		{
			SourceState = compiler.SourceState,
			LimitSearchToSourceState = LimitSearchToSourceState,
			IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
		} );
}
