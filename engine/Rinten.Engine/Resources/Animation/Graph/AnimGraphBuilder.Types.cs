namespace Rinten;

/// <summary>The vocabulary an animation graph is built out of.</summary>
public sealed partial class AnimGraphBuilder
{
	/// <summary>What a node produces. Used to fail fast when one is plugged into something that wants another kind.</summary>
	[Expose]
	public enum NodeKind
	{
		Pose = 0,
		Bool = 1,
		Float = 2,
		Id = 3,
		Vector = 4,
		Target = 5,
		BoneMask = 6,
	}

	[Expose]
	public enum FloatComparison
	{
		GreaterThanEqual = 0,
		LessThanEqual = 1,
		NearEqual = 2,
		GreaterThan = 3,
		LessThan = 4,
	}

	[Expose]
	public enum FloatMathOperation
	{
		Add = 0,
		Subtract = 1,
		Multiply = 2,
		Divide = 3,
		Modulo = 4,
		Abs = 5,
		Negate = 6,
		Floor = 7,
		Ceiling = 8,
		IntegerPart = 9,
		FractionalPart = 10,
		InverseFractionalPart = 11,
	}

	[Expose]
	public enum AngleOperation
	{
		ClampTo180 = 0,
		ClampTo360 = 1,
		FlipHemisphere = 2,
		FlipHemisphereNegate = 3,
	}

	[Expose]
	public enum VectorComponent
	{
		X = 0,
		Y = 1,
		Z = 2,
		Length = 3,
		AngleHorizontal = 4,
		AngleVertical = 5,
	}

	[Expose]
	public enum TargetInfoType
	{
		AngleHorizontal = 0,
		AngleVertical = 1,
		Distance = 2,
		DistanceHorizontalOnly = 3,
		DistanceVerticalOnly = 4,
		DeltaOrientationX = 5,
		DeltaOrientationY = 6,
		DeltaOrientationZ = 7,
	}

	[Expose]
	public enum Easing
	{
		Linear = 0,
		InQuad = 1,
		OutQuad = 2,
		InOutQuad = 3,
		InCubic = 4,
		OutCubic = 5,
		InOutCubic = 6,
		InQuart = 7,
		OutQuart = 8,
		InOutQuart = 9,
		InQuint = 10,
		OutQuint = 11,
		InOutQuint = 12,
		InSine = 13,
		OutSine = 14,
		InOutSine = 15,
		InExpo = 16,
		OutExpo = 17,
		InOutExpo = 18,
		InCirc = 19,
		OutCirc = 20,
		InOutCirc = 21,

		/// <summary>Straight there, no curve at all.</summary>
		None = 22,
	}

	/// <summary>How a layer is put on top of what is under it.</summary>
	[Expose]
	public enum LayerBlendMode
	{
		/// <summary>Replaces, by weight.</summary>
		Overlay = 0,

		/// <summary>Adds its difference from a base pose.</summary>
		Additive = 1,

		/// <summary>Adds its difference the same way, where that difference was authored against the model rather than against each bone's parent.</summary>
		ModelSpace = 2,
	}

	[Expose]
	public enum IKBlendMode
	{
		Effector = 0,
		Pose = 1,
	}

	[Expose]
	public enum RootMotionSamplingMode
	{
		Delta = 0,
		WorldSpace = 1,
	}

	[Expose]
	public enum RootMotionBlend
	{
		Blend = 0,
		Additive = 1,
		IgnoreSource = 2,
		IgnoreTarget = 3,
	}

	[Flags]
	[Expose]
	public enum RootMotionOverrideFlags
	{
		None = 0,
		MoveX = 1,
		MoveY = 2,
		MoveZ = 4,
		FacingPitch = 8,
		ListenForEvents = 16,
	}

	[Expose]
	public enum TargetWarpUpdate
	{
		None = 0,
		Recalculate = 1,
		Offset = 2,
		RecalculateOrOffset = 3,
	}

