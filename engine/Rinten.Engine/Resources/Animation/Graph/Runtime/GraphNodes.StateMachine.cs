using System.Text.Json.Nodes;

namespace Rinten.Animation;

//What a transition does when taken: blend duration and sync; a node so it can have its own inputs
internal sealed class TransitionNode : GraphNode
{
	private readonly int durationOverride, offsetOverride, mask;
	private readonly float offset;

	private GraphNode duration, timeOffset, startMask;

	public TransitionNode( JsonObject node )
	{
		TargetState = GraphFields.Integer( node, "Target", -1 );
		BlendTime = GraphFields.Number( node, "Duration" );
		Easing = GraphFields.Enum( node, "Easing", AnimGraphBuilder.Easing.Linear );
		CanBeForced = GraphFields.Flag( node, "CanBeForced" );

		durationOverride = GraphFields.Reference( node, "DurationOverride" );
		mask = GraphFields.Reference( node, "StartBoneMask" );

		offset = GraphFields.Number( node, "TimeOffset" );
		offsetOverride = GraphFields.Reference( node, "TimeOffsetOverride" );

		TimeMode = GraphFields.Enum( node, "TimeMode", AnimGraphBuilder.TransitionTimeMode.None );
		SyncMatch = (AnimGraphBuilder.TransitionSyncMatch)GraphFields.Integer( node, "SyncMatch" );

		PreferClosestSyncId = GraphFields.Flag( node, "PreferClosestSyncId" );
		MatchTimeInSeconds = GraphFields.Flag( node, "MatchTimeInSeconds" );
		OffsetTimeInSeconds = GraphFields.Flag( node, "OffsetTimeInSeconds" );
		ClampDuration = GraphFields.Flag( node, "ClampDuration" );

		MaskBlendPercentage = GraphFields.Number( node, "BoneMaskBlendPercentage" );
		RootMotion = GraphFields.Enum( node, "RootMotion", AnimGraphBuilder.RootMotionBlend.Blend );
	}

	/// <summary>Whether the state being entered is put where the one being left is, and whether it is kept there while the blend runs.</summary>
	public AnimGraphBuilder.TransitionTimeMode TimeMode { get; }

	/// <summary>What to line the two up by, when marks are used rather than the clock.</summary>
	public AnimGraphBuilder.TransitionSyncMatch SyncMatch { get; }

	public bool PreferClosestSyncId { get; }

	/// <summary>Match time in seconds rather than as a fraction of the clip</summary>
	public bool MatchTimeInSeconds { get; }

	public bool OffsetTimeInSeconds { get; }

	/// <summary>Whether the blend is cut short so it finishes with the state it is leaving, rather than running past the end of it.</summary>
	public bool ClampDuration { get; }

	/// <summary>How much of the blend the starting mask lasts for.</summary>
	public float MaskBlendPercentage { get; }

	public AnimGraphBuilder.RootMotionBlend RootMotion { get; }

	/// <summary>Which state this goes to.</summary>
	public int TargetState { get; }

	/// <summary>How long the blend into it takes. Named for what it is rather than Duration, which on a node already means how long what it plays lasts.</summary>
	public float BlendTime { get; }

	public AnimGraphBuilder.Easing Easing { get; }

	/// <summary>Whether another transition may cut this one short.</summary>
	public bool CanBeForced { get; }

	public override void Link( GraphNode[] nodes )
	{
		duration = ValueNodes.Get( nodes, durationOverride );
		timeOffset = ValueNodes.Get( nodes, offsetOverride );
		startMask = ValueNodes.Get( nodes, mask );
	}

	/// <summary>How long this transition should take, this time.</summary>
	public float DurationFor( GraphContext context )
		=> duration is null ? BlendTime : MathF.Max( duration.Float( context ), 0.0f );

	/// <summary>How far past where it was lined up the entered state starts.</summary>
	public float OffsetFor( GraphContext context )
		=> timeOffset is null ? offset : timeOffset.Float( context );

	public int MaskFor( GraphContext context ) => startMask?.Mask( context ) ?? -1;
}

//A set of states, one of which is current, and the transitions between them.
internal sealed class StateMachineNode : PoseNode
{
	private sealed class Route
	{
		public int To;
		public int Transition;
		public int Condition;

		public TransitionNode TransitionNode;
		public GraphNode ConditionNode;
	}

	private sealed class State
	{
		public int Pose;
		public int EntryCondition;

