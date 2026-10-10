using System;
using System.IO;

using Rinten.CoreAnimation;

namespace EngineTests;

//Our own animation format, written, read back and sampled. A format is a promise between a writer and a reader - and a clip has a third party
//to that promise, the sampler, because it is the only code that decides what a time between two frames means. These make the whole trip.
[TestClass]
public class AnimFormatTest
{
	//What the builder takes a frame in: the file's layout.
	private const int Stride = AnimClipBuilder.FloatsPerFrame;

	private static string TempPath( string name )
		=> Path.Combine( Path.GetTempPath(), $"rintencore-test-{name}-{Guid.NewGuid():N}.anim_c" );

	//Ten floats a frame: position, rotation, scale.
	private static float[] Frames( int count, Func<int, (Vector3 position, Rotation rotation, Vector3 scale)> at )
	{
		var frames = new float[count * Stride];

		for ( var frame = 0; frame < count; frame++ )
		{
			var (position, rotation, scale) = at( frame );
			var i = frame * Stride;

			frames[i + 0] = position.x;
			frames[i + 1] = position.y;
			frames[i + 2] = position.z;

			frames[i + 3] = rotation.x;
			frames[i + 4] = rotation.y;
			frames[i + 5] = rotation.z;
			frames[i + 6] = rotation.w;

			frames[i + 7] = scale.x;
			frames[i + 8] = scale.y;
			frames[i + 9] = scale.z;
		}

		return frames;
	}

	//Three bones over five frames: one that slides, one that turns, and one that does nothing at all - which is the case the format compresses.
	private static AnimClipData BuildTestClip( string path, bool looping )
	{
		var builder = new AnimClipBuilder();
		Assert.IsNotNull( builder, "the core should have made a builder" );

		try
		{
			builder.Name = "animations/test/walk.anim";
			builder.SetFrameRate( 20.0f );
			builder.Looping = looping;

			Assert.AreEqual( 0, builder.AddBone( "root", -1 ) );
			Assert.AreEqual( 1, builder.AddBone( "spine", 0 ) );
			Assert.AreEqual( 2, builder.AddBone( "head", 1 ) );

			//A bone cannot hang off one that does not exist yet, or a reader would have to look forward to know where it is.
			Assert.AreEqual( -1, builder.AddBone( "ghost", 99 ) );

			var root = Frames( 5, frame => ( new Vector3( frame, 0, 0 ), Rotation.Identity, Vector3.One ) );
			var spine = Frames( 5, frame => ( Vector3.Zero, Rotation.FromAxis( Vector3.Up, frame * 20.0f ), Vector3.One ) );
			var head = Frames( 5, _ => ( new Vector3( 0, 0, 1 ), Rotation.Identity, Vector3.One ) );

			Assert.IsTrue( builder.SetFrames( 0, root, 5 ) );
			Assert.IsTrue( builder.SetFrames( 1, spine, 5 ) );
			Assert.IsTrue( builder.SetFrames( 2, head, 5 ) );

			//A track that is neither one frame nor the clip's length is two animations in one file, and is refused rather than truncated.
			var wrong = Frames( 3, _ => ( Vector3.Zero, Rotation.Identity, Vector3.One ) );
			Assert.IsFalse( builder.SetFrames( 2, wrong, 3 ) );

			Assert.IsTrue( builder.Save( path ), "the clip should have been written" );
		}
		finally
		{
			builder.Dispose();
		}

		var clip = AnimClipData.Load( path );
		Assert.IsNotNull( clip, "the clip should load back" );

		return clip;
	}

	//The whole trip: a skeleton and its keys in, a file, and the same numbers back out of the sampler.
	[TestMethod]
	public void ClipSurvivesTheRoundTrip()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var path = TempPath( "roundtrip" );
		var clip = BuildTestClip( path, looping: false );

