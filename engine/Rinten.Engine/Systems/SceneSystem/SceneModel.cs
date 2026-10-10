using NativeEngine;
using System.Runtime.InteropServices;

namespace Rinten;

/// <summary>A model scene object that supports animations and can be rendered within a <see cref="SceneWorld"/>.</summary>
[Expose]
public sealed partial class SceneModel : SceneObject
{
	internal SceneModel() { }
	internal SceneModel( HandleCreationData _ ) { }

	public SceneModel( SceneWorld sceneWorld, string model, Transform transform ) : this( sceneWorld, Model.Load( model ), transform ) { }

	public SceneModel( SceneWorld sceneWorld, Model model, Transform transform )
	{
		Assert.IsValid( sceneWorld );
		Assert.NotNull( model );

		if ( !model.HasRenderMeshes() ) model = Model.Error;

		//Nothing native. A scene model is a model, where it stands, and whatever is posing it - and the posing is our own core's, through the backend below.
		Model = model;
		Transform = transform;

		JoinWorld( sceneWorld );

		RefreshAnimation();
	}

	//Whoever is animating this, chosen per scene object rather than once for the process. Our core can only pose a model in our own
	//format with bones in it; anything else gets a backend that leaves it unposed.
	internal IAnimatableObjectBackend Animation
	{
		get
		{
			//Made on demand as well as at construction: a scene object can also arrive the other way round, made by
			//native code and handed a managed wrapper afterwards, and that path never runs a constructor of ours.
			if ( animation is null ) RefreshAnimation();

			return animation;
		}
	}

	private IAnimatableObjectBackend animation;

	private void RefreshAnimation()
	{
		//What was playing, so the one being made can carry on with it. A model changing lands here, and a character
		//changes model the moment it draws a weapon - the graph it was running is not something to lose there.
		var was = animation;

		if ( animation is CoreAnimatableObject existing ) existing.Destroy();

			var core = new CoreAnimatableObject( this );

			if ( core.CanPose )
			{
				animation = core;
				core.TakeOver( was );

				return;
			}

			core.Destroy();

		//A model our core cannot pose - one in the legacy compiled format, or one with no bones - is not animated at all;
		//a backend that quietly does nothing is the honest answer until the model is converted.
		animation = new UnposedObject( Model?.ResourceName ?? Model?.Name );
	}

	internal override void OnNativeDestroy()
	{
		//The pose is ours and nothing else will free it - the native object going away is exactly when it stops being needed.
		if ( animation is CoreAnimatableObject core ) core.Destroy();
		animation = null;

		base.OnNativeDestroy();
	}

	//The only graph a scene model has.
	/// <summary>The animation graph driving this, in our own format.</summary>
	public AnimGraph AnimGraph
	{
		get => Animation.Graph;
		set => Animation.Graph = value;
	}

	public float PlaybackRate
	{
		get => Animation.PlaybackRate;
		set => Animation.PlaybackRate = value;
	}

	internal override void OnModelChanged()
	{
		base.OnModelChanged();

		//A different model is a different skeleton, and possibly a different engine posing it.
		RefreshAnimation();
	}

	/// <summary>Sets the world space bone transform of a bone by its index.</summary>
	public void SetBoneWorldTransform( int boneIndex, Transform transform )
	{
		OOBChecks.ThrowIfBoneOutOfBounds( boneIndex, Model?.BoneCount ?? 0, nameof( boneIndex ) );

		Animation.SetBoneWorldTransform( boneIndex, transform );
	}

	/// <summary>Returns the world space transform of a bone by its index.</summary>
	public Transform GetBoneWorldTransform( int boneIndex )
	{
		//TODO: Throw on index OOB
		//TODO: Returns nullable to match GetAttachment()?
		return Animation.GetBoneWorldTransform( boneIndex );
	}

	//Fill dest with this frame's world space bone transforms in a single interop call, indexed by bone index. Entries
	//past the model's bone count are left as identity. Much cheaper than calling GetBoneWorldTransform in a loop.
	internal void GetBoneWorldTransforms( Span<Transform> dest )
	{
		Animation.GetBoneWorldTransforms( dest );
	}

	//The index of the bone closest to a world position, or -1 without bones.
	//Resolved natively in a single interop call - no bone transforms are copied out.
	internal int GetClosestBoneIndex( Vector3 worldPoint )
	{
		return Animation.GetClosestBoneIndex( worldPoint );
	}