	[Expose]
	public enum FootPhase
	{
		LeftFootDown = 0,
		LeftFootPassing = 1,
		RightFootDown = 2,
		RightFootPassing = 3,
		LeftPhase = 4,
		RightPhase = 5,
		None = 6,
	}

	[Expose]
	public enum EventPriority
	{
		HighestWeight = 0,
		HighestPercentageThrough = 1,

		/// <summary>The same as <see cref="HighestPercentageThrough"/>.</summary>
		HighestProgress = 1,
	}

	[Expose]
	public enum EventMatch
	{
		Or = 0,
		And = 1,

		/// <summary>The same as <see cref="Or"/>.</summary>
		Any = 0,

		/// <summary>The same as <see cref="And"/>.</summary>
		All = 1,
	}

	[Expose]
	public enum EventSearch
	{
		SearchAll = 0,
		OnlySearchGraphEvents = 1,
		OnlySearchAnimEvents = 2,

		/// <summary>The same as <see cref="SearchAll"/>.</summary>
		All = 0,

		/// <summary>The same as <see cref="OnlySearchGraphEvents"/>.</summary>
		GraphEventsOnly = 1,

		/// <summary>The same as <see cref="OnlySearchAnimEvents"/>.</summary>
		AnimEventsOnly = 2,
	}

	[Expose]
	public enum GraphEventType
	{
		Entry = 0,
		FullyInState = 1,
		Exit = 2,
		Timed = 3,
		Generic = 4,
		Any = 5,
	}

	[Expose]
	public enum SyncEventInfo
	{
		IndexAndPercentage = 0,
		IndexOnly = 1,
		PercentageOnly = 2,
	}

	[Expose]
	public enum SyncEventTrigger
	{
		ExactlyAtEventIndex = 0,
		GreaterThanEqualToEventIndex = 1,

		/// <summary>The same as <see cref="ExactlyAtEventIndex"/>.</summary>
		ExactIndex = 0,

		/// <summary>The same as <see cref="GreaterThanEqualToEventIndex"/>.</summary>
		AtOrAfterIndex = 1,
	}

	[Expose]
	public enum TransitionRule
	{
		AnyAllowed = 0,
		FullyAllowed = 1,
		ConditionallyAllowed = 2,
		Blocked = 3,
	}

	[Expose]
	public enum TransitionTimeMode
	{
		None = 0,
		Synchronized = 1,
		MatchSourceTime = 2,
	}

	[Flags]
	[Expose]
	public enum TransitionSyncMatch
	{
		None = 0,
		EventIndex = 1,
		EventId = 2,
		EventPercentage = 4,
	}

	//-- settings ------------------------------------------------------------ Classes rather than structs, so a caller can leave
	//one out and get the defaults below rather than a block of zeroes - a clip at zero speed being the kind of default nobody means.

	public sealed class ControlParameterSettings
	{
		/// <summary>Whether the parameter is part of the graph's public face - the list a game sets by name. An implicit one, made because a node asked for a parameter nobody declared, is not.</summary>
		public bool Describe { get; set; }

		public float Min { get; set; }
		public float Max { get; set; }

		/// <summary>What it stands at before anything sets it, and what it goes back to when it resets. A number for the numeric kinds, and for an id the position in <see cref="Options"/> of the name it starts as.</summary>
		public float Default { get; set; }

		/// <summary>Whether the parameter goes back to its default once the graph has read it.</summary>
		public bool AutoReset { get; set; }

		public IReadOnlyList<string> Options { get; set; }
	}

	public sealed class ClipSettings
	{
		public bool Looping { get; set; }
		public float Speed { get; set; } = 1.0f;

		public Node PlayInReverse { get; set; }
		public Node ResetTime { get; set; }

		public int SyncEventOffset { get; set; }
		public bool SampleRootMotion { get; set; }

		public IEnumerable<string> GraphEvents { get; set; }
	}

	public sealed class TimeControlledClipSettings
	{
		public Node PlayInReverse { get; set; }
		public bool SampleRootMotion { get; set; }

		public IEnumerable<string> GraphEvents { get; set; }
	}

	public sealed class PoseSettings
	{
		public Node Time { get; set; }

