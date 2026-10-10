using System;
using Rinten.CoreAnimation;

namespace EngineTests;

//Look-at solve: which axis aims at the target and how far each chain bone turns
[TestClass]
public class AnimLookAtTest
{
	//root, spine, chest, head - each a quarter metre above the last.
	private static CoreAnimRuntime.Skeleton Spine( params Rotation[] rests )
	{
		var names = new[] { "root", "spine", "chest", "head" };

		var skeleton = new CoreAnimRuntime.Skeleton
		{
			Bones = new CoreAnimRuntime.Skeleton.BoneEntry[names.Length],
			Order = new[] { 0, 1, 2, 3 },
		};

		for ( var bone = 0; bone < names.Length; bone++ )
		{
			skeleton.Bones[bone] = new CoreAnimRuntime.Skeleton.BoneEntry
			{
				Name = names[bone],
				Key = names[bone],
				Parent = bone - 1,
				Rest = new AnimTrs
				{
					Position = bone == 0 ? Vector3.Zero : new Vector3( 0, 0, 0.25f ),
					Rotation = bone < rests.Length ? rests[bone] : Rotation.Identity,
					Scale = Vector3.One,
				},
			};

			skeleton.ByName[names[bone]] = bone;
		}

		return skeleton;
	}

	//One standing in it, with the rest pose as its working pose.
	private static CoreAnimRuntime.Instance Standing( CoreAnimRuntime.Skeleton skeleton )
	{
		var count = skeleton.Bones.Length;

		var instance = new CoreAnimRuntime.Instance
		{
			Skeleton = skeleton,
			Local = new AnimTrs[count],
			Model = new AnimTrs[count],
			Previous = new AnimTrs[count],
			Overridden = new bool[count],
			Overrides = new AnimTrs[count],
		};

		var pose = new AnimTrs[count];

		for ( var bone = 0; bone < count; bone++ )
		{
			instance.Local[bone] = skeleton.Bones[bone].Rest;
			pose[bone] = skeleton.Bones[bone].Rest;
		}

		instance.Poses.Add( pose );
		instance.PoseUsed.Add( true );

		return instance;
	}

	//Which way a bone's own axis points once the whole pose is composed.
	private static Vector3 Points( CoreAnimRuntime.Instance instance, int bone, Vector3 axis )
		=> (Rotation)CoreAnimRuntime.ModelOf( instance.Pose( 0 ), instance.Skeleton, bone ).Rotation * axis;

	private static float Between( Vector3 a, Vector3 b )
		=> MathF.Acos( Math.Clamp( Vector3.Dot( a.Normal, b.Normal ), -1.0f, 1.0f ) ) * 180.0f / MathF.PI;

	//A look aims the attachment, not the bone; the citizen's head bone points up the skull
	[TestMethod]
	public void ALookPointsTheAttachment()
	{
		//What the citizen's own forward_reference is: a quarter turn round and a quarter turn over,
		//which is what turns a bone that runs up the neck into one that faces the way the character does.
		var marker = Rotation.From( new Angles( 0.0f, 90.0f, 90.0f ) );

		//So the head bone stands with the marker on it facing straight ahead.
		var head = marker.Inverse;

		var skeleton = Spine( Rotation.Identity, Rotation.Identity, Rotation.Identity, head );
		var instance = Standing( skeleton );

		var target = Rotation.From( new Angles( -20.0f, 35.0f, 0.0f ) ).Forward;

		Assert.IsTrue( CoreAnimRuntime.PoseLookAt( instance, 0, new[] { 3 }, new[] { 1.0f },
			target, 1.0f, 0.0f, 180.0f, 180.0f, 3, marker.Forward ) );

		var aimed = Points( instance, 3, marker.Forward );

		Assert.IsTrue( Between( aimed, target ) < 1.0f,
			$"the attachment ended up {Between( aimed, target ):0.0} degrees off what it was aimed at" );
	}

