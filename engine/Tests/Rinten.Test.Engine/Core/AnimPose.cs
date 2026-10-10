using System;
using System.IO;

using Rinten.CoreAnimation;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests;

//Skeletons and the poses that stand in them. Three bones in a line, which is enough for everything that can go wrong: the rest pose
//has to come out of the inverse binds, a clip has to find its bones by name, and a bone has to carry its children when it turns.
[TestClass]
public class AnimPoseTest
{
	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-pose-{Guid.NewGuid():N}{extension}" );

	//The inverse of a bone resting at a position, in the layout a model file carries: row vectors, translation in the last row.
	private static float[] InverseBind( Vector3 rest ) => new float[]
	{
		1, 0, 0, 0,
		0, 1, 0, 0,
		0, 0, 1, 0,
		-rest.x, -rest.y, -rest.z, 1,
	};

	//A model with a skeleton and nothing else: root at the floor, spine a metre up, head a metre above that.
	private static unsafe CoreAnimRuntime.Skeleton BuildSkeleton( string path )
	{
		var builder = new CoreAssets.ModelBuilder();
		Assert.IsNotNull( builder );

		try
		{
			var rest = new[] { Vector3.Zero, new Vector3( 0, 0, 1 ), new Vector3( 0, 0, 2 ) };
			var names = new[] { "root", "spine", "head" };

			for ( var bone = 0; bone < names.Length; bone++ )
			{
				var matrix = InverseBind( rest[bone] );
				fixed ( float* m = matrix )
				{
					Assert.AreEqual( bone, builder.AddBone( names[bone], bone - 1, m ) );
				}
			}

			Assert.AreEqual( 1, builder.Save( path ) );
		}
		finally
		{
			builder.Dispose();
		}

		var model = CoreAssets.ModelFile.Load( path );
		Assert.IsNotNull( model, "the skeleton should load back" );

		var skeleton = CoreAnimRuntime.CreateSkeleton( model );
		model.Dispose();

		Assert.IsNotNull( skeleton, "a model with bones should make a skeleton" );

		return skeleton;
	}

	//A clip over the same three bones. The spine turns a quarter about x across one second; everything else stays where the skeleton rests.
	private static unsafe AnimClipData BuildClip( string path, Rotation turn, Vector3 rootAtEnd )
	{
		var builder = new AnimClipBuilder();

		try
		{
			builder.SetFrameRate( 1.0f );

			builder.AddBone( "root", -1 );
			builder.AddBone( "spine", 0 );
			builder.AddBone( "head", 1 );

			void Track( int bone, Transform first, Transform second )
			{
				Assert.IsTrue( builder.SetFrames( bone,
					new[] { AnimTrs.FromTransform( first ), AnimTrs.FromTransform( second ) } ) );
			}

			Track( 0,
				new Transform( Vector3.Zero, Rotation.Identity, 1.0f ),
				new Transform( rootAtEnd, Rotation.Identity, 1.0f ) );

			Track( 1,
				new Transform( new Vector3( 0, 0, 1 ), Rotation.Identity, 1.0f ),
				new Transform( new Vector3( 0, 0, 1 ), turn, 1.0f ) );

			Track( 2,
				new Transform( new Vector3( 0, 0, 1 ), Rotation.Identity, 1.0f ),
				new Transform( new Vector3( 0, 0, 1 ), Rotation.Identity, 1.0f ) );

			Assert.IsTrue( builder.Save( path ) );
		}
		finally
		{
			builder.Dispose();
		}

		var clip = AnimClipData.Load( path );
		Assert.IsNotNull( clip );

		return clip;
	}

	private static Transform ModelBone( CoreAnimRuntime.Instance instance, int bone )
	{
		Assert.IsTrue( instance.ModelBone( bone, out var trs ) );

		return trs.ToTransform();
	}

	//The skeleton is the model's bones, and the rest pose is worked out from the inverse binds - which is all a model file says about where a bone stands.
	[TestMethod]
	public unsafe void SkeletonComesOutOfTheModelsBones()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var path = TempPath( ".mdl_c" );
		var skeleton = BuildSkeleton( path );

