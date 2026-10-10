using System.Text.Json.Nodes;

using Rinten.CoreAnimation;

namespace Rinten.Animation;

//Asking the animation what it is doing. Every one of these reads the events the playing clips are in the middle of this frame. Two clips blended together
//both say what they marked, and which one is listened to is decided by weight - a clip blended in at a tenth marked a footstep a tenth as loudly.
internal abstract class EventQueryNode : GraphNode
{
	private readonly int state;
	private readonly bool limitToSource;

	protected EventQueryNode( JsonObject node )
	{
		var query = node?["Query"] as JsonObject;

		state = GraphFields.Integer( query, "SourceState", -1 );
		limitToSource = GraphFields.Flag( query, "LimitToSource" );
	}

	//Whether this query is interested in an event at all. A condition written on a transition asks about the state it is leaving,
	//not about everything the graph is playing - a footstep in an upper body layer is not what a leg transition is waiting for.
	protected bool Wants( GraphContext context, in GraphContext.Sampled marked )
	{
		if ( !limitToSource ) return true;

		var wanted = state >= 0 ? state : context.State;

		return marked.State == wanted;
	}

	//The event this query cares most about, of a kind, optionally by name. Which of two matching events
	//wins is what byProgress decides: the one that is louder, or the one that is furthest through.
	protected bool Best( GraphContext context, GraphContext.SampledKind kind, string id, bool byProgress,
		out GraphContext.Sampled best )
	{
		best = default;

		var found = false;
		var score = float.MinValue;

		foreach ( var marked in context.Events )
		{
			if ( marked.Kind != kind ) continue;
			if ( !Wants( context, marked ) ) continue;

			if ( !string.IsNullOrEmpty( id ) && !string.Equals( marked.Id, id, StringComparison.OrdinalIgnoreCase ) ) continue;

			var candidate = byProgress ? marked.Fraction : marked.Weight;
			if ( found && candidate <= score ) continue;

			found = true;
			score = candidate;
			best = marked;
		}

		return found;
	}

	protected static bool ByProgress( JsonObject node )
		=> GraphFields.Text( node, "Priority" ) != nameof( AnimGraphBuilder.EventPriority.HighestWeight );
}

//True while a foot is in the phase asked about.
internal sealed class FootstepConditionNode : EventQueryNode
{
	private readonly AnimGraphBuilder.FootPhase phase;

	public FootstepConditionNode( JsonObject node ) : base( node )
		=> phase = GraphFields.Enum( node, "Phase", AnimGraphBuilder.FootPhase.LeftFootDown );

	protected override bool OnBool( GraphContext context )
	{
		foreach ( var marked in context.Events )
		{
			if ( marked.Kind != GraphContext.SampledKind.Foot ) continue;
			if ( !Wants( context, marked ) ) continue;

			if ( Matches( phase, marked.Data ) ) return true;
		}

		return false;
	}

	//Whether a marked foot phase answers the one being asked for. "Left phase" is either of the left
	//foot's, which is what a graph means when it asks about a foot rather than about a moment of one.
	internal static bool Matches( AnimGraphBuilder.FootPhase phase, int data )
		=> phase switch
		{
			AnimGraphBuilder.FootPhase.LeftPhase => data is 0 or 1,
			AnimGraphBuilder.FootPhase.RightPhase => data is 2 or 3,
			AnimGraphBuilder.FootPhase.None => false,
			_ => (int)phase == data,
		};
}

//How far through the foot event it is, or -1 when there is none.
internal sealed class FootstepPercentageNode : EventQueryNode
{
	private readonly AnimGraphBuilder.FootPhase phase;
	private readonly bool byProgress;

	public FootstepPercentageNode( JsonObject node ) : base( node )
	{
		phase = GraphFields.Enum( node, "Phase", AnimGraphBuilder.FootPhase.LeftFootDown );
		byProgress = ByProgress( node );
	}

	protected override float OnFloat( GraphContext context )
	{
		var found = false;
		var score = float.MinValue;
		var fraction = -1.0f;

		foreach ( var marked in context.Events )
		{
			if ( marked.Kind != GraphContext.SampledKind.Foot ) continue;
			if ( !Wants( context, marked ) ) continue;
			if ( !FootstepConditionNode.Matches( phase, marked.Data ) ) continue;

			var candidate = byProgress ? marked.Fraction : marked.Weight;
			if ( found && candidate <= score ) continue;

			found = true;
			score = candidate;
			fraction = marked.Fraction;
		}

		//Minus one rather than zero: no foot event at all is a different thing from one that has just begun.
		return fraction;
	}
}

//Which foot, as a name the game can act on.
internal sealed class FootstepEventIdNode : EventQueryNode
{
	private readonly bool byProgress;

