using System;
using Rinten.CoreAnimation;

namespace EngineTests;

//Tests whether masked blends happen in model space or parent-local space
[TestClass]
public class AnimBlendSpaceTest
{
	//root, spine, chest, hand - each a quarter metre above the last.
	private static CoreAnimRuntime.Skeleton Body()
	{
		var names = new[] { "root", "spine", "chest", "hand" };

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

		for ( var bone = 0; bone < count; bone++ )
			instance.Local[bone] = skeleton.Bones[bone].Rest;

		return instance;
	}

	//Somewhere to put a pose, filled with the rest pose.
	private static int Pose( CoreAnimRuntime.Instance instance )
	{
		var pose = new AnimTrs[instance.Skeleton.Bones.Length];

		for ( var bone = 0; bone < pose.Length; bone++ )
			pose[bone] = instance.Skeleton.Bones[bone].Rest;

		instance.Poses.Add( pose );
		instance.PoseUsed.Add( true );

		return instance.Poses.Count - 1;
	}

	//Somewhere to put a mask, with a weight per bone.
	private static int Mask( CoreAnimRuntime.Instance instance, params float[] weights )
	{
		instance.Masks.Add( weights );
		instance.MaskUsed.Add( true );

		return instance.Masks.Count - 1;
	}

	//Which way a bone points once the whole pose is composed.
	private static float Yaw( CoreAnimRuntime.Instance instance, int pose, int bone )
	{
		var facing = (Rotation)CoreAnimRuntime.ModelOf( instance.Pose( pose ), instance.Skeleton, bone ).Rotation
			* Vector3.Forward;

		return facing.EulerAngles.yaw;
	}

	//Part-weight blends in model space, so the hand reaches the layer's pose despite a low spine weight
	[TestMethod]
	public void AModelSpaceBlendLandsTheHandWhereTheLayerHasIt()
	{
		var skeleton = Body();
		var instance = Standing( skeleton );

		var under = Pose( instance );
		var over = Pose( instance );

		instance.Pose( over )[1].Rotation = Rotation.From( new Angles( 0.0f, 40.0f, 0.0f ) );

		//root, spine, chest, hand.
		var mask = Mask( instance, 0.0f, 0.25f, 1.0f, 1.0f );

		CoreAnimRuntime.PoseBlendModel( instance, under, under, over, 1.0f, mask, rotationOnly: true );

		Assert.AreEqual( 10.0f, Yaw( instance, under, 1 ), 0.5f,
			"the spine takes the quarter its weight asks for" );

		Assert.AreEqual( 40.0f, Yaw( instance, under, 2 ), 0.5f,
			"the chest is weighed at one, so it arrives where the layer has it" );

		Assert.AreEqual( 40.0f, Yaw( instance, under, 3 ), 0.5f,
			"and so does the hand, whatever its parents kept" );
	}

	//Blending where the bones stand leaves them where they sat. Rotation only: the turn is blended and the offset from the parent
	//is the one the bone came with, so a body keeps its proportions through a blend that only means to say which way it is facing.
	[TestMethod]
	public void ARotationOnlyBlendKeepsTheBonesWhereTheySat()
	{
		var skeleton = Body();
		var instance = Standing( skeleton );

		var under = Pose( instance );
		var over = Pose( instance );

		instance.Pose( over )[1].Rotation = Rotation.From( new Angles( 0.0f, 40.0f, 0.0f ) );
		instance.Pose( over )[2].Position = new Vector3( 0, 0, 4.0f );

		var mask = Mask( instance, 1.0f, 1.0f, 1.0f, 1.0f );

		CoreAnimRuntime.PoseBlendModel( instance, under, under, over, 1.0f, mask, rotationOnly: true );

		Assert.AreEqual( 0.25f, instance.Pose( under )[2].Position.z, 0.001f,
			"a rotation-only blend brought a position across and stretched the body" );
	}

	//The whole transform, where the layer says model space without saying rotation only.
	[TestMethod]
	public void AModelSpaceBlendCarriesPositionsWhenItIsNotRotationOnly()
	{
		var skeleton = Body();
		var instance = Standing( skeleton );

		var under = Pose( instance );
		var over = Pose( instance );

		instance.Pose( over )[2].Position = new Vector3( 0, 0, 4.0f );

		var mask = Mask( instance, 1.0f, 1.0f, 1.0f, 1.0f );

		CoreAnimRuntime.PoseBlendModel( instance, under, under, over, 1.0f, mask, rotationOnly: false );

		Assert.AreEqual( 4.0f, instance.Pose( under )[2].Position.z, 0.001f,
			"a full model-space blend is the whole transform, offsets and all" );
	}

	//Additive deltas apply after the base rotation, the order the citizen's aim grids require
	[TestMethod]
	public void ADifferenceLaidOnTurnsAfterWhatIsUnderIt()
	{
		var skeleton = Body();
		var instance = Standing( skeleton );

		var under = Pose( instance );
		var over = Pose( instance );

		var basePose = Rotation.From( new Angles( 0.0f, 40.0f, 0.0f ) );
		var difference = Rotation.From( new Angles( 25.0f, 0.0f, 0.0f ) );

		instance.Pose( under )[2].Rotation = basePose;
		instance.Pose( over )[2].Rotation = difference;

		CoreAnimRuntime.PoseAdditive( instance, under, under, over, 1.0f, -1, separately: true );

		var wanted = (Rotation)( basePose * difference );
		var got = (Rotation)instance.Pose( under )[2].Rotation;

		var off = MathF.Acos( Math.Clamp( MathF.Abs(
			( got.x * wanted.x ) + ( got.y * wanted.y ) + ( got.z * wanted.z ) + ( got.w * wanted.w ) ),
			0.0f, 1.0f ) ) * 2.0f * 180.0f / MathF.PI;

		Assert.IsTrue( off < 0.1f,
			$"the difference went on {off:0.0} degrees away from where it belongs, which is the other order" );
	}
}
