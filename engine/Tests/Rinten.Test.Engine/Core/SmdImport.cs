using System.IO;

using Rinten.CoreAnimation;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests;

[TestClass]
public class SmdImportTest
{
	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-smd-{System.Guid.NewGuid():N}{extension}" );

	[TestMethod]
	public void ATriangleSmdWritesOurModel()
	{
		if ( !CoreNative.Available ) return;

		var smd = TempPath( ".smd" );
		var compiled = TempPath( ".mdl_c" );

		File.WriteAllText( smd, """
			version 1
			nodes
			0 "root" -1
			end
			skeleton
			time 0
			0 0 0 0 0 0 0
			end
			triangles
			test
			0  0 0 0  0 0 1  0 0
			0  1 0 0  0 0 1  1 0
			0  0 1 0  0 0 1  0 1
			end
			""" );

		try
		{
			var builder = new CoreAssets.ModelBuilder();

			try
			{
				Assert.AreEqual( 1, CoreSmdImport.Import( builder, smd, 1.0f ) );
				Assert.AreEqual( 1, builder.Save( compiled ) );
				Assert.IsTrue( File.Exists( compiled ), "the compiled model should have been written" );
			}
			finally
			{
				builder.Dispose();
			}
		}
		finally
		{
			if ( File.Exists( smd ) ) File.Delete( smd );
			if ( File.Exists( compiled ) ) File.Delete( compiled );
		}
	}

	[TestMethod]
	public void AnAnimationSmdHasNoMeshes()
	{
		if ( !CoreNative.Available ) return;

		var smd = TempPath( ".smd" );

		File.WriteAllText( smd, """
			version 1
			nodes
			0 "root" -1
			end
			skeleton
			time 0
			0 0 0 0 0 0 0
			time 1
			0 0 0 1 0 0 0
			end
			""" );

		try
		{
			var builder = new CoreAssets.ModelBuilder();

			try
			{
				Assert.AreEqual( 0, CoreSmdImport.Import( builder, smd, 1.0f ) );
			}
			finally
			{
				builder.Dispose();
			}
		}
		finally
		{
			if ( File.Exists( smd ) ) File.Delete( smd );
		}
	}

	[TestMethod]
	public unsafe void AnAnimationSmdWritesAClip()
	{
		if ( !CoreNative.Available ) return;

		var smd = TempPath( ".smd" );
		var compiled = TempPath( ".anim_c" );

		File.WriteAllText( smd, """
			version 1
			nodes
			0 "root" -1
			1 "child" 0
			end
			skeleton
			time 0
			0 0 0 0 0 0 0
			1 0 0 0 0 0 0
			time 1
			0 0 0 2 0 0 0
			1 1 0 0 0 0 0
			end
			""" );

		try
		{
			var builder = new AnimClipBuilder();
			int frames;

			try
			{
				frames = CoreSmdImport.ImportClip( builder, smd, 1.0f );
				Assert.AreEqual( 2, frames );
				Assert.IsTrue( builder.Save( compiled ) );
			}
			finally
			{
				builder.Dispose();
			}

			var clip = AnimClipData.Load( compiled );

			Assert.AreEqual( 2, clip.FrameCount );
			Assert.AreEqual( 30.0f, clip.FrameRate, 0.0001f );
			Assert.AreEqual( 2, clip.Bones.Length );
			Assert.AreEqual( "root", clip.Bones[0].Name );
			Assert.AreEqual( "child", clip.Bones[1].Name );
			Assert.AreEqual( 0, clip.Bones[1].Parent );

			var pose = new AnimTrs[2];

			clip.Sample( 0.0f, false, pose );
			Assert.AreEqual( 0.0f, pose[0].Position.y, 0.0001f );

			//Two of their units up is two of ours up, which is y; the child's step along their forward is a step along ours, which is -z.
			clip.Sample( 1.0f / 30.0f, false, pose );
			Assert.AreEqual( 2.0f, pose[0].Position.y, 0.0001f );
			Assert.AreEqual( -1.0f, pose[1].Position.z, 0.0001f );
		}
		finally
		{
			if ( File.Exists( smd ) ) File.Delete( smd );
			if ( File.Exists( compiled ) ) File.Delete( compiled );
		}
	}

	[TestMethod]
	public void AnAnimationSmdCompilesFromAModelClip()
	{
		if ( !CoreNative.Available ) return;

		var folder = Path.Combine( Path.GetTempPath(), $"rinten-smd-def-{System.Guid.NewGuid():N}" );
		Directory.CreateDirectory( folder );

		var smd = Path.Combine( folder, "walk.smd" );
		var compiled = Path.Combine( folder, "walk.anim_c" );

		File.WriteAllText( smd, """
			version 1
			nodes
			0 "root" -1
			end
			skeleton
			time 0
			0 0 0 0 0 0 0
			time 2
			0 0 0 1 0 0 0
			end
			""" );

		var modelClip = new ModelDefinition.ModelClip
		{
			File = "walk.smd",
			TakeName = "walk_n",
			Scale = 0.0254f,
			Looping = true,
			Additive = false,
		};

		try
		{
			var result = AnimationCompiler.CompileModelClip( modelClip, null, folder, compiled );

			Assert.IsTrue( result.Success, result.Reason );
			Assert.AreEqual( 3, result.Frames );

			var clip = AnimClipData.Load( compiled );

			Assert.AreEqual( "walk_n", clip.Name );
			Assert.AreEqual( 3, clip.FrameCount );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}
}
