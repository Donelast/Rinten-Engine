
namespace Rinten;

/// <summary>A model scene object that supports animations and can be rendered within a <see cref="SceneWorld"/>.</summary>
public sealed partial class SceneModel : SceneObject
{
	/// <summary>Manually override the final bone transform.</summary>
	public void SetBoneOverride( int boneIndex, in Transform transform )
	{
		if ( boneIndex < 0 ) return;

		Animation.SetBoneOverride( boneIndex, transform );
	}

	/// <summary>Clears all bone transform overrides.</summary>
	public void ClearBoneOverrides()
	{
		Animation.ClearBoneOverrides();
	}

	/// <summary>Whether any bone transforms have been overridden.</summary>
	public bool HasBoneOverrides()
	{
		return Animation.HasBoneOverrides();
	}

	/// <summary>Calculates the velocity from the previous and current bone transforms.</summary>
	public void GetBoneVelocity( int boneIndex, out Vector3 linear, out Vector3 angular )
	{
		linear = default;
		angular = default;

		var delta = Animation.DeltaTime;
		if ( delta <= 0.0f )
			return;

		var boneNow = Animation.GetBoneWorldTransform( boneIndex );
		var boneThen = Animation.GetBoneWorldPreviousTransform( boneIndex );

		linear = (boneNow.Position - boneThen.Position) / delta;

		var diff = Rotation.Difference( boneThen.Rotation, boneNow.Rotation );
		angular = new Vector3( diff.x, diff.y, diff.z ) / delta;
	}
}