		/// <summary>Whether this state poses nothing. The editor calls such a state Off, and what authors use one for is somewhere to hang the transitions that apply wherever the machine happens to be - which is why the one in every graph here is named "Any".</summary>
		public bool Any;

		public GraphNode PoseNode;
		public GraphNode EntryConditionNode;

		public Route[] Routes = Array.Empty<Route>();

		/// <summary>What this state announces: on the way in, once it is fully arrived, on the way out, and at times measured from either end.</summary>
		public string[] Entry = Array.Empty<string>();
		public string[] FullyIn = Array.Empty<string>();
		public string[] Exit = Array.Empty<string>();

		public Timed[] Elapsed = Array.Empty<Timed>();
		public Timed[] Remaining = Array.Empty<Timed>();

		/// <summary>What this state says its layer should weigh, for a machine that is being used as a layer. The state decides rather than the layer, which is what lets one state be an upper body and the next be nothing at all.</summary>
		public int LayerWeight = -1;
		public int LayerRootMotionWeight = -1;
		public int LayerMask = -1;

		public GraphNode LayerWeightNode, LayerRootMotionNode, LayerMaskNode;
	}

	//An event a state announces once a time has passed, or before it has.
	private readonly record struct Timed( string Id, float Time, bool AtLeast );

	private readonly State[] states;

	private int current = -1;

	//Where the transition running now came from, or -1.
	private int previous = -1;

	private TransitionNode transition;
	private float transitionElapsed, transitionDuration;

	//How far through the blend into the current state is, from zero to one. Worked out once a frame rather
	//than where it is used, because what a layer weighs is asked for outside the pose that worked it out.
	private float blend = 1.0f;

	//How long the machine has been in the state it is in.
	private float elapsed;

	//What happened this frame, for the events a state announces.
	private bool entered, arrived;
	private int left = -1;

	//Which state the machine starts in, when nothing else says.
	private readonly int fallbackState;

	public StateMachineNode( JsonObject node )
	{
		fallbackState = GraphFields.Integer( node, "Default", 0 );

		var list = GraphFields.Array( node, "States" );
		states = new State[list?.Count ?? 0];

		for ( var i = 0; i < states.Length; i++ )
		{
			var entry = list[i] as JsonObject;

			var state = new State
			{
				Pose = GraphFields.Reference( entry, "Pose" ),
				EntryCondition = GraphFields.Reference( entry, "EntryCondition" ),

				Any = GraphFields.Flag( entry, "Off" ),

				Entry = Names( entry, "Entry" ),
				FullyIn = Names( entry, "FullyIn" ),
				Exit = Names( entry, "Exit" ),

				Elapsed = TimedEvents( entry, "Elapsed" ),
				Remaining = TimedEvents( entry, "Remaining" ),

				LayerWeight = GraphFields.Reference( entry, "LayerWeight" ),
				LayerRootMotionWeight = GraphFields.Reference( entry, "LayerRootMotionWeight" ),
				LayerMask = GraphFields.Reference( entry, "LayerBoneMask" ),
			};

			var routes = GraphFields.Array( entry, "Routes" );
			state.Routes = new Route[routes?.Count ?? 0];

			for ( var r = 0; r < state.Routes.Length; r++ )
			{
				var route = routes[r] as JsonObject;

				state.Routes[r] = new Route
				{
					To = GraphFields.Integer( route, "To", -1 ),
					Transition = GraphFields.Reference( route, "Transition" ),
					Condition = GraphFields.Reference( route, "Condition" ),
				};
			}

			states[i] = state;
		}
	}

	private static string[] Names( JsonObject node, string field )
	{
		var list = GraphFields.Array( node, field );
		var names = new string[list?.Count ?? 0];

		for ( var i = 0; i < names.Length; i++ ) names[i] = list[i]?.GetValue<string>() ?? "";

		return names;
	}

	private static Timed[] TimedEvents( JsonObject node, string field )
	{
		var list = GraphFields.Array( node, field );
		var events = new Timed[list?.Count ?? 0];

		for ( var i = 0; i < events.Length; i++ )
		{
			if ( list[i] is not JsonObject entry ) continue;

			events[i] = new Timed(
				GraphFields.Text( entry, "Id" ),
				GraphFields.Number( entry, "Time" ),
				GraphFields.Text( entry, "Comparison" ) != "LessThanEqual" );
		}

		return events;
	}