	public FootstepEventIdNode( JsonObject node ) : base( node ) => byProgress = ByProgress( node );

	protected override string OnId( GraphContext context )
		=> Best( context, GraphContext.SampledKind.Foot, null, byProgress, out var best ) ? best.Id : "";
}

//The name of whatever the animation has marked right now.
internal sealed class IdEventNode : EventQueryNode
{
	private readonly string fallback;
	private readonly bool byProgress;

	public IdEventNode( JsonObject node ) : base( node )
	{
		fallback = GraphFields.Text( node, "Default" );
		byProgress = ByProgress( node );
	}

	protected override string OnId( GraphContext context )
		=> Best( context, GraphContext.SampledKind.Generic, null, byProgress, out var best ) ? best.Id : fallback;
}

//Whether any - or all - of these are marked right now.
internal sealed class IdEventConditionNode : EventQueryNode
{
	private readonly string[] ids;
	private readonly bool all;

	public IdEventConditionNode( JsonObject node ) : base( node )
	{
		all = GraphFields.Text( node, "Match" ) == nameof( AnimGraphBuilder.EventMatch.And );

		var list = GraphFields.Array( node, "Ids" );
		ids = new string[list?.Count ?? 0];

		for ( var i = 0; i < ids.Length; i++ ) ids[i] = list[i]?.GetValue<string>() ?? "";
	}

	protected override bool OnBool( GraphContext context )
	{
		if ( ids.Length == 0 ) return false;

		foreach ( var id in ids )
		{
			var found = false;

			foreach ( var marked in context.Events )
			{
				if ( !Wants( context, marked ) ) continue;
				if ( !string.Equals( marked.Id, id, StringComparison.OrdinalIgnoreCase ) ) continue;

				found = true;
				break;
			}

			if ( all && !found ) return false;
			if ( !all && found ) return true;
		}

		return all;
	}
}

internal sealed class IdEventPercentageNode : EventQueryNode
{
	private readonly string id;
	private readonly bool byProgress;

	public IdEventPercentageNode( JsonObject node ) : base( node )
	{
		id = GraphFields.Text( node, "Id" );
		byProgress = ByProgress( node );
	}

	protected override float OnFloat( GraphContext context )
		=> Best( context, GraphContext.SampledKind.Generic, id, byProgress, out var best ) ? best.Fraction : -1.0f;
}

//A number the artist drew along the clip, read where it is now.
internal sealed class CurveEventNode : EventQueryNode
{
	private readonly string id;
	private readonly float fallback;
	private readonly int fallbackNode;
	private readonly bool byProgress;

	private GraphNode spare;

	public CurveEventNode( JsonObject node ) : base( node )
	{
		id = GraphFields.Text( node, "Id" );
		fallback = GraphFields.Number( node, "Default" );
		fallbackNode = GraphFields.Reference( node, "DefaultNode" );
		byProgress = ByProgress( node );
	}

	public override void Link( GraphNode[] nodes ) => spare = ValueNodes.Get( nodes, fallbackNode );

	protected override float OnFloat( GraphContext context )
	{
		if ( Best( context, GraphContext.SampledKind.Curve, id, byProgress, out var best ) && best.Clip is not null )
			return best.Clip.EventCurve( best.Index, best.Fraction );

		return spare is null ? fallback : spare.Float( context );
	}
}

//Whether the animation says a transition away is allowed right now. Nothing marked means anything is allowed:
//markup exists to forbid, and a clip nobody wrote on should not stop a transition that would otherwise be taken.
internal sealed class TransitionEventConditionNode : EventQueryNode
{
	private readonly AnimGraphBuilder.TransitionRule rule;
	private readonly string id;

	public TransitionEventConditionNode( JsonObject node ) : base( node )
	{
		rule = GraphFields.Enum( node, "Rule", AnimGraphBuilder.TransitionRule.AnyAllowed );
		id = GraphFields.Text( node, "Id" );
	}

	protected override bool OnBool( GraphContext context )
	{
		var marked = false;

		foreach ( var sampled in context.Events )
		{
			if ( sampled.Kind != GraphContext.SampledKind.Transition ) continue;
			if ( !Wants( context, sampled ) ) continue;

			if ( !string.IsNullOrEmpty( id ) && !string.Equals( sampled.Id, id, StringComparison.OrdinalIgnoreCase ) ) continue;

			marked = true;

			//Blocked wins over everything else that is sampled: one clip saying "not here" is the whole answer.
			if ( sampled.Data == (int)AnimGraphBuilder.TransitionRule.Blocked ) return false;

			if ( sampled.Data == (int)rule ) return true;
		}

		return !marked && rule == AnimGraphBuilder.TransitionRule.AnyAllowed;
	}
}