		try
		{
			Assert.AreEqual( "animations/test/walk.anim", clip.Name );

			Assert.AreEqual( 5, clip.FrameCount );
			Assert.AreEqual( 20.0f, clip.FrameRate, 0.0001f );

			//Four gaps between five frames at a twentieth of a second each.
			Assert.AreEqual( 0.2f, clip.Duration, 0.0001f );

			Assert.IsFalse( clip.Looping );
			Assert.IsFalse( clip.Additive );

			Assert.AreEqual( 3, clip.Bones.Length );

			Assert.AreEqual( "root", clip.Bones[0].Name );
			Assert.AreEqual( "spine", clip.Bones[1].Name );
			Assert.AreEqual( "head", clip.Bones[2].Name );

			Assert.AreEqual( -1, clip.Bones[0].Parent );
			Assert.AreEqual( 0, clip.Bones[1].Parent );
			Assert.AreEqual( 1, clip.Bones[2].Parent );

			var pose = new AnimTrs[3];

			Assert.AreEqual( 3, clip.Sample( 0.0f, false, pose ) );

			Assert.AreEqual( 0.0f, pose[0].Position.x, 0.0001f, "the root starts at the origin" );
			Assert.AreEqual( 1.0f, pose[1].Rotation.W, 0.0001f, "the spine starts unturned" );
			Assert.AreEqual( 1.0f, pose[2].Position.z, 0.0001f, "the head sits where it was put" );

			//Halfway between the third and fourth frames - a time no key sits on, so this is the interpolation and not a lookup.
			Assert.AreEqual( 3, clip.Sample( 0.125f, false, pose ) );

			Assert.AreEqual( 2.5f, pose[0].Position.x, 0.0001f, "the root slides a unit a frame" );

			Assert.AreEqual( 50.0f, ( (Rotation)pose[1].Rotation ).Angle(), 0.5f,
				"the spine turns twenty degrees a frame" );

			//Past the end of a clip that does not loop: the last frame stands.
			Assert.AreEqual( 3, clip.Sample( 10.0f, false, pose ) );

			Assert.AreEqual( 4.0f, pose[0].Position.x, 0.0001f, "the root holds where the clip left it" );
		}
		finally
		{
			File.Delete( path );
		}
	}

	//A looping clip asked for a time past its end wraps into it, rather than holding still like one that does not loop.
	[TestMethod]
	public void LoopingWrapsInsteadOfHolding()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var path = TempPath( "looping" );
		var clip = BuildTestClip( path, looping: true );

		try
		{
			Assert.IsTrue( clip.Looping, "the clip should say it loops" );

			var pose = new AnimTrs[3];

			//A whole duration and a frame past the start.
			Assert.AreEqual( 3, clip.Sample( 0.2f + 0.05f, true, pose ) );

			Assert.AreEqual( 1.0f, pose[0].Position.x, 0.0001f, "a lap and a frame in is the second frame" );

			//Before the start wraps the same way round.
			Assert.AreEqual( 3, clip.Sample( -0.05f, true, pose ) );

			Assert.AreEqual( 3.0f, pose[0].Position.x, 0.0001f, "a frame before the start is a frame before the end" );

			//The same clip sampled without looping holds instead.
			clip.Sample( 5.0f, false, pose );

			Assert.AreEqual( 4.0f, pose[0].Position.x, 0.0001f );
		}
		finally
		{
			File.Delete( path );
		}
	}

	//A pose asked for fewer bones than the clip has writes only that many - the caller's buffer is the limit, not the clip's bone count.
	[TestMethod]
	public void SamplingStopsAtTheCallersCapacity()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var path = TempPath( "capacity" );
		var clip = BuildTestClip( path, looping: false );

		try
		{
			var sentinel = new AnimTrs { Position = new Vector3( -12345.0f, -12345.0f, -12345.0f ) };

			var pose = new AnimTrs[3];
			for ( var i = 0; i < pose.Length; i++ ) pose[i] = sentinel;

			//One bone of room, and the clip has three: the span is the limit.
			Assert.AreEqual( 1, clip.Sample( 0.1f, false, pose.AsSpan( 0, 1 ) ),
				"one bone asked for, one written" );

			for ( var bone = 1; bone < pose.Length; bone++ )
			{
				Assert.AreEqual( sentinel.Position.x, pose[bone].Position.x, 0.0001f,
					$"bone {bone} is past the one that was asked for" );
			}
		}
		finally
		{
			File.Delete( path );
		}
	}

	//A bone that never moves costs one key, not one per frame; measured by file size
	[TestMethod]
	public void StillBonesCostOneKey()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		const int bones = 8;
		const int frames = 200;

		long Write( string path, bool moving )
		{
			var builder = new AnimClipBuilder();

			try
			{
				for ( var bone = 0; bone < bones; bone++ )
					builder.AddBone( $"bone{bone}", bone == 0 ? -1 : bone - 1 );

				for ( var bone = 0; bone < bones; bone++ )
				{
					var keys = Frames( frames, frame =>
						( new Vector3( moving ? frame : 0.0f, 0, 0 ), Rotation.Identity, Vector3.One ) );

					builder.SetFrames( bone, keys, frames );
				}

				Assert.IsTrue( builder.Save( path ) );
			}
			finally
			{
				builder.Dispose();
			}

			return new FileInfo( path ).Length;
		}

		var stillPath = TempPath( "still" );
		var movingPath = TempPath( "moving" );

		try
		{
			var still = Write( stillPath, moving: false );
			var moving = Write( movingPath, moving: true );

			Assert.IsTrue( moving > still * 4,
				$"a clip that moves should cost far more than one that does not - {moving} against {still}" );

			//And what it costs is still readable: compression that loses the pose would pass the test above and fail everything after it.
			var clip = AnimClipData.Load( stillPath );
			Assert.IsNotNull( clip );

			var pose = new AnimTrs[bones];

			Assert.AreEqual( bones, clip.Sample( 3.0f, false, pose ) );

			for ( var bone = 0; bone < bones; bone++ )
			{
				Assert.AreEqual( 1.0f, pose[bone].Rotation.W, 0.0001f, $"bone {bone} should be unturned" );
				Assert.AreEqual( 1.0f, pose[bone].Scale.x, 0.0001f, $"bone {bone} should be unscaled" );
			}
		}
		finally
		{
			File.Delete( stillPath );
			File.Delete( movingPath );
		}
	}

	//A file that is not one of ours, or is half of one, is refused rather than read as a valid clip full of whatever was next in memory.
	[TestMethod]
	public void RefusesWhatIsNotAClip()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var garbage = TempPath( "garbage" );
		var truncated = TempPath( "truncated" );
		var real = TempPath( "real" );

		try
		{
			File.WriteAllText( garbage, "this is not a clip, it is a sentence" );
			Assert.IsNull( AnimClipData.Load( garbage ) );
			Assert.IsNull( AnimClipData.Load( "no/such/file.anim_c" ) );

			BuildTestClip( real, looping: false );

			var bytes = File.ReadAllBytes( real );
			File.WriteAllBytes( truncated, bytes[..( bytes.Length - 16 )] );

			Assert.IsNull( AnimClipData.Load( truncated ),
				"a file cut short reads as valid until something walks off the end of it" );
		}
		finally
		{
			File.Delete( garbage );
			File.Delete( truncated );
			File.Delete( real );
		}
	}
}
