using System;
using System.IO;
using Rinten.Animation;
using Rinten.CoreAnimation;

using CoreAssets = Rinten.CoreAssets;

namespace SceneTests;

//A scene model's graph, parameters and rate survive a model change (e.g. drawing a weapon)
[TestClass]
[DoNotParallelize]
public class SceneModelGraphTest
{
	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-scenegraph-{Guid.NewGuid():N}{extension}" );

	//Root, spine a metre up, head a metre above that.
	private static unsafe Model BuildModel()
	{
		var path = TempPath( ".mdl_c" );

		var builder = new CoreAssets.ModelBuilder();

		try
		{
			var rest = new[] { Vector3.Zero, new Vector3( 0, 0, 1 ), new Vector3( 0, 0, 2 ) };
			var names = new[] { "root", "spine", "head" };

			for ( var bone = 0; bone < names.Length; bone++ )
			{
				var matrix = new float[]
				{
					1, 0, 0, 0,
					0, 1, 0, 0,
					0, 0, 1, 0,
					-rest[bone].x, -rest[bone].y, -rest[bone].z, 1,
				};

				fixed ( float* m = matrix ) builder.AddBone( names[bone], bone - 1, m );
			}

			Assert.AreEqual( 1, builder.Save( path ) );
		}
		finally
		{
			builder.Dispose();
		}

		try
		{
			//From its bytes rather than by name: a temp directory is not one of the places a resource path is looked for.
			return Model.FromCore( CoreModel.FromBytes( File.ReadAllBytes( path ), path ) );
		}
		finally
		{
			try { File.Delete( path ); } catch ( IOException ) { }
		}
	}

	//A one-second clip holding the spine at a turn for its length.
	private static unsafe string BuildClip( Rotation turn )
	{
		var path = TempPath( ".anim_c" );

		var builder = new AnimClipBuilder();

		try
		{
			builder.SetFrameRate( 1.0f );

			builder.AddBone( "root", -1 );
			builder.AddBone( "spine", 0 );

			void Track( int bone, Transform pose )
			{
				var trs = AnimTrs.FromTransform( pose );

				builder.SetFrames( bone, new[] { trs, trs } );
			}

			Track( 0, new Transform( Vector3.Zero, Rotation.Identity, 1.0f ) );
			Track( 1, new Transform( new Vector3( 0, 0, 1 ), turn, 1.0f ) );

			Assert.IsTrue( builder.Save( path ) );
		}
		finally
		{
			builder.Dispose();
		}

		return Path.ChangeExtension( path, ".anim" ).Replace( '\\', '/' );
	}

	//A graph with one clip in it and one thing that can be set.
	private static AnimGraph BuildGraph()
	{
		var builder = AnimGraph.Builder;

		builder.BoolParameter( "moving" );

		var clip = builder.Clip( BuildClip( Rotation.Identity ),
			new AnimGraphBuilder.ClipSettings { Looping = true } );

		return AnimGraph.FromDefinition( builder.Build( clip ), "test" );
	}

	[TestMethod]
	public void ChangingTheModelKeepsTheGraphItWasPlaying()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var world = new SceneWorld();

		try
		{
			var first = BuildModel();
			var second = BuildModel();

			Assert.IsNotNull( first );
			Assert.IsNotNull( second );

			//Made with something else and given the skeleton after: the constructor swaps out a model with nothing to draw, and these are bones and nothing else.
			var scene = new SceneModel( world, Model.Error, Transform.Zero ) { Model = first };

			var graph = BuildGraph();
			Assert.IsNotNull( graph );

			scene.AnimGraph = graph;
			scene.SetAnimParameter( "moving", true );

			Assert.AreSame( graph, scene.AnimGraph );

			scene.Model = second;

			//The graph doesn't report parameters back, so only the graph itself is checked after the change
			Assert.AreSame( graph, scene.AnimGraph,
				"a character changes model when it draws a weapon, and losing the graph there is losing every animation it has" );
		}
		finally
		{
			world.Delete();
		}
	}

	//The graph generation keeps counting across backends so renderers notice and resend parameters
	[TestMethod]
	public void TheGraphGenerationCountsOnThroughAModelChange()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var world = new SceneWorld();

		try
		{
			var scene = new SceneModel( world, Model.Error, Transform.Zero ) { Model = BuildModel() };

			scene.AnimGraph = BuildGraph();

			var was = scene.GraphGeneration;

			scene.Model = BuildModel();

			Assert.IsTrue( scene.GraphGeneration > was,
				$"the graph was built again, so it should say so - {was} to {scene.GraphGeneration}" );
		}
		finally
		{
			world.Delete();
		}
	}
}