		public float RemapRangeStart { get; set; }
		public float RemapRangeEnd { get; set; } = 1.0f;

		public float FixedTime { get; set; }
		public bool UseFramesAsInput { get; set; }
	}

	public sealed class BlendSpace2DSettings
	{
		public IReadOnlyList<Node> Sources { get; set; }
		public IReadOnlyList<Vector2> Positions { get; set; }

		/// <summary>Triangles over the positions, three indices each, and the outline around them. Worked out once when the graph is compiled rather than every frame it plays.</summary>
		public IReadOnlyList<int> Indices { get; set; }
		public IReadOnlyList<int> HullIndices { get; set; }

		public bool Looping { get; set; }

		/// <summary>Whether the poses it blends are put into one stride rather than each keeping its own time. What a locomotion space needs and a space of unrelated poses does not.</summary>
		public bool Synchronized { get; set; }
	}

	public sealed class BoneMaskSelectionSettings
	{
		public bool SwitchDynamically { get; set; }
		public float BlendTime { get; set; }
	}

	public sealed class LookAtSettings
	{
		/// <summary>How much of the turn to do, from nothing to all of it.</summary>
		public Node Weight { get; set; }

		/// <summary>The attachment that does the pointing - the eyes, a weapon's hold point, a marker on the head.</summary>
		public string Attachment { get; set; } = "";

		/// <summary>How far round it may go, in degrees. Past that it stops following rather than wrapping the neck round.</summary>
		public float Limit { get; set; } = 180.0f;

		//A neck and a spine are not limited alike in both directions - a spine turns a long way round and hardly leans at all - so the two are held apart. Left at a half turn, only Limit applies.
		/// <summary>How far it may turn to either side, in degrees, on its own rather than as part of one cone.</summary>
		public float YawLimit { get; set; } = 180.0f;

		/// <summary>How far it may tip up or down, in degrees.</summary>
		public float PitchLimit { get; set; } = 180.0f;
	}

	public sealed class LayerSettings
	{
		public Node Weight { get; set; }
		public Node BoneMask { get; set; }

		public LayerBlendMode BlendMode { get; set; }

		public bool Synchronized { get; set; }
		public bool IgnoreEvents { get; set; }

		/// <summary>Whether the layer only says which way bones point, leaving their offsets from their parents alone - which is what their graph means by blending in model space, rotation only.</summary>
		public bool RotationOnly { get; set; }

		/// <summary>Whether a difference is laid on a channel at a time - offsets added, turns composed - rather than composed as one transform.</summary>
		public bool ChannelsApart { get; set; }

		/// <summary>Whether the difference was authored against the model rather than each parent.</summary>
		public bool ModelSpace { get; set; }

		public Node RootMotionWeight { get; set; }
	}

	public sealed class EaseSettings
	{
		public float Time { get; set; }
		public Easing Easing { get; set; }

		public bool UseStartValue { get; set; }
		public float StartValue { get; set; }
	}

	public sealed class SpringSettings
	{
		public float Hertz { get; set; } = 10.0f;
		public float DampingRatio { get; set; } = 1.0f;

		public bool UseStartValue { get; set; }
		public float StartValue { get; set; }
	}

	public sealed class FloatMathSettings
	{
		public Node B { get; set; }
		public float ValueB { get; set; }

		public bool AbsoluteResult { get; set; }
		public bool NegateResult { get; set; }
	}

	public sealed class IdSwitchSettings
	{
		public Node WhenTrue { get; set; }
		public string TrueValue { get; set; }

		public Node WhenFalse { get; set; }
		public string FalseValue { get; set; }
	}

	public sealed class IdBasedSelectorSettings
	{
		public Node Fallback { get; set; }
		public bool IgnoreInvalidOptions { get; set; }

		/// <summary>Whether the options have to be clips rather than any pose, which is what lets one be swapped without restarting the time it is at.</summary>
		public bool ClipsOnly { get; set; }
	}

	public sealed class ParameterizedSelectorSettings
	{
		public IReadOnlyList<int> Weights { get; set; }