	//Fill dest with the previous frame's world space bone transforms in a single interop
	//call - the counterpart to GetBoneWorldTransforms, for computing per-bone motion.
	internal void GetBoneWorldPreviousTransforms( Span<Transform> dest )
	{
		Animation.GetBoneWorldPreviousTransforms( dest );
	}

	//Fill dest with the parent-space bone transforms in a single interop call, indexed by bone index. Entries
	//past the model's bone count are left as identity. Much cheaper than calling GetParentSpaceBone in a loop.
	internal void GetParentSpaceBones( Span<Transform> dest )
	{
		Animation.GetParentSpaceBones( dest );
	}

	/// <summary>Returns the world space transform of a bone by its name.</summary>
	public Transform GetBoneWorldTransform( string boneName )
	{
		//TODO: Returns nullable to match GetAttachment()?
		return Animation.GetBoneWorldTransform( boneName );
	}

	/// <summary>Returns the local space transform of a bone by its index.</summary>
	public Transform GetBoneLocalTransform( int boneIndex )
	{
		//TODO: Throw on index OOB
		//TODO: Returns nullable to match GetAttachment()?
		return Animation.GetBoneLocalTransform( boneIndex );
	}

	/// <summary>Returns the local space transform of a bone by its name.</summary>
	public Transform GetBoneLocalTransform( string boneName )
	{
		//TODO: Returns nullable to match GetAttachment()?
		return Animation.GetBoneLocalTransform( boneName );
	}

	internal Transform GetWorldSpaceAnimationTransform( int boneIndex )
	{
		return Animation.GetBoneWorldTransform( boneIndex );
	}

	//Set material group to replace materials of the model as set up in ModelDoc.
	public new void SetMaterialGroup( string name ) //TODO - REMOVE ME
	{
		base.SetMaterialGroup( name );
	}

	/// <summary>Set which body group to use.</summary>
	public void SetBodyGroup( string name, int value )
	{
		//Which bits the chosen part claims, out of the model's own groups. The mask is the whole of what
		//a body group is to the renderer: a mesh is drawn when it carries any bit the mask has set.
		if ( !Model.IsValid() ) return;

		MeshGroupMask = Model.Parts.WithChoice( Model.Parts.Resolve( MeshGroupMask ), name, value );
	}

	/// <summary>Get attachment transform by name.</summary>
	public Transform? GetAttachment( string name, bool worldspace = true )
	{
		return Animation.GetAttachment( name, worldspace );
	}

	/// <summary>Allows the scene model to not use the anim graph so it can play sequences directly</summary>
	public bool UseAnimGraph { get; set; } = true;

	/// <summary>Get the calculated motion from animgraph since last frame</summary>
	public Transform RootMotion
	{
		get => Animation.RootMotion;
	}

	SceneObjectAnimationSequence _currentSequence;

	//The skin palette, reused between frames; copied into frame memory as soon as it is written - see CoreSceneRenderer.PoseBones.
	internal System.Numerics.Matrix4x4[] PoseBuffer;

	/// <summary>Allows playback of sequences directly, rather than using an animation graph. Requires <see cref="UseAnimGraph"/> disabled if the scene model has one.</summary>
	public AnimationSequence CurrentSequence
	{
		get
		{
			//Create on first access
			_currentSequence ??= new SceneObjectAnimationSequence( this );

			return _currentSequence;
		}
	}

	SceneObjectMorphCollection _morphs;

	/// <summary>Access this sceneobject's morph collection. Morphs are generally used in the model to control the face, for things like emotions and lip sync.</summary>
	public MorphCollection Morphs
	{
		get
		{
			//Create on first access
			_morphs ??= new SceneObjectMorphCollection( this );

			return _morphs;
		}
	}

	SceneObjectDirectPlayback _directPlayback;

	/// <summary>Access this sceneobject's direct playback. Direct playback is used to control the direct playback node in an animgraph to play sequences directly in code</summary>
	public AnimGraphDirectPlayback DirectPlayback
	{
		get
		{
			//Create on first access
			_directPlayback ??= new SceneObjectDirectPlayback( this );

			return _directPlayback;
		}
	}
}
