using System.Linq;
using Rinten.Diagnostics;

namespace Editor.MovieMaker;

#nullable enable

public static class ModelExtensions
{
	public static IEnumerable<BoneCollection.Bone> GetAncestorsAndSelf( this BoneCollection.Bone bone )
	{
		while ( bone is not null )
		{
			yield return bone;
			bone = bone.Parent;
		}
	}

	public static BoneCollection.Bone GetRoot( this BoneCollection.Bone bone, TrackView bonesTrackView )
	{
		if ( bone.Parent is null ) return bone;

		return bone.GetAncestorsAndSelf()
			.Skip( 1 )
			.First( x => x.Parent is null || x.IsLocked( bonesTrackView ) );
	}

	/// <summary>Is this bone locked in place when trying to solve IK?</summary>
	public static bool IsLocked( this BoneCollection.Bone bone, TrackView bonesTrackView )
	{
		Assert.AreEqual( "Bones", bonesTrackView.Name );

		return bonesTrackView.GetCookie( $"{bone.Name}.{nameof( IsLocked )}", false );
	}

	/// <summary>Set whether this bone is locked in place when trying to solve IK.</summary>
	public static void SetLocked( this BoneCollection.Bone bone, TrackView bonesTrackView, bool value )
	{
		Assert.AreEqual( "Bones", bonesTrackView.Name );

		//We set the cookie on the Bones track instead of the individual bone's track because that bone might not have a track yet.

		bonesTrackView.SetCookie( $"{bone.Name}.{nameof( IsLocked )}", value );
	}
}
