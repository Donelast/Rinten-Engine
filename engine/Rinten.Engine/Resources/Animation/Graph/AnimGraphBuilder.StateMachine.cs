using System.Text.Json.Nodes;

namespace Rinten;

public sealed partial class AnimGraphBuilder
{
	//Built rather than passed in one go, because a state machine is written a state at a time and a transition needs both of its ends to already exist.
	/// <summary>A set of states, of which one is current, and the transitions between them.</summary>
	public sealed class StateMachineBuilder
	{
		/// <summary>One state, as a handle. What a transition is drawn between and what an event query is asked about.</summary>
		public sealed class State : IEquatable<State>
		{
			private readonly int index;

			internal State( int index )
			{
				this.index = index;
			}

			/// <summary>Whether this is a state at all. Nothing is a null handle, which is what a machine being drawn is full of.</summary>
			public bool IsValid => index >= 0;

			/// <summary>Where the state's pose sits in the definition.</summary>
			public int GetRuntimeIndex() => index;

			public bool Equals( State other ) => other is not null && index == other.index;
			public override bool Equals( object obj ) => Equals( obj as State );
			public override int GetHashCode() => index;

			public static bool operator ==( State a, State b ) => a is null ? b is null : a.Equals( b );
			public static bool operator !=( State a, State b ) => !( a == b );
		}

		[Expose]
		public enum TimeComparison
		{
			LessThan = 0,
			LessThanEqual = 1,
			GreaterThan = 2,
			GreaterThanEqual = 3,
		}

		[Expose]
		public enum TimeComparisonType
		{
			PercentageThroughState = 0,
			PercentageThroughSyncEvent = 1,
			ElapsedTime = 2,

			/// <summary>The same as <see cref="ElapsedTime"/>.</summary>
			CurrentTime = 2,
		}

		/// <summary>Which way round a timed state event is: fired once past a time, or once before it.</summary>
		[Expose]
		public enum TimedEventComparison
		{
			LessThanEqual = 0,
			GreaterThanEqual = 1,
		}

		public sealed class StateSettings
		{
			/// <summary>A state that poses nothing - somewhere for the machine to be while whatever it belongs to is not playing.</summary>
			public bool IsOffState { get; set; }

			public IReadOnlyList<string> EntryEvents { get; set; }
			public IReadOnlyList<string> FullyInStateEvents { get; set; }
			public IReadOnlyList<string> ExitEvents { get; set; }

			/// <summary>Fired against how long the state has been running.</summary>
			public IReadOnlyList<(string Id, float Time, TimedEventComparison Operator)> ElapsedEvents { get; set; }

			/// <summary>And against how long it has left.</summary>
			public IReadOnlyList<(string Id, float Time, TimedEventComparison Operator)> RemainingEvents { get; set; }

			/// <summary>What this state says its layer should weigh, for a machine used as a layer. The state decides, not the layer.</summary>
			public Node LayerWeight { get; set; }
			public Node LayerRootMotionWeight { get; set; }
			public Node LayerBoneMask { get; set; }
		}

		public sealed class TransitionRouteSettings
		{
			/// <summary>When this route may be taken at all.</summary>
			public Node Condition { get; set; }

			/// <summary>Whether it may cut a transition already running short.</summary>
			public bool CanBeForced { get; set; }
		}

		public sealed class TimeConditionSettings
		{
			public TimeComparisonType Type { get; set; }
			public TimeComparison Comparison { get; set; }

			public float Value { get; set; }
			public Node InputValue { get; set; }
		}

		private readonly AnimGraphBuilder owner;
		private readonly Node node;
		private readonly JsonObject fields;

		internal StateMachineBuilder( AnimGraphBuilder owner, Node node, JsonObject fields )
		{
			this.owner = owner;
			this.node = node;
			this.fields = fields;
		}

		public Node Node => node;

		public static implicit operator Node( StateMachineBuilder builder ) => builder.node;

		private JsonArray States => fields["States"].AsArray();

		public State AddState( Node pose, StateSettings settings = null )
		{
			settings ??= new StateSettings();

			var state = new JsonObject
			{
				["Pose"] = Ref( pose ),
				["Off"] = settings.IsOffState,
				["Entry"] = Text( settings.EntryEvents ),
				["FullyIn"] = Text( settings.FullyInStateEvents ),
				["Exit"] = Text( settings.ExitEvents ),
				["Elapsed"] = TimedEvents( settings.ElapsedEvents ),
				["Remaining"] = TimedEvents( settings.RemainingEvents ),
				["LayerWeight"] = Ref( settings.LayerWeight ),
				["LayerRootMotionWeight"] = Ref( settings.LayerRootMotionWeight ),
				["LayerBoneMask"] = Ref( settings.LayerBoneMask ),
				["EntryCondition"] = -1,
				["Routes"] = new JsonArray(),
			};

			States.Add( state );

			return new State( States.Count - 1 );
		}

