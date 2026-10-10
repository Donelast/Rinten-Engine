using System;
using System.Collections.Generic;

namespace Rinten;

//Animator for models our core cannot pose; answers as a still object so callers need no null check
internal sealed class UnposedObject : IAnimatableObjectBackend
{
	private static readonly Logger log = new( "Animation" );

	private readonly string model;

	//Whether anybody has been told this one answers to nothing.
	private bool said;

	public UnposedObject( string model = null ) => this.model = model;

	//Logs once per object that a parameter was dropped, since it looks like a graph ignoring it
	private void Dropped( string name )
	{
		if ( said ) return;

		said = true;

		log.Info( $"{model ?? "a model"} has no skeleton to pose, so animation parameters like '{name}' do nothing on it" );
	}

	public void Update( float delta ) { }
	public void SetBindPose() { }
	public void FinishBoneUpdate() { }
	public int MergeFrom( SceneModel parent ) => 0;

	public Transform GetParentSpaceBone( int bone ) => Transform.Zero;
	public void SetParentSpaceBone( int bone, in Transform transform ) { }
	public void GetParentSpaceBones( Span<Transform> destination ) { }

	public Transform GetBoneWorldTransform( int bone ) => Transform.Zero;
	public Transform GetBoneWorldTransform( string bone ) => Transform.Zero;
	public void SetBoneWorldTransform( int bone, in Transform transform ) { }
	public void GetBoneWorldTransforms( Span<Transform> destination ) { }

	public void GetBoneWorldPreviousTransforms( Span<Transform> destination ) { }
	public Transform GetBoneWorldPreviousTransform( int bone ) => Transform.Zero;

	public Transform GetBoneLocalTransform( int bone ) => Transform.Zero;
	public Transform GetBoneLocalTransform( string bone ) => Transform.Zero;

	public int GetClosestBoneIndex( Vector3 worldPoint ) => -1;

	public int GraphGeneration => 0;

	public float DeltaTime => 0.0f;

	public void SetBoneOverride( int bone, in Transform transform ) { }
	public void ClearBoneOverrides() { }
	public bool HasBoneOverrides() => false;

	public BBox? WorldBounds => null;
	public BBox? LocalBounds => null;

	public Transform? GetAttachment( string name, bool worldSpace ) => null;

	public float PlaybackRate { get; set; } = 1.0f;
	public Transform RootMotion => Transform.Zero;

	public AnimGraph Graph { get; set; }
	public Animation.AnimGraphInstance RunningGraph => null;

	public void SetParameter( string name, float value ) => Dropped( name );
	public void SetParameter( string name, bool value ) => Dropped( name );
	public void SetParameter( string name, int value ) => Dropped( name );
	public void SetParameter( string name, Vector3 value ) => Dropped( name );
	public void SetParameter( string name, Rotation value ) => Dropped( name );
	public bool SetParameter( string name, string option )
	{
		Dropped( name );
		return false;
	}
	public void SetParameter( string name, Transform value ) => Dropped( name );
	public void SetTargetBone( string name, string bone ) { }
	public void ClearTarget( string name ) { }
	public void ResetParameters() { }
	public void ResetParameter( string name ) { }

	public int GetIntParameter( string name ) => 0;

	public string Sequence { get; set; }
	public float SequenceCycle { get; set; }
	public float SequenceDuration => 0.0f;
	public bool SequenceFinished => true;

	public bool SequenceLooping { set { } }
	public bool SequenceBlending { set { } }

	public IReadOnlyList<string> SequenceNames { get; } = Array.Empty<string>();
}
