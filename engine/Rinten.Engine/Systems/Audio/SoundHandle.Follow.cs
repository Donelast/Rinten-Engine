namespace Rinten;

public partial class SoundHandle
{
	/// <summary>Update our position every frame relative to our parent</summary>
	public bool FollowParent { get; set; }

	/// <summary>If we're following a parent, our position will be this relative to them.</summary>
	public Transform LocalTransform { get; set; }

	/// <summary>Object to follow (offset by LocalTransform); also lets GameObject.StopAllSounds stop this sound</summary>
	public GameObject Parent { get; set; }

	void UpdateFollower()
	{
		if ( !FollowParent ) return;

		if ( !Parent.IsValid() )
		{
			Parent = default;
			FollowParent = false;
			return;
		}

		var parentTx = Parent.WorldTransform;
		Transform = parentTx.ToWorld( LocalTransform );
	}

	/// <summary>Clear our parent - stop following</summary>
	[Obsolete( "Just use Parent property directly" )]
	public void ClearParent()
	{
		Parent = default;
	}

	/// <summary>Tell the SoundHandle to follow this GameObject's position</summary>
	[Obsolete( "Just use Parent property directly" )]
	public void SetParent( GameObject obj )
	{
		Parent = obj;
	}
}