		public State AddState( Node pose, IEnumerable<string> entryEvents,
			IEnumerable<string> fullyInStateEvents, IEnumerable<string> exitEvents )
			=> AddState( pose, new StateSettings
			{
				EntryEvents = entryEvents?.ToList(),
				FullyInStateEvents = fullyInStateEvents?.ToList(),
				ExitEvents = exitEvents?.ToList(),
			} );

		public State AddOffState( IEnumerable<string> entryEvents = null,
			IEnumerable<string> fullyInStateEvents = null, IEnumerable<string> exitEvents = null )
			=> AddState( default, new StateSettings
			{
				IsOffState = true,
				EntryEvents = entryEvents?.ToList(),
				FullyInStateEvents = fullyInStateEvents?.ToList(),
				ExitEvents = exitEvents?.ToList(),
			} );

		/// <summary>How to get from one state to another: which transition to play, and when it is allowed.</summary>
		public void AddTransition( State from, State to, Node transition, TransitionRouteSettings settings = null )
		{
			settings ??= new TransitionRouteSettings();

			if ( from is not { IsValid: true } || to is not { IsValid: true } ) return;

			States[from.GetRuntimeIndex()].AsObject()["Routes"].AsArray().Add( new JsonObject
			{
				["To"] = to.GetRuntimeIndex(),
				["Transition"] = Ref( transition ),
				["Condition"] = Ref( settings.Condition ),
				["CanBeForced"] = settings.CanBeForced,
			} );
		}

		/// <summary>The transition itself - how the blend into a state is done, as opposed to when it happens.</summary>
		public Node CreateTransition( State target, TransitionSettings settings = null )
		{
			settings ??= new TransitionSettings();

			return owner.Add( NodeKind.Pose, "Transition", new JsonObject
			{
				["Machine"] = node.GetRuntimeIndex(),
				["Target"] = target is { IsValid: true } ? target.GetRuntimeIndex() : -1,
				["Duration"] = settings.Duration,
				["DurationOverride"] = Ref( settings.DurationOverride ),
				["TimeOffset"] = settings.TimeOffset,
				["TimeOffsetOverride"] = Ref( settings.TimeOffsetOverride ),
				["TimeMode"] = settings.TimeMode.ToString(),
				["SyncMatch"] = (int)settings.SyncMatch,
				["PreferClosestSyncId"] = settings.PreferClosestSyncEventId,
				["MatchTimeInSeconds"] = settings.MatchTimeInSeconds,
				["OffsetTimeInSeconds"] = settings.OffsetTimeInSeconds,
				["ClampDuration"] = settings.ClampDuration,
				["TargetSyncId"] = Ref( settings.TargetSyncId ),
				["StartBoneMask"] = Ref( settings.StartBoneMask ),
				["BoneMaskBlendPercentage"] = settings.BoneMaskBlendPercentage,
				["Easing"] = settings.BlendEasing.ToString(),
				["RootMotion"] = settings.RootMotionMode.ToString(),
				["CanBeForced"] = settings.CanBeForced,
			} );
		}

		/// <summary>When the machine may start in this state, for machines with more than one way in.</summary>
		public void SetEntryCondition( State state, Node condition )
		{
			if ( state is not { IsValid: true } ) return;

			States[state.GetRuntimeIndex()].AsObject()["EntryCondition"] = Ref( condition );
		}

		//The first one unless this says otherwise: an author draws the states in the order they think of them, and the one they start in is rarely the first of those.
		/// <summary>Which state the machine starts in.</summary>
		public void SetDefaultState( State state )
		{
			if ( state is not { IsValid: true } ) return;

			fields["Default"] = state.GetRuntimeIndex();
		}

		/// <summary>The machine, finished, as a node to connect to something.</summary>
		public Node Build() => node;

		/// <summary>Whether a state has played out - allowing for the transition that would follow it, so the next one starts on time rather than after.</summary>
		public Node Completed( State state, float transitionDuration = 0.0f )
			=> owner.StateCompletedCondition( state, transitionDuration );

		private static JsonArray TimedEvents( IReadOnlyList<(string Id, float Time, TimedEventComparison Operator)> events )
		{
			var array = new JsonArray();
			if ( events is null ) return array;

			foreach ( var (id, time, comparison) in events )
			{
				array.Add( new JsonObject
				{
					["Id"] = id ?? "",
					["Time"] = time,
					["Comparison"] = comparison.ToString(),
				} );
			}

			return array;
		}
	}

	/// <summary>A state machine, empty and waiting for states.</summary>
	public StateMachineBuilder StateMachine()
	{
		var fields = new JsonObject { ["States"] = new JsonArray() };
		var node = Add( NodeKind.Pose, "StateMachine", fields );

		return new StateMachineBuilder( this, node, fields );
	}
}
