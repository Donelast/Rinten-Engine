using System;
using System.Collections.Generic;
using System.IO;
using Rinten.Animation;

using CoreAssets = Rinten.CoreAssets;
using Rinten.CoreAnimation;

namespace EngineTests;

//A graph running end to end on a real skeleton: states, a transition, and the resulting pose
[TestClass]
public class AnimGraphRuntimeTest
{
	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-graph-{Guid.NewGuid():N}{extension}" );

	//Root, spine a metre up, head a metre above that.
	private static unsafe CoreAnimRuntime.Skeleton BuildSkeleton( string path )
	{
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

		var model = CoreAssets.ModelFile.Load( path );
		var skeleton = CoreAnimRuntime.CreateSkeleton( model );

		model.Dispose();

		Assert.IsNotNull( skeleton );

		return skeleton;
	}

	//A one-second clip that holds the spine at a given turn for its whole length, so what a pose is made of is obvious from the angle.
	private static unsafe void BuildClip( string path, Rotation turn )
	{
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
	}

	private static float SpineAngle( CoreAnimRuntime.Instance instance )
	{
		instance.ModelBone( 1, out var trs );

		return trs.ToTransform().Rotation.Angle();
	}

	//Two states, a condition, and a transition: the pose should be the first clip, then part way between, then the second.
	[TestMethod]
	public void StateMachineTransitionsBetweenClips()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var modelPath = TempPath( ".mdl_c" );
		var idlePath = TempPath( ".anim_c" );
		var walkPath = TempPath( ".anim_c" );

		var skeleton = BuildSkeleton( modelPath );
		var instance = CoreAnimRuntime.CreateInstance( skeleton );

		BuildClip( idlePath, Rotation.Identity );
		BuildClip( walkPath, Rotation.FromAxis( Vector3.Forward, 90.0f ) );

