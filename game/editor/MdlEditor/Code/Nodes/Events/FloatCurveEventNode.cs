using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Samples a float-curve animation event, falling back to a default when none match</summary>
[Title( "Float Curve Event" ), Group( "Events" ), Icon( "show_chart" )]
public class FloatCurveEventNode : Node
{
	/// <summary>Optional float overriding the default value.</summary>
	[Input( typeof( FloatLink ) )]
	public NodeInput Default { get; set; }

	/// <summary>Only sample float curve events with this ID. Leave empty to match any.</summary>
	[Property]
	public string MatchEventId { get; set; } = "";

	[Property]
	public float DefaultValue { get; set; }

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

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var defaultValue = compiler.EmitValue( this, Default );
		return compiler.Builder.CurveEvent( new AnimGraphBuilder.CurveEventSettings
		{
			EventId = MatchEventId,
			DefaultValue = DefaultValue,
			DefaultValueNode = defaultValue,
			Priority = PriorityRule,
			LimitSearchToSourceState = LimitSearchToSourceState,
			IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
		} );
	}
}
