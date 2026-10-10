namespace Rinten;

partial class SkinnedModelRenderer
{
	//Simulates bones using physics defined on the model.
	internal BonePhysics Physics { get; private set; }

	//Does our model have collision and joints.
	internal bool HasBonePhysics()
	{
		return !Scene.IsEditor && Model.IsValid() && Model.Physics is { Parts.Count: > 0, Joints.Count: > 0 };
	}

	//Give this renderer the bone physics its model asks for, or none if it asks for none. The previous world, if there was one, is thrown away.
	internal void RebuildBonePhysics( SkinnedModelRenderer parent )
	{
		Physics?.Destroy();
		Physics = HasBonePhysics() ? new BonePhysics( this, parent ) : null;
	}

	//Let go of any bone physics, putting the bones back under the animation.
	internal void DestroyBonePhysics()
	{
		Physics?.Destroy();
		Physics = null;
	}
}
