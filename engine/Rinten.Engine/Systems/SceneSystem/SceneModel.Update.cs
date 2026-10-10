namespace Rinten;

public sealed partial class SceneModel
{
	/// <summary>Update this animation. Delta is the time you want to advance, usually RealTime.Delta</summary>
	public void Update( float delta )
	{
		Animation.Update( delta );
		FinishBoneUpdate();
	}

	//Update this animation. Delta is the time you want to advance, usually RealTime.Delta
	internal void Update( float delta, Action updateBones )
	{
		Animation.Update( delta );
		updateBones.Invoke();
		FinishBoneUpdate();
	}

	/// <summary>Update all of the bones to the bind pose</summary>
	public void UpdateToBindPose()
	{
		Animation.SetBindPose();
		FinishBoneUpdate();
	}

	//Update all of the bones to the bind pose
	internal void UpdateToBindPose( Action updateBones )
	{
		Animation.SetBindPose();
		updateBones.Invoke();
		FinishBoneUpdate();
	}

	//Updates attachments, ao proxies etc Should be called any time the world transform change
	internal void FinishBoneUpdate()
	{
		if ( Parent.IsValid() )
			return;

		Animation.FinishBoneUpdate();
	}

	/// <summary>Update our bones to match the target's bones. This is a manual bone merge.</summary>
	public int MergeBones( SceneModel parent )
	{
		Assert.IsValid( parent );
		Assert.False( parent == this );

		return Animation.MergeFrom( parent );
	}

	/// <summary>Returns the parent space transform of a bone by its index.</summary>
	public Transform GetParentSpaceBone( int i ) => Animation.GetParentSpaceBone( i );

	internal void SetParentSpaceBone( int i, in Transform tx ) => Animation.SetParentSpaceBone( i, tx );
}