	public override void Link( GraphNode[] nodes )
	{
		foreach ( var state in states )
		{
			state.PoseNode = ValueNodes.Get( nodes, state.Pose );
			state.EntryConditionNode = ValueNodes.Get( nodes, state.EntryCondition );

			state.LayerWeightNode = ValueNodes.Get( nodes, state.LayerWeight );
			state.LayerRootMotionNode = ValueNodes.Get( nodes, state.LayerRootMotionWeight );
			state.LayerMaskNode = ValueNodes.Get( nodes, state.LayerMask );

			foreach ( var route in state.Routes )
			{
				route.TransitionNode = ValueNodes.Get( nodes, route.Transition ) as TransitionNode;
				route.ConditionNode = ValueNodes.Get( nodes, route.Condition );
			}
		}
	}

	public override void Reset( GraphContext context )
	{
		current = -1;
		previous = -1;
		transition = null;

		elapsed = 0.0f;
		Untick();
	}

	/// <summary>How long the machine has been in its current state.</summary>
	public float Elapsed => elapsed;

	/// <summary>How far through its current state it is, if that has a length.</summary>
	public float ProgressOf( GraphContext context, int state )
	{
		if ( state < 0 || state >= states.Length ) return 0.0f;

		return states[state].PoseNode?.Progress( context ) ?? 0.0f;
	}

	public float DurationOf( GraphContext context, int state )
	{
		if ( state < 0 || state >= states.Length ) return 0.0f;

		return states[state].PoseNode?.Duration( context ) ?? 0.0f;
	}

	/// <summary>Which state the machine is in - what a condition asks about.</summary>
	public int Current => current;

	protected override void OnTick( GraphContext context )
	{
		if ( states.Length == 0 ) return;

		entered = false;
		arrived = false;
		left = -1;

		if ( current < 0 )
		{
			current = Entry( context );
			elapsed = 0.0f;
			entered = true;

			states[current].PoseNode?.Reset( context );
		}

		elapsed += context.Delta;

		//A transition already running goes first: it either finishes, or it keeps the machine where it is unless something is allowed to cut in.
		if ( transition is not null )
		{
			transitionElapsed += context.Delta;

			if ( transitionElapsed >= transitionDuration )
			{
				transition = null;
				previous = -1;

				//Arrived: what a state announces once nothing is blending into it any more.
				arrived = true;
			}
		}

		Consider( context );

		blend = transition is null || transitionDuration <= 0.0f
			? 1.0f
			: Curves.Apply( transition.Easing, Math.Clamp( transitionElapsed / transitionDuration, 0.0f, 1.0f ) );
	}

	//Which state to start in: the first whose entry condition says it may, or the first there is.
	private int Entry( GraphContext context )
	{
		for ( var i = 0; i < states.Length; i++ )
		{
			if ( states[i].EntryConditionNode is null ) continue;

			var was = context.Machine;
			context.Machine = this;

			try
			{
				if ( states[i].EntryConditionNode.Bool( context ) ) return i;
			}
			finally
			{
				context.Machine = was;
			}
		}

		return fallbackState >= 0 && fallbackState < states.Length ? fallbackState : 0;
	}

	//Take the first route out of the current state whose condition is true. In the order they were written, because a
	//state where two conditions are true at once has to pick one and the first is the only answer an author can predict.
	private void Consider( GraphContext context )
	{
		if ( current < 0 || current >= states.Length ) return;

		//Mid-transition, only a transition that says it may force its way in gets to interrupt.
		var interrupting = transition is not null;

		if ( Take( context, states[current], interrupting, false ) ) return;

		//Then routes from Any states, which are tried from every state
		for ( var i = 0; i < states.Length; i++ )
		{
			if ( !states[i].Any || i == current ) continue;

			if ( Take( context, states[i], interrupting, true ) ) return;
		}
	}

	//The first route out of state whose condition is true, taken. False if none of them were.
	private bool Take( GraphContext context, State state, bool interrupting, bool fromAnywhere )
	{
		foreach ( var route in state.Routes )
		{
			if ( route.To < 0 || route.To >= states.Length ) continue;
			if ( interrupting && !( route.TransitionNode?.CanBeForced ?? false ) ) continue;

			//An Any route never sends the machine into the state it is already in. Its condition is usually still true while what it
			//started is playing, and re-entering restarts that from the beginning - a state that can never finish, one frame at a time.
			if ( fromAnywhere && route.To == current ) continue;

			var allowed = true;

			if ( route.ConditionNode is not null )
			{
				var was = context.Machine;
				context.Machine = this;

				try
				{
					allowed = route.ConditionNode.Bool( context );
				}
				finally
				{
					context.Machine = was;
				}
			}

			if ( !allowed ) continue;

			Enter( context, route );
			return true;
		}

		return false;
	}

