using System.Text.Json.Nodes;

namespace Rinten;

/// <summary>Queries on the animation itself (foot phase, clip events, state progress) for reactive graphs</summary>
public sealed partial class AnimGraphBuilder
{
	//Where an event query looks. A condition on a transition asks about the state being left; one in the graph asks about everything playing.
	private JsonObject Query( EventQuerySettings settings )
	{
		settings ??= new EventQuerySettings();

		return new JsonObject
		{
			["SourceState"] = settings.SourceState is { IsValid: true } ? settings.SourceState.GetRuntimeIndex() : -1,
			["LimitToSource"] = settings.LimitSearchToSourceState,
			["IgnoreInactive"] = settings.IgnoreInactiveEvents,
		};
	}

	private Node Event( NodeKind kind, string type, JsonObject fields, EventQuerySettings settings )
	{
		fields["Query"] = Query( settings );
		return Add( kind, type, fields );
	}

	//-- footsteps ------------------------------------------------------------

	/// <summary>True while the animation is in a phase of a footstep.</summary>
	public Node FootstepCondition( FootPhase phase, EventQuerySettings settings = null )
		=> Event( NodeKind.Bool, "FootstepCondition", new JsonObject
		{
			["Phase"] = phase.ToString(),
		}, settings );

	/// <summary>How far through that phase it is, or -1 when it is not in one.</summary>
	public Node FootstepPercentage( FootPhase phase, EventPriority priority = EventPriority.HighestWeight,
		EventQuerySettings settings = null )
		=> Event( NodeKind.Float, "FootstepPercentage", new JsonObject
		{
			["Phase"] = phase.ToString(),
			["Priority"] = priority.ToString(),
		}, settings );

	/// <summary>Which foot, as a name the game can act on.</summary>
	public Node FootstepEventId( EventPriority priority = EventPriority.HighestWeight,
		EventQuerySettings settings = null )
		=> Event( NodeKind.Id, "FootstepEventId", new JsonObject
		{
			["Priority"] = priority.ToString(),
		}, settings );

	//-- marked events --------------------------------------------------------

	/// <summary>The id of whatever the animation has marked at this moment, or a default when it has marked nothing.</summary>
	public Node IdEvent( string fallback = null, EventPriority priority = EventPriority.HighestWeight,
		EventQuerySettings settings = null )
		=> Event( NodeKind.Id, "IdEvent", new JsonObject
		{
			["Default"] = fallback ?? "",
			["Priority"] = priority.ToString(),
		}, settings );

	/// <summary>Whether any - or all - of these ids are marked right now.</summary>
	public Node IdEventCondition( IEnumerable<string> ids, EventMatch match = EventMatch.Or,
		EventSearch search = EventSearch.SearchAll, EventQuerySettings settings = null )
		=> Event( NodeKind.Bool, "IdEventCondition", new JsonObject
		{
			["Ids"] = Text( ids ),
			["Match"] = match.ToString(),
			["Search"] = search.ToString(),
		}, settings );

	/// <summary>How far through a marked event the animation is.</summary>
	public Node IdEventPercentage( string id, EventPriority priority = EventPriority.HighestWeight,
		EventQuerySettings settings = null )
		=> Event( NodeKind.Float, "IdEventPercentage", new JsonObject
		{
			["Id"] = id ?? "",
			["Priority"] = priority.ToString(),
		}, settings );

	/// <summary>A number the artist drew along the clip - a curve marked with an id, read at wherever the clip is now.</summary>
	public Node CurveEvent( CurveEventSettings settings = null )
	{
		settings ??= new CurveEventSettings();

		return Event( NodeKind.Float, "CurveEvent", new JsonObject
		{
			["Id"] = settings.EventId ?? "",
			["Default"] = settings.DefaultValue,
			["DefaultNode"] = Ref( settings.DefaultValueNode ),
			["Priority"] = settings.Priority.ToString(),
		}, new EventQuerySettings
		{
			LimitSearchToSourceState = settings.LimitSearchToSourceState,
			IgnoreInactiveEvents = settings.IgnoreInactiveEvents,
		} );
	}

