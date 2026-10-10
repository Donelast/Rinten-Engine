using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>True when sampled events contain any (Or) or all (And) of the configured IDs</summary>
[Title( "ID Event Condition" ), Group( "Events" ), Icon( "label_important" )]
public class IDEventConditionNode : Node
{
	[Property]
	public AnimGraphBuilder.EventMatch Operator { get; set; } = AnimGraphBuilder.EventMatch.Or;

	[Property]
	public AnimGraphBuilder.EventSearch SearchRule { get; set; } = AnimGraphBuilder.EventSearch.SearchAll;

	/// <summary>When used in a transition, limit the search to the source state only. Useful to ignore events from lower layers.</summary>
	[Property]
	public bool LimitSearchToSourceState { get; set; }

	/// <summary>Ignore any events from states that we are transitioning away from.</summary>
	[Property]
	public bool IgnoreInactiveBranchEvents { get; set; }

	[Property]
	public List<string> EventIds { get; set; } = [];

	/// <summary>True when the events are <i>not</i> there rather than when they are.</summary>
	[Property]
	public bool Invert { get; set; }

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var ids = EventIds?.Where( id => !string.IsNullOrWhiteSpace( id ) ).ToList() ?? [];
		if ( ids.Count == 0 )
		{
			compiler.Warn( "ID Event Condition has no event IDs" );
			return compiler.ConstBool( false );
		}

		var found = compiler.Builder.IdEventCondition(
			ids,
			Operator,
			SearchRule,
			new AnimGraphBuilder.EventQuerySettings
			{
				SourceState = compiler.SourceState,
				LimitSearchToSourceState = LimitSearchToSourceState,
				IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
			} );

		return Invert ? compiler.Builder.Not( found ) : found;
	}
}