//Events the graph raised about itself - a state entered, left, arrived in.
internal sealed class GraphEventConditionNode : EventQueryNode
{
	private readonly string[] ids;
	private readonly AnimGraphBuilder.GraphEventType[] types;
	private readonly bool all;

	public GraphEventConditionNode( JsonObject node ) : base( node )
	{
		all = GraphFields.Text( node, "Match" ) == nameof( AnimGraphBuilder.EventMatch.And );

		var list = GraphFields.Array( node, "Events" );
		var count = list?.Count ?? 0;

		ids = new string[count];
		types = new AnimGraphBuilder.GraphEventType[count];

		for ( var i = 0; i < count; i++ )
		{
			if ( list[i] is not JsonObject entry ) continue;

			ids[i] = GraphFields.Text( entry, "Id" );
			types[i] = GraphFields.Enum( entry, "Type", AnimGraphBuilder.GraphEventType.Any );
		}
	}

	protected override bool OnBool( GraphContext context )
	{
		if ( ids.Length == 0 ) return false;

		for ( var i = 0; i < ids.Length; i++ )
		{
			var found = false;

			foreach ( var marked in context.Events )
			{
				if ( marked.Kind != GraphContext.SampledKind.Graph ) continue;
				if ( !Wants( context, marked ) ) continue;
				if ( !string.Equals( marked.Id, ids[i], StringComparison.OrdinalIgnoreCase ) ) continue;

				if ( types[i] != AnimGraphBuilder.GraphEventType.Any && marked.Data != (int)types[i] ) continue;

				found = true;
				break;
			}

			if ( all && !found ) return false;
			if ( !all && found ) return true;
		}

		return all;
	}
}

//-- sync events --------------------------------------------------------------- A sync event marks the same moment across clips - the
//foot landing in a walk and in a run. What lets a transition between them start on the beat rather than wherever the two happened to be.

//Which sync event the state is in, and how far through it.
internal sealed class CurrentSyncEventNode : EventQueryNode
{
	private readonly AnimGraphBuilder.SyncEventInfo info;
	private readonly int state;

	public CurrentSyncEventNode( JsonObject node ) : base( node )
	{
		info = GraphFields.Enum( node, "Info", AnimGraphBuilder.SyncEventInfo.IndexAndPercentage );
		state = GraphFields.Integer( node, "State", -1 );
	}

	protected override float OnFloat( GraphContext context )
	{
		if ( !Sync.Current( context, state, out var marked ) ) return -1.0f;

		return info switch
		{
			AnimGraphBuilder.SyncEventInfo.IndexOnly => marked.Data,
			AnimGraphBuilder.SyncEventInfo.PercentageOnly => marked.Fraction,

			//Both at once, the way a sync point is usually spoken about: the event's number plus how far into it we are.
			_ => marked.Data + marked.Fraction,
		};
	}
}

internal sealed class CurrentSyncEventIdNode : EventQueryNode
{
	private readonly int state;

	public CurrentSyncEventIdNode( JsonObject node ) : base( node )
		=> state = GraphFields.Integer( node, "State", -1 );

	protected override string OnId( GraphContext context )
		=> Sync.Current( context, state, out var marked ) ? marked.Id : "";
}

internal sealed class SyncEventConditionNode : EventQueryNode
{
	private readonly int state, index;
	private readonly AnimGraphBuilder.SyncEventTrigger trigger;

	public SyncEventConditionNode( JsonObject node ) : base( node )
	{
		state = GraphFields.Integer( node, "State", -1 );
		index = GraphFields.Integer( node, "Index" );
		trigger = GraphFields.Enum( node, "Trigger", AnimGraphBuilder.SyncEventTrigger.ExactlyAtEventIndex );
	}

	protected override bool OnBool( GraphContext context )
	{
		if ( !Sync.Current( context, state, out var marked ) ) return false;

		return trigger == AnimGraphBuilder.SyncEventTrigger.GreaterThanEqualToEventIndex
			? marked.Data >= index
			: marked.Data == index;
	}
}

//Finding the sync event a state is currently in.
internal static class Sync
{
	public static bool Current( GraphContext context, int state, out GraphContext.Sampled current )
	{
		current = default;

		var wanted = state >= 0 ? state : context.State;
		var found = false;
		var weight = float.MinValue;

		foreach ( var marked in context.Events )
		{
			if ( marked.Kind != GraphContext.SampledKind.Sync ) continue;
			if ( wanted >= 0 && marked.State != wanted ) continue;

			//The loudest, because two clips blended together can both be in a sync event and the one that matters is the one being seen.
			if ( found && marked.Weight <= weight ) continue;

			found = true;
			weight = marked.Weight;
			current = marked;
		}

		return found;
	}
}
