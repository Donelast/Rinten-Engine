using System;
using Rinten.CoreAnimation;

namespace EngineTests;

//Constraint-driven bones (e.g. forearm twist) evaluated after the pose is made
[TestClass]
public class AnimConstraintsTest
{
	//A forearm, the twist bone on it, and the hand at the end.
	private static CoreAnimRuntime.Skeleton Arm()
	{
		var names = new[] { "arm_lower", "arm_lower_twist", "hand" };
		var parents = new[] { -1, 0, 0 };

		var skeleton = new CoreAnimRuntime.Skeleton
		{
			Bones = new CoreAnimRuntime.Skeleton.BoneEntry[names.Length],
			Order = new[] { 0, 1, 2 },
		};

		for ( var bone = 0; bone < names.Length; bone++ )
		{
			skeleton.Bones[bone] = new CoreAnimRuntime.Skeleton.BoneEntry
			{
				Name = names[bone],
				Key = names[bone],
				Parent = parents[bone],
				Rest = new AnimTrs
				{
					Position = bone == 0 ? Vector3.Zero : new Vector3( bone * 0.12f, 0, 0 ),
					Rotation = Rotation.Identity,
					Scale = Vector3.One,
				},
			};

			skeleton.ByName[names[bone]] = bone;
		}

		return skeleton;
	}

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

	//How far a bone has turned about its own length.
	private static float Twist( CoreAnimRuntime.Instance instance, int pose, string named )
	{
		var bone = instance.Skeleton.Find( named );

		var rest = AnimTrs.NormaliseQuat( instance.Skeleton.Bones[bone].Rest.Rotation );

		var turned = AnimTrs.NormaliseQuat(
			System.Numerics.Quaternion.Inverse( rest )
			* AnimTrs.NormaliseQuat( instance.Pose( pose )[bone].Rotation ) );

		return AnimTrs.TwistAngle( turned, Vector3.Right ) * 180.0f / MathF.PI;
	}

	//A twist bone takes the share of the wrist its model asks for. Their citizen asks for sixty five hundredths of the wrist on the forearm,
	//half of the shoulder the other way on the upper arm, six tenths of the ankle on the shin. Nothing but this puts those bones anywhere.
	[TestMethod]
	public void ATwistBoneTakesItsShareOfTheWrist()
	{
		var skeleton = Arm();
		var instance = Standing( skeleton );

		//The wrist, turned about the arm's own length.
		instance.Pose( 0 )[2].Rotation = Rotation.FromAxis( Vector3.Right, 90.0f );

		var constraint = new CoreAnimConstraints.Bound
		{
			Kind = ModelDefinition.ConstraintKind.TiltTwist,
			Bone = skeleton.Find( "arm_lower_twist" ),
			Weight = 0.65f,
			Drivers = new[] { skeleton.Find( "hand" ) },
			Shares = new[] { 1.0f },
		};

		CoreAnimConstraints.Apply( instance, 0, new[] { constraint } );

		Assert.AreEqual( 90.0f * 0.65f, Twist( instance, 0, "arm_lower_twist" ), 0.5f,
			"the twist bone is what keeps the skin from shearing where the hand starts" );

		//And it turns about the arm rather than moving along it.
		Assert.AreEqual( 0.12f, instance.Pose( 0 )[1].Position.x, 0.0001f,
			"a twist is a turn, and a bone that moved is a bone somewhere it cannot be" );
	}

	//A joint that only bends is not a joint that twists. The share is of the turn about the bone's own length and of nothing
	//else: a wrist bent forwards is the hand pointing somewhere, and a forearm that followed that would fold in the middle.
	[TestMethod]
	public void ATwistBoneIgnoresTheBend()
	{
		var skeleton = Arm();
		var instance = Standing( skeleton );

		instance.Pose( 0 )[2].Rotation = Rotation.FromAxis( Vector3.Up, 60.0f );

		var constraint = new CoreAnimConstraints.Bound
		{
			Kind = ModelDefinition.ConstraintKind.TiltTwist,
			Bone = skeleton.Find( "arm_lower_twist" ),
			Weight = 0.65f,
			Drivers = new[] { skeleton.Find( "hand" ) },
			Shares = new[] { 1.0f },
		};

		CoreAnimConstraints.Apply( instance, 0, new[] { constraint } );

		Assert.AreEqual( 0.0f, Twist( instance, 0, "arm_lower_twist" ), 0.5f,
			"a bend is not a twist, and copying it folds the arm in the middle" );
	}

	//A position-constrained bone sits at the weighted average of its drivers
	[TestMethod]
	public void APlacedBoneSitsBetweenTheBonesThatDriveIt()
	{
		var skeleton = Arm();
		var instance = Standing( skeleton );

		//The wrist, turned about the arm's own length and bent as well.
		instance.Pose( 0 )[2].Rotation = Rotation.FromAxis( Vector3.Right, 90.0f )
			* Rotation.FromAxis( Vector3.Up, 40.0f );

		var was = instance.Pose( 0 )[1].Position;

		var constraint = new CoreAnimConstraints.Bound
		{
			Kind = ModelDefinition.ConstraintKind.Parent,
			Bone = skeleton.Find( "arm_lower_twist" ),
			Weight = 1.75f,
			Drivers = new[] { skeleton.Find( "arm_lower" ), skeleton.Find( "hand" ) },
			Shares = new[] { 0.5f, 0.5f },
			Offsets = new[] { AnimTrs.Identity, AnimTrs.Identity },
		};

		CoreAnimConstraints.Apply( instance, 0, new[] { constraint } );

		Assert.AreEqual( was.x, instance.Pose( 0 )[1].Position.x, 0.0005f,
			"a wrist that turns in place moves neither end of the forearm, so nothing between them moves either" );

		Assert.AreEqual( was.y, instance.Pose( 0 )[1].Position.y, 0.0005f );
		Assert.AreEqual( was.z, instance.Pose( 0 )[1].Position.z, 0.0005f );
	}

	//And it goes with them, further than they went, where they do move. Their weights run past one - three quarters further on the
	//twist bones, a quarter of the way on the elbow helpers - which is what pushes a helper out of the skin as the joint folds.
	[TestMethod]
	public void APlacedBoneGoesFurtherThanItsDrivers()
	{
		var skeleton = Arm();
		var instance = Standing( skeleton );

		//The hand pulled a tenth of a metre further out along the arm.
		instance.Pose( 0 )[2].Position += new Vector3( 0.1f, 0.0f, 0.0f );

		var constraint = new CoreAnimConstraints.Bound
		{
			Kind = ModelDefinition.ConstraintKind.Parent,
			Bone = skeleton.Find( "arm_lower_twist" ),
			Weight = 1.75f,
			Drivers = new[] { skeleton.Find( "arm_lower" ), skeleton.Find( "hand" ) },
			Shares = new[] { 0.5f, 0.5f },
			Offsets = new[] { AnimTrs.Identity, AnimTrs.Identity },
		};

		CoreAnimConstraints.Apply( instance, 0, new[] { constraint } );

		//Halfway between an elbow that has not moved and a wrist ten centimetres further out is five, and seven and a half at their weight.
		Assert.AreEqual( 0.12f + ( 0.05f * 1.75f ), instance.Pose( 0 )[1].Position.x, 0.0005f,
			"the weight is what pushes an elbow helper out of the skin as the joint folds" );
	}
}
