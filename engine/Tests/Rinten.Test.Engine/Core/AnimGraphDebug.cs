using System;
using System.IO;
using Rinten.Animation;

using CoreAssets = Rinten.CoreAssets;
using Rinten.CoreAnimation;

namespace EngineTests;

//Watching a graph run, which is what the editor draws with. The interesting property is that it says which part of the graph is actually
//playing - a state machine reaches one state a frame, and a node nobody asked should not look live because it was live a minute ago.
[TestClass]
public class AnimGraphDebugTest
{
	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-debug-{Guid.NewGuid():N}{extension}" );

	private static string Source( string compiled ) => Path.ChangeExtension( compiled, ".anim" ).Replace( '\\', '/' );

	private static unsafe CoreAnimRuntime.Skeleton BuildSkeleton( string path )
	{
		var builder = new CoreAssets.ModelBuilder();

		try
		{
			var rest = new[] { Vector3.Zero, new Vector3( 0, 0, 1 ) };
			var names = new[] { "root", "spine" };

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

	//A second of the spine sliding a metre along, with a mark in the middle.
	private static unsafe void BuildClip( string path, float from, float to, bool marked = false )
	{
		var builder = new AnimClipBuilder();

		try
		{
			builder.SetFrameRate( 1.0f );

			builder.AddBone( "root", -1 );
			builder.AddBone( "spine", 0 );

			builder.SetFrames( 0, new[]
			{
				AnimTrs.FromTransform( new Transform( Vector3.Zero, Rotation.Identity, 1.0f ) ),
				AnimTrs.FromTransform( new Transform( Vector3.Zero, Rotation.Identity, 1.0f ) )
			} );

			builder.SetFrames( 1, new[]
			{
				AnimTrs.FromTransform( new Transform( new Vector3( from, 0, 1 ), Rotation.Identity, 1.0f ) ),
				AnimTrs.FromTransform( new Transform( new Vector3( to, 0, 1 ), Rotation.Identity, 1.0f ) )
			} );

			if ( marked ) builder.AddEvent( "step", 2, 0.25f, 0.75f, 0 );

			Assert.IsTrue( builder.Save( path ) );
		}
		finally
		{
			builder.Dispose();
		}
	}

	private static float SpineX( CoreAnimRuntime.Instance instance )
	{
		instance.ModelBone( 1, out var trs );

		return trs.ToTransform().Position.x;
	}

	private static bool Ready()
	{
		if ( CoreNative.Available ) return true;

		Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
		return false;
	}

	//A watched graph says which nodes were reached, what they play, and what the value nodes worked out.
	[TestMethod]
	public void WatchingAGraphSaysWhatIsPlaying()
	{
		if ( !Ready() ) return;

		var model = TempPath( ".mdl_c" );
		var walk = TempPath( ".anim_c" );
		var idle = TempPath( ".anim_c" );

		var skeleton = BuildSkeleton( model );
		var instance = CoreAnimRuntime.CreateInstance( skeleton );

		BuildClip( walk, 0.0f, 1.0f, marked: true );
		BuildClip( idle, 0.0f, 0.0f );

		try
		{
			var builder = AnimGraph.Builder;

			var moving = builder.BoolParameter( "moving" );

			var machine = builder.StateMachine();

			//The clips as well as the states: a state is asked about by the node that poses it, which is what the editor maps its states onto.
			var standingClip = builder.Clip( Source( idle ), new AnimGraphBuilder.ClipSettings { Looping = true } );
			var walkingClip = builder.Clip( Source( walk ), new AnimGraphBuilder.ClipSettings { Looping = true } );

			var standing = machine.AddState( standingClip );
			var walking = machine.AddState( walkingClip );

			machine.AddTransition( standing, walking, machine.CreateTransition( walking ),
				new AnimGraphBuilder.StateMachineBuilder.TransitionRouteSettings { Condition = moving } );

			var graph = AnimGraph.FromDefinition( builder.Build( machine ), "watched" );

			var running = new AnimGraphInstance( graph, instance, skeleton, null );
			Assert.IsTrue( running.IsValid );

			var watching = running.Watch();

			running.Update( 0.1f );

			//The state it is in ran; the one it is not in did not, whatever it might have done on some other frame.
			Assert.IsTrue( watching.TryGetInfo( standingClip.GetRuntimeIndex(), out var standingInfo ) );
			Assert.IsTrue( standingInfo.IsActive, "the state the machine is in should be live" );
			Assert.AreEqual( AnimGraphNodeValueType.Pose, standingInfo.ValueType );

			Assert.IsFalse( watching.TryGetInfo( walkingClip.GetRuntimeIndex(), out _ ),
				"a state nothing has been in has nothing to say" );

			//What the game set, read back off the node that reads it.
			running.SetBool( "moving", true );
			running.Update( 0.0f );
			running.Update( 0.1f );

			Assert.IsTrue( watching.TryGetValue( moving.GetRuntimeIndex(), out var value ) );
			Assert.IsTrue( value.Bool, "the parameter node should have read what was set" );

			Assert.IsTrue( watching.TryGetInfo( walkingClip.GetRuntimeIndex(), out var walkingInfo ) );
			Assert.IsTrue( walkingInfo.IsActive, "and the state it moved to should be live now" );

			Assert.AreEqual( 1.0f, walkingInfo.Duration, 0.01f, "the clip it plays is a second long" );
			Assert.AreEqual( 1, walkingInfo.SyncEventCount, "the mark on that clip should be counted" );

			Assert.IsTrue( watching.TryGetSyncEvent( walkingClip.GetRuntimeIndex(), 0, out var marked ) );
			Assert.AreEqual( 0.25f, marked.StartTime, 0.01f, "marks are fractions of the clip" );
			Assert.AreEqual( 0.5f, marked.Duration, 0.01f );

			//And which state the machine is in, which is what draws the machine.
			Assert.IsTrue( watching.TryGetState( machine.Node.GetRuntimeIndex(), out var state, out _, out _, out _ ) );
			Assert.AreEqual( 1, state );
		}
		finally
		{

			File.Delete( model );
			File.Delete( walk );
			File.Delete( idle );
		}
	}

	//A recorded graph can be stepped back through, and what comes back is the pose that was on screen rather than what the graph would do now.
	[TestMethod]
	public void RecordingStepsBackThroughWhatHappened()
	{
		if ( !Ready() ) return;

		var model = TempPath( ".mdl_c" );
		var clip = TempPath( ".anim_c" );

		var skeleton = BuildSkeleton( model );
		var instance = CoreAnimRuntime.CreateInstance( skeleton );

		BuildClip( clip, 0.0f, 1.0f );

		try
		{
			var builder = AnimGraph.Builder;

			var graph = AnimGraph.FromDefinition(
				builder.Build( builder.Clip( Source( clip ), new AnimGraphBuilder.ClipSettings { Looping = true } ) ),
				"recorded" );

			var running = new AnimGraphInstance( graph, instance, skeleton, null );
			Assert.IsTrue( running.IsValid );

			var recording = running.Record();

			for ( var i = 0; i < 5; i++ ) running.Update( 0.1f, new Transform( new Vector3( i, 0, 0 ) ) );

			running.StopRecording();

			Assert.AreEqual( 5, recording.Count, "every update should have been kept" );
			Assert.AreEqual( 0.5f, SpineX( instance ), 0.02f, "half a second in, half way along" );

			Assert.IsTrue( recording.StartPlayback() );

			//The first update again: the clip has moved on, and what comes back is where it was, not where it is.
			Assert.IsTrue( running.GoTo( 0 ) );
			Assert.AreEqual( 0.1f, SpineX( instance ), 0.02f );

			Assert.IsTrue( running.GoTo( 3 ) );
			Assert.AreEqual( 0.4f, SpineX( instance ), 0.02f );

			Assert.IsTrue( recording.TryGetInfo( 3, out var info ) );
			Assert.AreEqual( AnimGraphRecordedUpdateType.TimeStep, info.Type );
			Assert.AreEqual( 0.1f, info.DeltaTime, 0.001f );

			//Where the character was standing at the time, which a walk needs or every recorded frame is at the origin.
			Assert.AreEqual( 3.0f, recording.WorldOf( 3 ).Position.x, 0.001f );

			Assert.IsFalse( running.GoTo( 99 ), "there is no such update" );
		}
		finally
		{

			File.Delete( model );
			File.Delete( clip );
		}
	}
}