		try
		{
			//Clips are named by resource path here rather than by sequence, because there is no model definition
			//in a temp directory - the runtime tries the model's own list first and falls back to this.
			var builder = AnimGraph.Builder;

			var moving = builder.BoolParameter( "moving" );

			var machine = builder.StateMachine();

			var idle = machine.AddState( builder.Clip( Source( idlePath ),
				new AnimGraphBuilder.ClipSettings { Looping = true } ) );

			var walk = machine.AddState( builder.Clip( Source( walkPath ),
				new AnimGraphBuilder.ClipSettings { Looping = true } ) );

			machine.AddTransition( idle, walk,
				machine.CreateTransition( walk, new AnimGraphBuilder.TransitionSettings { Duration = 1.0f } ),
				new AnimGraphBuilder.StateMachineBuilder.TransitionRouteSettings { Condition = moving } );

			var graph = AnimGraph.FromDefinition( builder.Build( machine ), "test" );
			Assert.IsNotNull( graph );

			var running = new AnimGraphInstance( graph, instance, skeleton, null );
			Assert.IsTrue( running.IsValid, "the graph should have something to play" );

			//Standing still: the first state, which holds the spine straight.
			running.Update( 0.1f );
			Assert.AreEqual( 0.0f, SpineAngle( instance ), 1.0f, "the idle clip does not turn the spine" );

			//Told to move, and half a second into a second-long transition.
			running.SetBool( "moving", true );

			running.Update( 0.0f );
			running.Update( 0.5f );

			var midway = SpineAngle( instance );

			Assert.IsTrue( midway is > 20.0f and < 70.0f,
				$"halfway through the transition the spine should be part way round - got {midway}" );

			//And once it is over, the second state on its own.
			running.Update( 0.6f );

			Assert.AreEqual( 90.0f, SpineAngle( instance ), 1.0f, "the walk clip turns it a quarter" );

			//Nothing was left borrowed: a graph that leaks a pose a frame runs out of them, and the count is what says so.
			var poses = 0;
			var held = new List<int>();

			for ( var i = 0; i < 64; i++ )
			{
				var pose = CoreAnimRuntime.PoseAlloc( instance );
				if ( pose < 0 ) break;

				held.Add( pose );
				poses++;
			}

			foreach ( var pose in held ) CoreAnimRuntime.PoseFree( instance, pose );

			Assert.IsTrue( poses >= 64, "poses should be handed back after every frame" );
		}
		finally
		{

			File.Delete( modelPath );
			File.Delete( idlePath );
			File.Delete( walkPath );
		}
	}

	//A blend space with two poses either side of a value, blended by where that value falls between them.
	[TestMethod]
	public void BlendSpaceBlendsBetweenItsPoses()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var modelPath = TempPath( ".mdl_c" );
		var straightPath = TempPath( ".anim_c" );
		var turnedPath = TempPath( ".anim_c" );

		var skeleton = BuildSkeleton( modelPath );
		var instance = CoreAnimRuntime.CreateInstance( skeleton );

		BuildClip( straightPath, Rotation.Identity );
		BuildClip( turnedPath, Rotation.FromAxis( Vector3.Forward, 90.0f ) );

		try
		{
			var builder = AnimGraph.Builder;

			var amount = builder.FloatParameter( "lean", 0.0f, 1.0f );

			var space = builder.BlendSpace( amount,
				new[]
				{
					builder.Clip( Source( straightPath ), new AnimGraphBuilder.ClipSettings { Looping = true } ),
					builder.Clip( Source( turnedPath ), new AnimGraphBuilder.ClipSettings { Looping = true } ),
				},
				new[] { 0.0f, 1.0f } );

			var graph = AnimGraph.FromDefinition( builder.Build( space ), "blendspace" );
			var running = new AnimGraphInstance( graph, instance, skeleton, null );

			Assert.IsTrue( running.IsValid );

			running.SetFloat( "lean", 0.0f );
			running.Update( 0.1f );
			Assert.AreEqual( 0.0f, SpineAngle( instance ), 1.0f );

			running.SetFloat( "lean", 1.0f );
			running.Update( 0.1f );
			Assert.AreEqual( 90.0f, SpineAngle( instance ), 1.0f );

			//And between them, which is the whole point of a blend space.
			running.SetFloat( "lean", 0.5f );
			running.Update( 0.1f );

			var half = SpineAngle( instance );
			Assert.IsTrue( half is > 30.0f and < 60.0f, $"halfway across should be halfway round - got {half}" );

			//Outside the ends is the end, not an extrapolation past it.
			running.SetFloat( "lean", 5.0f );
			running.Update( 0.1f );
			Assert.AreEqual( 90.0f, SpineAngle( instance ), 1.0f );
		}
		finally
		{

			File.Delete( modelPath );
			File.Delete( straightPath );
			File.Delete( turnedPath );
		}
	}

	//The clip path a graph node names: the source half, because that is what a project writes and what the runtime looks for.
	private static string Source( string compiled ) => Path.ChangeExtension( compiled, ".anim" ).Replace( '\\', '/' );

	//A layer masked by a weighted mask affects only the weighted bones
	[TestMethod]
	public void AMaskWithItsOwnWeightsHoldsALayerToPartOfTheSkeleton()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var modelPath = TempPath( ".mdl_c" );
		var straightPath = TempPath( ".anim_c" );
		var turnedPath = TempPath( ".anim_c" );

		var skeleton = BuildSkeleton( modelPath );
		var instance = CoreAnimRuntime.CreateInstance( skeleton );

		BuildClip( straightPath, Rotation.Identity );
		BuildClip( turnedPath, Rotation.FromAxis( Vector3.Forward, 90.0f ) );

		try
		{
			float Turn( IEnumerable<(string Bone, float Weight)> weights )
			{
				var builder = AnimGraph.Builder;

				var layers = builder.Layers(
					builder.Clip( Source( straightPath ), new AnimGraphBuilder.ClipSettings { Looping = true } ) );

				layers.AddLayer(
					builder.Clip( Source( turnedPath ), new AnimGraphBuilder.ClipSettings { Looping = true } ),
					new AnimGraphBuilder.LayerSettings
					{
						BoneMask = builder.BoneMask( "Only_Spine", weights ),
					} );

				var graph = AnimGraph.FromDefinition( builder.Build( layers.Node ), "masked" );

				var running = new AnimGraphInstance( graph, instance, skeleton, null );
				Assert.IsTrue( running.IsValid );

				running.Update( 0.1f );

				return SpineAngle( instance );
			}

			//The spine is named, so the layer reaches it and it turns the whole quarter the layer's clip asks for.
			Assert.AreEqual( 90.0f, Turn( new[] { ("spine", 1.0f) } ), 1.0f,
				"a mask naming the spine should let the layer turn it" );

			//The head is named and the spine is not, so the layer stops above it and the spine stays where the base pose left it.
			Assert.AreEqual( 0.0f, Turn( new[] { ("head", 1.0f) } ), 1.0f,
				"a mask that skips the spine should leave it alone" );

			//And a mask of nothing at all - their _empty_anim_weightlist - keeps the layer off the whole skeleton rather than putting it everywhere.
			Assert.AreEqual( 0.0f, Turn( Array.Empty<(string, float)>() ), 1.0f,
				"an empty mask should mask everything out" );
		}
		finally
		{

			File.Delete( modelPath );
			File.Delete( straightPath );
			File.Delete( turnedPath );
		}
	}
}
