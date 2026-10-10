namespace Rinten;

/// <summary>What a graph node produces, as far as anything watching it is concerned.</summary>
public enum AnimGraphNodeValueType
{
	Unknown = 0,
	Bool = 1,
	ID = 2,
	Float = 3,
	Vector = 4,
	Target = 5,
	Pose = 6,
	BoneMask = 7,
}

/// <summary>What one node of a running graph did on the frame just gone.</summary>
public record struct AnimGraphNodeDebugInfo
{
	/// <summary>Whether this node has run at all yet.</summary>
	public bool IsInitialized;

	/// <summary>Whether what it produced makes sense - a clip it could find, a bone the model has.</summary>
	public bool IsValid;

	//The interesting one: a graph is a tree that is pulled from, and the part of it that is actually playing is the part that was asked.
	/// <summary>Whether anything asked it for anything this frame.</summary>
	public bool IsActive;

	public AnimGraphNodeValueType ValueType;

	/// <summary>How far through what it plays it is, from zero to one.</summary>
	public float CurrentTime;

	/// <summary>How long that is, in seconds. Zero for anything that is not played.</summary>
	public float Duration;

	/// <summary>How much time it was given this frame, which is not the frame's own.</summary>
	public float DeltaTime;

	/// <summary>Which of the marks on what it plays the playhead is inside, or -1.</summary>
	public int CurrentSyncEvent;

	public float CurrentSyncEventPercentage;

	public int SyncEventCount;
}

/// <summary>One mark on the thing a node is playing, as a fraction of its length.</summary>
public record struct AnimGraphSyncTrackEvent
{
	public StringToken ID;

	/// <summary>Where it starts, from zero to one.</summary>
	public float StartTime;

	/// <summary>How much of the clip it covers.</summary>
	public float Duration;
}

//One struct for every kind rather than one per kind: it is read once a frame per node by an editor that is drawing them all, and a shape that is the same whatever the node is what that editor wants.
/// <summary>The last thing a value node worked out, whichever kind of value it is.</summary>
public record struct AnimGraphNodeDebugValue
{
	public bool Bool;
	public float Float;
	public Vector3 Vector;

	public StringToken ID;

	public bool IsTargetSet;
	public bool IsBoneTarget;

	public StringToken TargetBoneID;
	public Transform TargetTransform;
}

/// <summary>Why a recorded update happened.</summary>
public enum AnimGraphRecordedUpdateType
{
	/// <summary>Time passing, which is what a frame normally is.</summary>
	TimeStep = 0,

	/// <summary>The game setting something, with no time passing.</summary>
	ParameterChange = 1,

	/// <summary>The graph being started or put back to the beginning.</summary>
	Reset = 2,
}

/// <summary>One update of a graph that was recorded, for stepping back through.</summary>
public record struct AnimGraphRecordedUpdateInfo
{
	public AnimGraphRecordedUpdateType Type;

	public float DeltaTime;

	/// <summary>The mark the recorded update started at, and how far into it.</summary>
	public int StartEvent;
	public float StartPercentage;

	/// <summary>And where it had got to by the end.</summary>
	public int EndEvent;
	public float EndPercentage;
}