	private void Enter( GraphContext context, Route route )
	{
		previous = current;
		current = route.To;

		left = previous;
		entered = true;

		transition = route.TransitionNode;
		transitionElapsed = 0.0f;
		transitionDuration = transition?.DurationFor( context ) ?? 0.0f;

		elapsed = 0.0f;

		//The state being entered starts again - a clip in it plays from the beginning rather than from wherever it was left last time.
		states[current].PoseNode?.Reset( context );

		LineUp( context );

		//A blend that would outlast what it is blending out of finishes with it instead: the
		//alternative is a second of blending from a clip that stopped playing half way through.
		if ( transition is not null && transition.ClampDuration && previous >= 0 && previous < states.Length )
		{
			var length = DurationOf( context, previous );

			if ( length > 0.0f )
			{
				var remaining = length * ( 1.0f - ProgressOf( context, previous ) );
				transitionDuration = MathF.Min( transitionDuration, remaining );
			}
		}

		if ( transitionDuration <= 0.0f )
		{
			transition = null;
			previous = -1;

			//Nothing to blend through means it has arrived already.
			arrived = true;
		}
	}

	//Syncs the entered state's phase to the one being left, minus the step it is about to take
	private void LineUp( GraphContext context )
	{
		if ( transition is null ) return;
		if ( previous < 0 || previous >= states.Length ) return;

		var from = states[previous].PoseNode;
		var to = states[current].PoseNode;

		if ( from is null || to is null ) return;

		//By what the artist marked first, if the transition asks for it: two clips are in step when the same foot is down, whatever the clock says.
		var matched = transition.SyncMatch != AnimGraphBuilder.TransitionSyncMatch.None
			&& from.CurrentSync( context, out var point )
			&& to.SeekToSync( context, point, transition.SyncMatch, transition.PreferClosestSyncId );

		if ( !matched && transition.TimeMode != AnimGraphBuilder.TransitionTimeMode.None )
		{
			var progress = from.Progress( context );

			var at = transition.MatchTimeInSeconds
				? progress * from.Duration( context )
				: progress * to.Duration( context );

			to.Seek( context, at - context.Delta );
		}

		var offset = transition.OffsetFor( context );
		if ( offset == 0.0f ) return;

		var seconds = transition.OffsetTimeInSeconds ? offset : offset * to.Duration( context );

		to.Seek( context, ( to.Progress( context ) * to.Duration( context ) ) + seconds );
	}

	//What the graph's reset count was when this last looked.
	private int signals = -1;

	protected override int OnPose( GraphContext context )
	{
		//A reset from below sends the machine back to its start state (used to detect parameter changes)
		var count = context.Graph?.ResetSignals ?? 0;

		if ( signals != count )
		{
			var first = signals < 0;
			signals = count;

			if ( !first )
			{
				current = Entry( context );
				previous = -1;
				transition = null;
				elapsed = 0.0f;
				blend = 1.0f;

				if ( current >= 0 && current < states.Length ) states[current].PoseNode?.Reset( context );
			}
		}

		Tick( context );

		if ( current < 0 || current >= states.Length ) return context.RestPose();

		Announce( context );

		var alpha = blend;

		//Which state is being played and what is blending into it, for anything drawing the machine: it is the whole of what a state machine is doing.
		context.Debug?.Machine( this, current,
			transition is null ? -1 : previous,
			transition?.Index ?? -1,
			alpha );

		if ( transition is null || previous < 0 || previous >= states.Length ) return PoseOf( context, current, 1.0f );

		//The state being left goes first, so a transition that keeps the two in step has this frame's position to line up against rather than the last one's.
		var from = PoseOf( context, previous, 1.0f - alpha );

		if ( transition.TimeMode == AnimGraphBuilder.TransitionTimeMode.Synchronized ) LineUp( context );

		var into = PoseOf( context, current, alpha );

		//The mask a transition starts under, if it has one: an upper body that changes while the legs keep walking. It is let go
		//of over the part of the blend the transition says, so the rest of the body catches up rather than never moving at all.
		var mask = transition.MaskFor( context );

		if ( mask >= 0 && transition.MaskBlendPercentage > 0.0f )
		{
			var through = Math.Clamp( alpha / transition.MaskBlendPercentage, 0.0f, 1.0f );

			var full = context.AllocMask();

			if ( full >= 0 )
			{
				context.FillMask( full, 1.0f );
				context.BlendMask( mask, mask, full, through );
				context.FreeMask( full );
			}
		}

		var blended = context.Blend( from, into, alpha, mask );

		if ( mask >= 0 ) context.FreeMask( mask );

		return blended;
	}