	//Aiming the bone's own forward instead is the quarter turn it looks like. The other half of the test above: not that the attachment is
	//better, but that leaving it out is wrong by the whole angle between the bone and the marker on it - which on a head is ninety degrees of nod.
	[TestMethod]
	public void ALookWithoutOneIsOutByTheBonesOwnTurn()
	{
		var marker = Rotation.From( new Angles( 0.0f, 90.0f, 90.0f ) );
		var head = marker.Inverse;

		var skeleton = Spine( Rotation.Identity, Rotation.Identity, Rotation.Identity, head );
		var instance = Standing( skeleton );

		var target = Vector3.Forward;

		Assert.IsTrue( CoreAnimRuntime.PoseLookAt( instance, 0, new[] { 3 }, new[] { 1.0f },
			target, 1.0f, 0.0f ) );

		var aimed = Points( instance, 3, marker.Forward );

		Assert.IsTrue( Between( aimed, target ) > 45.0f,
			"a head aimed by its own bone happened to end up facing forward, so this proves nothing" );
	}

	//Chain weights are shares of the whole turn, not of what parent bones left
	[TestMethod]
	public void AChainsWeightsAreSharesOfTheWholeTurn()
	{
		var skeleton = Spine();
		var instance = Standing( skeleton );

		var target = Rotation.From( new Angles( 0.0f, 40.0f, 0.0f ) ).Forward;

		//Tip first, which is the order a model writes a chain in.
		Assert.IsTrue( CoreAnimRuntime.PoseLookAt( instance, 0, new[] { 2, 1 }, new[] { 1.0f, 0.5f },
			target, 1.0f, 0.0f ) );

		var chest = Points( instance, 2, Vector3.Forward );
		var spine = Points( instance, 1, Vector3.Forward );

		Assert.AreEqual( 40.0f, chest.EulerAngles.yaw, 0.5f,
			"the bone whose share is all of it has to arrive at the target" );

		Assert.AreEqual( 20.0f, spine.EulerAngles.yaw, 0.5f,
			"the bone under it bends half way, so the turn is spread rather than added" );
	}

	//A rotation-ignoring reference is measured against the model, not its animated bone
	[TestMethod]
	public void AReferenceThatFacesOneWayIsMeasuredAgainstTheModel()
	{
		var skeleton = Spine();
		var instance = Standing( skeleton );

		var head = skeleton.Find( "head" );

		//The head turned a quarter of the way round, the way an animation would have left it.
		instance.Pose( 0 )[head].Rotation = Rotation.FromAxis( Vector3.Up, 45.0f );

		var bones = new[] { skeleton.Find( "spine" ), skeleton.Find( "chest" ), head };
		var weights = new[] { 1.0f, 1.0f, 1.0f };

		var before = new AnimTrs[bones.Length];

		for ( var i = 0; i < bones.Length; i++ ) before[i] = instance.Pose( 0 )[bones[i]];

		//Look where the model faces, by something that faces that way itself.
		CoreAnimRuntime.PoseLookAt( instance, 0, bones, weights, Vector3.Forward, 1.0f, 180.0f,
			180.0f, 180.0f, -1, Vector3.Forward, aimInModel: true );

		for ( var i = 0; i < bones.Length; i++ )
		{
			Assert.AreEqual( 0.0f,
				Between( (Rotation)before[i].Rotation * Vector3.Forward,
					(Rotation)instance.Pose( 0 )[bones[i]].Rotation * Vector3.Forward ),
				0.5f, "nothing has to move to look where the body is already looking" );
		}
	}

	//And one that rides a bone follows that bone, which is the difference.
	[TestMethod]
	public void AReferenceOnABoneFollowsIt()
	{
		var skeleton = Spine();
		var instance = Standing( skeleton );

		var head = skeleton.Find( "head" );

		instance.Pose( 0 )[head].Rotation = Rotation.FromAxis( Vector3.Up, 45.0f );

		var bones = new[] { head };

		var before = instance.Pose( 0 )[head];

		CoreAnimRuntime.PoseLookAt( instance, 0, bones, new[] { 1.0f }, Vector3.Forward, 1.0f, 180.0f,
			180.0f, 180.0f, head, Vector3.Forward );

		Assert.AreEqual( 45.0f,
			Between( (Rotation)before.Rotation * Vector3.Forward,
				(Rotation)instance.Pose( 0 )[head].Rotation * Vector3.Forward ),
			1.0f, "the head was turned away and had to come back to face the front" );
	}
}