	/// <summary>Events the graph itself raises - a state being entered, left, or fully arrived in.</summary>
	public Node GraphEventCondition( IEnumerable<(string Id, GraphEventType Type)> events,
		EventMatch match = EventMatch.Or, EventQuerySettings settings = null )
	{
		var array = new JsonArray();

		foreach ( var (id, type) in events ?? Array.Empty<(string, GraphEventType)>() )
		{
			array.Add( new JsonObject { ["Id"] = id ?? "", ["Type"] = type.ToString() } );
		}

		return Event( NodeKind.Bool, "GraphEventCondition", new JsonObject
		{
			["Events"] = array,
			["Match"] = match.ToString(),
		}, settings );
	}

	/// <summary>Whether the animation says a transition out is allowed right now - the markup an artist puts on a clip to keep a transition off the frames where it would look wrong.</summary>
	public Node TransitionEventCondition( TransitionRule rule, string id = null, EventQuerySettings settings = null )
		=> Event( NodeKind.Bool, "TransitionEventCondition", new JsonObject
		{
			["Rule"] = rule.ToString(),
			["Id"] = id ?? "",
		}, settings );

	//-- sync events ---------------------------------------------------------- A sync event is a mark that says "this is the same moment"
	//across clips - the foot hitting the ground in a walk and in a run. What lets two clips be blended without their feet fighting.

	/// <summary>Which sync event is current, and how far through it.</summary>
	public Node CurrentSyncEvent( StateMachineBuilder.State state, SyncEventInfo info = SyncEventInfo.IndexAndPercentage )
		=> Add( NodeKind.Float, "CurrentSyncEvent", new JsonObject
		{
			["State"] = state is { IsValid: true } ? state.GetRuntimeIndex() : -1,
			["Info"] = info.ToString(),
		} );

	/// <summary>What it is called.</summary>
	public Node CurrentSyncEventId( StateMachineBuilder.State state )
		=> Add( NodeKind.Id, "CurrentSyncEventId", new JsonObject
		{
			["State"] = state is { IsValid: true } ? state.GetRuntimeIndex() : -1,
		} );

	/// <summary>True once the animation reaches a numbered sync event.</summary>
	public Node SyncEventCondition( StateMachineBuilder.State state, int index,
		SyncEventTrigger trigger = SyncEventTrigger.ExactlyAtEventIndex )
		=> Add( NodeKind.Bool, "SyncEventCondition", new JsonObject
		{
			["State"] = state is { IsValid: true } ? state.GetRuntimeIndex() : -1,
			["Index"] = index,
			["Trigger"] = trigger.ToString(),
		} );

	//-- states ---------------------------------------------------------------

	//The transition duration is subtracted, because a transition takes time and a state that waits until it is finished before starting one arrives late by exactly that much. The override is for a duration that is itself worked out by the graph.
	/// <summary>Whether a state has played out.</summary>
	public Node StateCompletedCondition( StateMachineBuilder.State state, float transitionDuration = 0.0f,
		Node durationOverride = default )
		=> Add( NodeKind.Bool, "StateCompleted", new JsonObject
		{
			["State"] = state is { IsValid: true } ? state.GetRuntimeIndex() : -1,
			["Duration"] = transitionDuration,
			["DurationOverride"] = Ref( durationOverride ),
		} );

	/// <summary>How long a state has been running, or has left, compared with a number.</summary>
	public Node TimeCondition( StateMachineBuilder.State state, StateMachineBuilder.TimeConditionSettings settings )
	{
		settings ??= new StateMachineBuilder.TimeConditionSettings();

		return Add( NodeKind.Bool, "TimeCondition", new JsonObject
		{
			["State"] = state is { IsValid: true } ? state.GetRuntimeIndex() : -1,
			["Type"] = settings.Type.ToString(),
			["Comparison"] = settings.Comparison.ToString(),
			["Value"] = settings.Value,
			["Input"] = Ref( settings.InputValue ),
		} );
	}
}