	//-- being a layer ---------------------------------------------------------- A machine used as a layer is weighed by the state it is
	//in rather than by the layer it sits in: one state is an upper body, the next is nothing at all, and the layer itself says neither.

	/// <summary>What the state the machine is in says its layer should weigh.</summary>
	public float LayerWeight( GraphContext context ) => LayerValue( context, false );

	/// <summary>And how much of the movement in it the character should take.</summary>
	public float LayerRootMotionWeight( GraphContext context ) => LayerValue( context, true );

	private float LayerValue( GraphContext context, bool rootMotion )
	{
		if ( current < 0 || current >= states.Length ) return 1.0f;

		var into = WeightOf( context, current, rootMotion );

		if ( transition is null || previous < 0 || previous >= states.Length ) return into;

		//Blended across the transition, so a layer that is being left fades out with the state that was saying how much it weighed.
		return MathX.Lerp( WeightOf( context, previous, rootMotion ), into, blend );
	}

	private float WeightOf( GraphContext context, int state, bool rootMotion )
	{
		var node = rootMotion ? states[state].LayerRootMotionNode : states[state].LayerWeightNode;

		//Nothing connected is fully on, which is what a state that says nothing about being a layer means.
		return node is null ? 1.0f : Math.Clamp( node.Float( context ), 0.0f, 1.0f );
	}

	/// <summary>The mask the state the machine is in wants its layer under.</summary>
	public int LayerMask( GraphContext context )
	{
		if ( current < 0 || current >= states.Length ) return -1;

		var mask = states[current].LayerMaskNode?.Mask( context ) ?? -1;

		if ( mask >= 0 || transition is null || previous < 0 || previous >= states.Length ) return mask;

		//While a state with no mask of its own is being blended into, the one being left still has a
		//say - otherwise a masked layer goes full body for exactly as long as the transition lasts.
		return states[previous].LayerMaskNode?.Mask( context ) ?? -1;
	}

	//A state's pose, evaluated inside that state's own scope. The scope is what lets a condition ask about "this state" - an event marked on
	//the clip of the state being left is told apart from the same event on the one being entered by where it was sampled, not by its name.
	private int PoseOf( GraphContext context, int state, float weight )
	{
		var scope = context.PushState( this, state );
		var was = context.PushWeight( weight );

		try
		{
			return states[state].PoseNode?.Pose( context ) ?? context.RestPose();
		}
		finally
		{
			context.PopWeight( was );
			context.PopState( scope );
		}
	}

	//What the current state has to say this frame: that it has been entered, arrived in, or left, and whatever it announces at a time.
	private void Announce( GraphContext context )
	{
		if ( entered ) Raise( context, states[current].Entry, AnimGraphBuilder.GraphEventType.Entry, current );
		if ( arrived ) Raise( context, states[current].FullyIn, AnimGraphBuilder.GraphEventType.FullyInState, current );

		if ( left >= 0 && left < states.Length )
			Raise( context, states[left].Exit, AnimGraphBuilder.GraphEventType.Exit, left );

		foreach ( var timed in states[current].Elapsed )
		{
			if ( string.IsNullOrEmpty( timed.Id ) ) continue;
			if ( timed.AtLeast ? elapsed < timed.Time : elapsed > timed.Time ) continue;

			Raise( context, timed.Id, AnimGraphBuilder.GraphEventType.Timed, current );
		}

		var length = DurationOf( context, current );

		foreach ( var timed in states[current].Remaining )
		{
			if ( string.IsNullOrEmpty( timed.Id ) || length <= 0.0f ) continue;

			var remaining = length * ( 1.0f - ProgressOf( context, current ) );
			if ( timed.AtLeast ? remaining < timed.Time : remaining > timed.Time ) continue;

			Raise( context, timed.Id, AnimGraphBuilder.GraphEventType.Timed, current );
		}
	}

	private void Raise( GraphContext context, string[] ids, AnimGraphBuilder.GraphEventType type, int state )
	{
		foreach ( var id in ids )
		{
			if ( !string.IsNullOrEmpty( id ) ) Raise( context, id, type, state );
		}
	}

