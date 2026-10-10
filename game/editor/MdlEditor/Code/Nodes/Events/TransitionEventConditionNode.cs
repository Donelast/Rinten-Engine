using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Tests sampled transition events' allow/block permission, optionally by marker ID</summary>
[Title( "Transition Event Condition" ), Group( "Events" ), Icon( "alt_route" )]
public class TransitionEventConditionNode : Node
{
	[Property]
	public AnimGraphBuilder.TransitionRule RuleCondition { get; set; } = AnimGraphBuilder.TransitionRule.AnyAllowed;

	[Property]
	public bool MatchOnlySpecificMarkerId { get; set; }

	[Property, ShowIf( nameof( MatchOnlySpecificMarkerId ), true )]
	public string MarkerIdToMatch { get; set; } = "";

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

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		if ( MatchOnlySpecificMarkerId && string.IsNullOrWhiteSpace( MarkerIdToMatch ) )
		{
			compiler.Warn( "Transition Event Condition is set to match a specific marker ID but none is set" );
			return compiler.ConstBool( false );
		}

		return compiler.Builder.TransitionEventCondition(
			RuleCondition,
			MatchOnlySpecificMarkerId ? MarkerIdToMatch : null,
			new AnimGraphBuilder.EventQuerySettings
			{
				SourceState = compiler.SourceState,
				LimitSearchToSourceState = LimitSearchToSourceState,
				IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
			} );
	}
}
