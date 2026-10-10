namespace Rinten;

//What a SceneModel needs from its animator: bones, clip playback and IK, in managed types only
internal interface IAnimatableObjectBackend
{
	//Advance the animation and work out the new pose.
	void Update( float delta );

	//Put every bone back where the model rests.
	void SetBindPose();

	//Finish a pose someone has been changing by hand - work the bones back out into world space and update whatever hangs off them.
	void FinishBoneUpdate();

	//Take another scene model's pose for every bone the two share a name for. Take our bones from another model's by name; returns how many matched.
	int MergeFrom( SceneModel parent );

	//-- bones ------------------------------------------------------------------

	Transform GetParentSpaceBone( int bone );
	void SetParentSpaceBone( int bone, in Transform transform );

	//Every bone's parent-space transform at once. Entries past the model's bone count are left alone.
	void GetParentSpaceBones( Span<Transform> destination );

	Transform GetBoneWorldTransform( int bone );
	Transform GetBoneWorldTransform( string bone );
	void SetBoneWorldTransform( int bone, in Transform transform );

	//Every bone's world transform at once.
	void GetBoneWorldTransforms( Span<Transform> destination );

	//The same as it was before the last update, which is what a bone's velocity is measured against.
	void GetBoneWorldPreviousTransforms( Span<Transform> destination );
	Transform GetBoneWorldPreviousTransform( int bone );

	Transform GetBoneLocalTransform( int bone );
	Transform GetBoneLocalTransform( string bone );

	//The bone nearest a world position, or -1 without bones.
	int GetClosestBoneIndex( Vector3 worldPoint );

	//How long the last update advanced by, so a caller can turn the movement between two poses into a speed.
	float DeltaTime { get; }

	//Bumped when the graph is rebuilt (e.g. model change), so callers know to re-send parameters
	int GraphGeneration { get; }

	//-- bones driven from outside ----------------------------------------------

	//Hold a bone where something else puts it - a physics bone, or one a game object is moving by hand. It survives the pose being rebuilt.
	void SetBoneOverride( int bone, in Transform transform );
	void ClearBoneOverrides();
	bool HasBoneOverrides();

	//-- bounds -----------------------------------------------------------------

	//Animator-provided bounds, or null to use the model's own; we never grow them to fit the pose
	BBox? WorldBounds { get; }
	BBox? LocalBounds { get; }

	//-- attachments ------------------------------------------------------------

	//Where a named attachment is, or null if the model has no such thing.
	Transform? GetAttachment( string name, bool worldSpace );

	//-- playback ---------------------------------------------------------------

	float PlaybackRate { get; set; }

	//What the animation moved the model itself by, since the last update.
	Transform RootMotion { get; }

	//-- graphs: ours is set via a separate property from the legacy one so the two are never mixed --

	//The graph driving this, if ours is.
	AnimGraph Graph { get; set; }

	//The graph as it is running, for something that wants to watch it. Null for anything on a legacy graph, and null for ours
	//until a graph is set. Only the editor asks: watching a graph is what draws it playing, and a game has no use for it.
	Animation.AnimGraphInstance RunningGraph { get; }

	//Tell the graph something. A parameter it does not have is ignored - a game sets what it knows about, and a graph carries what it uses.
	void SetParameter( string name, float value );
	void SetParameter( string name, bool value );
	void SetParameter( string name, int value );
	void SetParameter( string name, Vector3 value );
	void SetParameter( string name, Rotation value );

	//An id parameter, by the name of one of its options.
	bool SetParameter( string name, string option );

	//A target: somewhere in the world for the graph to reach for, or one of the model's own bones to follow.
	void SetParameter( string name, Transform value );
	void SetTargetBone( string name, string bone );

	//Nothing to reach for, which is not the same as the origin.
	void ClearTarget( string name );

	//Put every parameter back to what the graph says it starts at.
	void ResetParameters();

	//And one of them.
	void ResetParameter( string name );

	//An enum-style id parameter as its number, which only the declaring graph knows
	int GetIntParameter( string name );

	//-- sequences -------------------------------------------------------------- Playing one
	//clip by name, which is what a model does when nothing is driving it through a graph.

	string Sequence { get; set; }
	float SequenceCycle { get; set; }
	float SequenceDuration { get; }
	bool SequenceFinished { get; }

	bool SequenceLooping { set; }
	bool SequenceBlending { set; }

	//What this model can be asked to play.
	IReadOnlyList<string> SequenceNames { get; }
}