	private void Raise( GraphContext context, string id, AnimGraphBuilder.GraphEventType type, int state )
		=> context.Raise( new GraphContext.Sampled( id, GraphContext.SampledKind.Graph, (int)type,
			0.0f, context.Weight, null, -1, this, state ) );

	public override float Duration( GraphContext context ) => DurationOf( context, current );

	public override float Progress( GraphContext context ) => ProgressOf( context, current );

	//A machine is where the state it is in is: a transition into a state that holds another machine lines up whatever that one is playing.
	public override void Seek( GraphContext context, float time )
	{
		if ( current < 0 || current >= states.Length ) return;

		states[current].PoseNode?.Seek( context, time );
	}

	public override bool CurrentSync( GraphContext context, out SyncPoint point )
	{
		point = default;

		if ( current < 0 || current >= states.Length ) return false;

		return states[current].PoseNode?.CurrentSync( context, out point ) ?? false;
	}

	public override bool SeekToSync( GraphContext context, in SyncPoint point,
		AnimGraphBuilder.TransitionSyncMatch match, bool preferClosestId )
	{
		if ( current < 0 || current >= states.Length ) return false;

		return states[current].PoseNode?.SeekToSync( context, point, match, preferClosestId ) ?? false;
	}
}

//Whether a state has played out, allowing for the transition that follows it. The transition's length is subtracted because
//a transition takes time: a state that waits until it is finished before starting one arrives late by exactly that much.
internal sealed class StateCompletedNode : GraphNode
{
	private readonly int state, durationOverride;
	private readonly float duration;

	private GraphNode overrideNode;

	public StateCompletedNode( JsonObject node )
	{
		state = GraphFields.Integer( node, "State", -1 );
		duration = GraphFields.Number( node, "Duration" );
		durationOverride = GraphFields.Reference( node, "DurationOverride" );
	}

	public override void Link( GraphNode[] nodes ) => overrideNode = ValueNodes.Get( nodes, durationOverride );

	protected override bool OnBool( GraphContext context )
	{
		var machine = context.Machine;
		if ( machine is null ) return false;

		var length = machine.DurationOf( context, state );
		if ( length <= 0.0f ) return false;

		var blend = overrideNode is null ? duration : MathF.Max( overrideNode.Float( context ), 0.0f );
		var progress = machine.ProgressOf( context, state );

		return progress * length >= MathF.Max( length - blend, 0.0f );
	}
}

//How long a state has been running, or how far through it is, compared with a number.
internal sealed class TimeConditionNode : GraphNode
{
	private readonly int state, input;
	private readonly float value;
	private readonly AnimGraphBuilder.StateMachineBuilder.TimeComparisonType type;
	private readonly AnimGraphBuilder.StateMachineBuilder.TimeComparison comparison;

	private GraphNode inputNode;

	public TimeConditionNode( JsonObject node )
	{
		state = GraphFields.Integer( node, "State", -1 );
		value = GraphFields.Number( node, "Value" );
		input = GraphFields.Reference( node, "Input" );

		type = GraphFields.Enum( node, "Type",
			AnimGraphBuilder.StateMachineBuilder.TimeComparisonType.ElapsedTime );

		comparison = GraphFields.Enum( node, "Comparison",
			AnimGraphBuilder.StateMachineBuilder.TimeComparison.GreaterThanEqual );
	}

	public override void Link( GraphNode[] nodes ) => inputNode = ValueNodes.Get( nodes, input );

	protected override bool OnBool( GraphContext context )
	{
		var machine = context.Machine;
		if ( machine is null ) return false;

		var against = inputNode is null ? value : inputNode.Float( context );

		var measured = type switch
		{
			AnimGraphBuilder.StateMachineBuilder.TimeComparisonType.PercentageThroughState
				=> machine.ProgressOf( context, state ),

			//Percentage through a sync event needs markup our clips do not carry yet; how far through the state is the nearest true thing.
			AnimGraphBuilder.StateMachineBuilder.TimeComparisonType.PercentageThroughSyncEvent
				=> machine.ProgressOf( context, state ),

			_ => machine.Elapsed,
		};

		return comparison switch
		{
			AnimGraphBuilder.StateMachineBuilder.TimeComparison.LessThan => measured < against,
			AnimGraphBuilder.StateMachineBuilder.TimeComparison.LessThanEqual => measured <= against,
			AnimGraphBuilder.StateMachineBuilder.TimeComparison.GreaterThan => measured > against,
			_ => measured >= against,
		};
	}

	protected override float OnFloat( GraphContext context ) => Bool( context ) ? 1.0f : 0.0f;
}