		try
		{
			Assert.AreEqual( 3, skeleton.BoneCount );

			Assert.AreEqual( "root", skeleton.BoneName( 0 ) );
			Assert.AreEqual( "spine", skeleton.BoneName( 1 ) );
			Assert.AreEqual( "head", skeleton.BoneName( 2 ) );

			Assert.AreEqual( -1, skeleton.BoneParent( 0 ) );
			Assert.AreEqual( 0, skeleton.BoneParent( 1 ) );
			Assert.AreEqual( 1, skeleton.BoneParent( 2 ) );

			//Ignoring case, because two files describing one rig disagree about capitals often enough that matching on them would lose bones.
			Assert.AreEqual( 1, skeleton.FindBone( "SPINE" ) );
			Assert.AreEqual( -1, skeleton.FindBone( "tail" ) );

			//Each one a metre above the bone it hangs from, which is the whole point of a rest pose being local.
			Assert.AreEqual( 0.0f, skeleton.Rest( 0 ).Position.z, 0.001f, "the root rests on the floor" );
			Assert.AreEqual( 1.0f, skeleton.Rest( 1 ).Position.z, 0.001f, "the spine rests a metre above the root" );
			Assert.AreEqual( 1.0f, skeleton.Rest( 2 ).Position.z, 0.001f, "the head rests a metre above the spine" );
		}
		finally
		{
			File.Delete( path );
		}
	}

	//A fresh instance stands in the bind pose, and its bones are where the model put them.
	[TestMethod]
	public unsafe void BindPoseStandsWhereTheModelDoes()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var path = TempPath( ".mdl_c" );
		var skeleton = BuildSkeleton( path );
		var instance = CoreAnimRuntime.CreateInstance( skeleton );

		try
		{
			Assert.IsNotNull( instance );
			Assert.AreEqual( 3, instance.BoneCount );

			Assert.AreEqual( 0.0f, ModelBone( instance, 0 ).Position.z, 0.001f );
			Assert.AreEqual( 1.0f, ModelBone( instance, 1 ).Position.z, 0.001f );
			Assert.AreEqual( 2.0f, ModelBone( instance, 2 ).Position.z, 0.001f );
		}
		finally
		{
			File.Delete( path );
		}
	}

	//A clip poses the skeleton, and a bone that turns carries what hangs off it - which is the difference between a pose and a list of transforms.
	[TestMethod]
	public unsafe void ClipTurnsABoneAndItsChildrenFollow()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var modelPath = TempPath( ".mdl_c" );
		var clipPath = TempPath( ".anim_c" );

		var skeleton = BuildSkeleton( modelPath );
		var clip = BuildClip( clipPath, Rotation.FromAxis( Vector3.Right, 90.0f ), Vector3.Zero );
		var instance = CoreAnimRuntime.CreateInstance( skeleton );

		try
		{
			CoreAnimRuntime.SetClip( instance, clip, false );
			Assert.AreEqual( 1.0f, instance.Clip.Duration, 0.001f );

			//All the way to the end, where the spine has turned a quarter.
			CoreAnimRuntime.Update( instance, 1.0f );

			Assert.IsTrue( instance.Finished, "a clip that does not loop runs out" );

			var spine = ModelBone( instance, 1 );
			var head = ModelBone( instance, 2 );

			Assert.AreEqual( 1.0f, spine.Position.z, 0.01f, "the spine has not moved, only turned" );
			Assert.AreEqual( 90.0f, spine.Rotation.Angle(), 0.5f );

			//The head was a metre above the spine and the spine turned a quarter about x, so it swings to a metre along -y.
			Assert.AreEqual( 0.0f, head.Position.x, 0.01f );
			Assert.AreEqual( -1.0f, head.Position.y, 0.01f );
			Assert.AreEqual( 1.0f, head.Position.z, 0.01f );

			//And back, which is what a bind pose is for.
			CoreAnimRuntime.BindPose( instance );
			Assert.AreEqual( 2.0f, ModelBone( instance, 2 ).Position.z, 0.001f );
		}
		finally
		{
			File.Delete( modelPath );
			File.Delete( clipPath );
		}
	}

	//A bone driven from outside - a physics bone, or one a game object moves by hand - keeps its
	//transform through an update that would otherwise pose it, and carries its children with it.
	[TestMethod]
	public unsafe void OverriddenBonesOutlastTheClip()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var modelPath = TempPath( ".mdl_c" );
		var clipPath = TempPath( ".anim_c" );

		var skeleton = BuildSkeleton( modelPath );
		var clip = BuildClip( clipPath, Rotation.FromAxis( Vector3.Right, 90.0f ), Vector3.Zero );
		var instance = CoreAnimRuntime.CreateInstance( skeleton );

		try
		{
			Assert.AreEqual( 0, instance.OverrideCount );

			var driven = new Transform( new Vector3( 0, 0, 3 ), Rotation.Identity, 1.0f );

			CoreAnimRuntime.SetBone( instance, 1, AnimTrs.FromTransform( driven ) );
			Assert.AreEqual( 1, instance.OverrideCount );

			CoreAnimRuntime.SetClip( instance, clip, false );
			CoreAnimRuntime.Update( instance, 1.0f );

			var spine = ModelBone( instance, 1 );
			Assert.AreEqual( 3.0f, spine.Position.z, 0.001f, "the clip should not have moved a driven bone" );
			Assert.AreEqual( 0.0f, spine.Rotation.Angle(), 0.5f, "nor turned it" );

			//The head is not driven, so it is still where the clip and its parent put it - a metre above a spine that is now three up.
			Assert.AreEqual( 4.0f, ModelBone( instance, 2 ).Position.z, 0.001f );

			CoreAnimRuntime.ClearBones( instance );
			Assert.AreEqual( 0, instance.OverrideCount );

			CoreAnimRuntime.Update( instance, 0.0f );
			Assert.AreEqual( 1.0f, ModelBone( instance, 1 ).Position.z, 0.001f, "and the clip has it back" );
		}
		finally
		{
			File.Delete( modelPath );
			File.Delete( clipPath );
		}
	}

	//A bone merge takes the other instance's pose for every bone the two share a name for. This is how a shirt follows the body wearing it.
	[TestMethod]
	public unsafe void MergeTakesTheOtherPose()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var modelPath = TempPath( ".mdl_c" );
		var clipPath = TempPath( ".anim_c" );

		var skeleton = BuildSkeleton( modelPath );
		var clip = BuildClip( clipPath, Rotation.FromAxis( Vector3.Right, 90.0f ), Vector3.Zero );

		var body = CoreAnimRuntime.CreateInstance( skeleton );
		var shirt = CoreAnimRuntime.CreateInstance( skeleton );

		try
		{
			CoreAnimRuntime.SetClip( body, clip, false );
			CoreAnimRuntime.Update( body, 1.0f );

			//Before the merge the shirt is still standing in the bind pose.
			Assert.AreEqual( 2.0f, ModelBone( shirt, 2 ).Position.z, 0.001f );

			Assert.AreEqual( 3, CoreAnimRuntime.Merge( shirt, body ), "every bone should have matched" );

			var head = ModelBone( shirt, 2 );
			Assert.AreEqual( -1.0f, head.Position.y, 0.01f );
			Assert.AreEqual( 1.0f, head.Position.z, 0.01f );
		}
		finally
		{
			File.Delete( modelPath );
			File.Delete( clipPath );
		}
	}

	//Root motion is what the root bone did since the last update, in its own frame - the number a caller needs to move the character the clip walked.
	[TestMethod]
	public unsafe void RootMotionIsWhatTheRootDid()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var modelPath = TempPath( ".mdl_c" );
		var clipPath = TempPath( ".anim_c" );

		var skeleton = BuildSkeleton( modelPath );
		var clip = BuildClip( clipPath, Rotation.Identity, new Vector3( 1, 0, 0 ) );
		var instance = CoreAnimRuntime.CreateInstance( skeleton );

		try
		{
			CoreAnimRuntime.SetClip( instance, clip, false );

			//The first update has nothing to measure against, so it reports nothing rather than the whole distance from the origin.
			CoreAnimRuntime.Update( instance, 0.0f );
			Assert.AreEqual( 0.0f, instance.RootMotion.Position.Length, 0.001f );

			CoreAnimRuntime.Update( instance, 0.5f );
			Assert.AreEqual( 0.5f, instance.RootMotion.Position.x, 0.01f,
				"half a second into a clip that walks a metre a second" );

			CoreAnimRuntime.Update( instance, 0.5f );
			Assert.AreEqual( 0.5f, instance.RootMotion.Position.x, 0.01f );
		}
		finally
		{
			File.Delete( modelPath );
			File.Delete( clipPath );
		}
	}
}