		public bool IgnoreInvalidOptions { get; set; }
		public bool HasWeights { get; set; }
		public bool ClipsOnly { get; set; }
	}

	public sealed class TargetSelectorSettings
	{
		public float PositionWeight { get; set; } = 1.0f;
		public float OrientationWeight { get; set; }

		public bool WorldSpace { get; set; }
		public bool IgnoreInvalidOptions { get; set; }
	}

	public sealed class IkSettings
	{
		/// <summary>The limb the model names, which says which bones turn and which way they fold. Empty leaves the effector bone to say, and its two parents are the limb.</summary>
		public string Chain { get; set; } = "";

		public Node Enabled { get; set; }
		public float BlendTime { get; set; }

		public bool WorldSpace { get; set; }
		public IKBlendMode BlendMode { get; set; }

		public float TwistWeight { get; set; }
	}

	public sealed class FootIKSettings
	{
		/// <summary>The limbs the model names, one foot each. Empty leaves the foot bones to say.</summary>
		public string LeftChain { get; set; } = "";

		public string RightChain { get; set; } = "";

		public Node LeftTarget { get; set; }
		public Node RightTarget { get; set; }
		public Node Enabled { get; set; }

		public float BlendTime { get; set; }
		public IKBlendMode BlendMode { get; set; }

		public bool WorldSpace { get; set; }
	}

	public sealed class RootMotionOverrideSettings
	{
		public Node MovingVelocity { get; set; }
		public Node FacingDirection { get; set; }

		public Node LinearVelocityLimit { get; set; }
		public Node AngularVelocityLimit { get; set; }

		public float MaxLinearVelocity { get; set; }
		public float MaxAngularVelocityDegrees { get; set; }

		public RootMotionOverrideFlags Flags { get; set; }
	}

	public sealed class TargetWarpSettings
	{
		public TargetWarpUpdate UpdateRule { get; set; }
		public RootMotionSamplingMode SamplingMode { get; set; }

		public bool AlignAtLastWarpEvent { get; set; }
		public string AlignmentBone { get; set; }

		public float SamplingPositionErrorThreshold { get; set; }
		public float MaxTangentLength { get; set; }
		public float LerpFallbackDistance { get; set; }

		public float TargetUpdateDistance { get; set; }
		public float TargetUpdateAngleDegrees { get; set; }
	}

	public sealed class CurveEventSettings
	{
		public string EventId { get; set; }

		public float DefaultValue { get; set; }
		public Node DefaultValueNode { get; set; }

		public EventPriority Priority { get; set; }

		public bool LimitSearchToSourceState { get; set; }
		public bool IgnoreInactiveEvents { get; set; }
	}

	/// <summary>Where an event query is asked from. A condition on a transition asks about the state it is leaving, not about the graph as a whole.</summary>
	public sealed class EventQuerySettings
	{
		public StateMachineBuilder.State SourceState { get; set; }

		public bool LimitSearchToSourceState { get; set; }
		public bool IgnoreInactiveEvents { get; set; }
	}

	public sealed class TransitionSettings
	{
		public float Duration { get; set; }
		public Node DurationOverride { get; set; }

		public float TimeOffset { get; set; }
		public Node TimeOffsetOverride { get; set; }

		public TransitionTimeMode TimeMode { get; set; }
		public TransitionSyncMatch SyncMatch { get; set; }

		public bool PreferClosestSyncEventId { get; set; }
		public bool MatchTimeInSeconds { get; set; }
		public bool OffsetTimeInSeconds { get; set; }
		public bool ClampDuration { get; set; }

		public Node TargetSyncId { get; set; }

		public Node StartBoneMask { get; set; }
		public float BoneMaskBlendPercentage { get; set; }

		public Easing BlendEasing { get; set; }
		public RootMotionBlend RootMotionMode { get; set; }

		/// <summary>Whether another transition may cut this one short. A transition that cannot be forced runs to its end whatever else becomes true.</summary>
		public bool CanBeForced { get; set; }
	}
}
