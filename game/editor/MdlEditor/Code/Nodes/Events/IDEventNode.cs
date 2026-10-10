using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>ID of the highest-priority sampled ID animation event, or Default Value</summary>
[Title( "ID Event Value" ), Group( "Events" ), Icon( "label" )]
public class IDEventNode : Node
{
	/// <summary>The ID this node returns when nothing matches.</summary>
	[Property]
	public string DefaultValue { get; set; } = "";

	/// <summary>When used in a transition, limit the search to the source state only. Useful to ignore events from lower layers.</summary>
	[Property]
	public bool LimitSearchToSourceState { get; set; }

	[Property]
	public AnimGraphBuilder.EventPriority PriorityRule { get; set; } = AnimGraphBuilder.EventPriority.HighestWeight;

	/// <summary>Ignore any events from states that we are transitioning away from.</summary>
	[Property]
	public bool IgnoreInactiveBranchEvents { get; set; }

	[Output( typeof( IDLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler ) => compiler.Builder.IdEvent(
		DefaultValue,
		PriorityRule,
		new AnimGraphBuilder.EventQuerySettings
		{
			SourceState = compiler.SourceState,
			LimitSearchToSourceState = LimitSearchToSourceState,
			IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
		} );
}
