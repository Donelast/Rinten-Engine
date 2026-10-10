using System;
using System.Collections.Generic;
using System.IO;
using Rinten.Animation;

using CoreAssets = Rinten.CoreAssets;
using Rinten.CoreAnimation;

namespace EngineTests;

//Pose post-effects and transition start points, measured end to end on a real skeleton
[TestClass]
public class AnimGraphEffectsTest
{
	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-effects-{Guid.NewGuid():N}{extension}" );

	private static string Source( string compiled ) => Path.ChangeExtension( compiled, ".anim" ).Replace( '\\', '/' );

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

	//A one-second clip that turns the spine from one angle to another over its length, so where the playhead is can be read off the pose.
	private static unsafe void BuildRamp( string path, float from, float to, Action<AnimClipBuilder> mark = null )
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
				AnimTrs.FromTransform( new Transform( new Vector3( 0, 0, 1 ), Rotation.FromAxis( Vector3.Forward, from ), 1.0f ) ),
				AnimTrs.FromTransform( new Transform( new Vector3( 0, 0, 1 ), Rotation.FromAxis( Vector3.Forward, to ), 1.0f ) )
			} );

			mark?.Invoke( builder );

			Assert.IsTrue( builder.Save( path ) );
		}
		finally
		{
			builder.Dispose();
		}
	}

	//Like the turn clip but slides the spine, so phase reads linearly instead of through slerp
	private static unsafe void BuildSlide( string path, float from, float to, Action<AnimClipBuilder> mark = null )
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

			mark?.Invoke( builder );

			Assert.IsTrue( builder.Save( path ) );
		}
		finally
		{
			builder.Dispose();
		}
	}

	//A clip that carries the whole model along - the root moves, which is what a walk cycle is measured by.
	private static unsafe void BuildTravel( string path, float distance )
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
				AnimTrs.FromTransform( new Transform( new Vector3( distance, 0, 0 ), Rotation.Identity, 1.0f ) )
			} );

			builder.SetFrames( 1, new[]
			{
				AnimTrs.FromTransform( new Transform( new Vector3( 0, 0, 1 ), Rotation.Identity, 1.0f ) ),
				AnimTrs.FromTransform( new Transform( new Vector3( 0, 0, 1 ), Rotation.Identity, 1.0f ) )
			} );

			Assert.IsTrue( builder.Save( path ) );
		}
		finally
		{
			builder.Dispose();
		}
	}

	private static Transform Bone( CoreAnimRuntime.Instance instance, int bone )
	{
		instance.ModelBone( bone, out var trs );

		return trs.ToTransform();
	}

	private static float SpineAngle( CoreAnimRuntime.Instance instance ) => Bone( instance, 1 ).Rotation.Angle();

	//Somewhere to run a graph, cleaned up whatever happens.
	private sealed class Rig : IDisposable
	{
		public readonly CoreAnimRuntime.Skeleton Skeleton;
		public readonly CoreAnimRuntime.Instance Instance;

		private readonly string model;

		public Rig()
		{
			model = TempPath( ".mdl_c" );

			Skeleton = BuildSkeleton( model );
			Instance = CoreAnimRuntime.CreateInstance( Skeleton );
		}

		public AnimGraphInstance Play( AnimGraphBuilder builder, AnimGraphBuilder.Node root, string name )
		{
			var graph = AnimGraph.FromDefinition( builder.Build( root ), name );
			Assert.IsNotNull( graph );

			var running = new AnimGraphInstance( graph, Instance, Skeleton, null );
			Assert.IsTrue( running.IsValid, $"{name}: the graph should have something to play" );

			return running;
		}

		public void Dispose()
		{

			File.Delete( model );
		}
	}

	private static bool Ready()
	{
		if ( CoreNative.Available ) return true;

		Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
		return false;
	}

	//Two bones bent so the third lands on a target: what an arm reaching for something is.
	[TestMethod]
	public void TwoBoneIKPutsTheEndOnTheTarget()
	{
		if ( !Ready() ) return;

		var clip = TempPath( ".anim_c" );
		BuildRamp( clip, 0.0f, 0.0f );

		using var rig = new Rig();

		try
		{
			var builder = AnimGraph.Builder;

			var target = builder.TargetParameter( "hand" );

			var pose = builder.Clip( Source( clip ), new AnimGraphBuilder.ClipSettings { Looping = true } );
			var ik = builder.TwoBoneIK( pose, "head", target );

			var running = rig.Play( builder, ik, "ik" );

			//Nothing to reach for: the animation is left alone, and the head is where the skeleton rests.
			running.Update( 0.1f );
			Assert.AreEqual( 2.0f, Bone( rig.Instance, 2 ).Position.z, 0.01f, "with no target the pose is untouched" );

			//A target the arm can get to, in the model's own space. The limb is two metres
			//long straightened out, so anything nearer than that should be reached exactly.
			running.SetTarget( "hand", new Transform( new Vector3( 1.0f, 0.0f, 1.0f ), Rotation.Identity, 1.0f ) );
			running.Update( 0.1f );

			var reached = Bone( rig.Instance, 2 ).Position;

			Assert.AreEqual( 1.0f, reached.x, 0.02f, "the end should land on the target" );
			Assert.AreEqual( 1.0f, reached.z, 0.02f, "the end should land on the target" );

			//And one it cannot: the limb straightens towards it rather than stretching or giving up.
			running.SetTarget( "hand", new Transform( new Vector3( 10.0f, 0.0f, 0.0f ), Rotation.Identity, 1.0f ) );
			running.Update( 0.1f );

			var strained = Bone( rig.Instance, 2 ).Position;

			Assert.AreEqual( 2.0f, strained.Length, 0.02f, "an unreachable target should not stretch the limb" );
			Assert.IsTrue( strained.x > 1.9f, $"it should point at what it cannot reach - got {strained}" );
		}
		finally
		{
			File.Delete( clip );
		}
	}

	//IK blend modes differ halfway in: Pose shortens the limb, Effector keeps it intact
	[TestMethod]
	public void TheTwoIkBlendModesAreDifferent()
	{
		if ( !Ready() ) return;

		var clip = TempPath( ".anim_c" );
		BuildRamp( clip, 0.0f, 0.0f );

		try
		{
			var reached = new Dictionary<AnimGraphBuilder.IKBlendMode, Vector3>();
			var lengths = new Dictionary<AnimGraphBuilder.IKBlendMode, float>();

			foreach ( var mode in new[]
			{
				AnimGraphBuilder.IKBlendMode.Effector,
				AnimGraphBuilder.IKBlendMode.Pose,
			} )
			{
				using var rig = new Rig();

				var builder = AnimGraph.Builder;
				var target = builder.TargetParameter( "hand" );

				var pose = builder.Clip( Source( clip ), new AnimGraphBuilder.ClipSettings { Looping = true } );

				//Long enough to blend that a single step lands part way in, which is the only place the two modes can disagree.
				var ik = builder.Ik( pose, target, new AnimGraphBuilder.IkSettings
				{
					BlendMode = mode,
					BlendTime = 0.4f,
				}, "head" );

				var running = rig.Play( builder, ik, $"ik {mode}" );

				running.SetTarget( "hand", new Transform( new Vector3( 1.0f, 0.0f, 1.0f ), Rotation.Identity, 1.0f ) );
				running.Update( 0.1f );

				var tip = Bone( rig.Instance, 2 ).Position;
				var joint = Bone( rig.Instance, 1 ).Position;

				//The joint, not the tip. The tip lands between where it rested and the target either way -
				//that is what a weight means - and the modes differ in what the limb does to get it there.
				reached[mode] = joint;
				lengths[mode] = ( tip - joint ).Length;
			}

			var apart = ( reached[AnimGraphBuilder.IKBlendMode.Effector]
				- reached[AnimGraphBuilder.IKBlendMode.Pose] ).Length;

			Assert.IsTrue( apart > 0.01f,
				$"the joint should not be in the same place part way in - effector {reached[AnimGraphBuilder.IKBlendMode.Effector]}, "
				+ $"pose {reached[AnimGraphBuilder.IKBlendMode.Pose]}" );

			//The limb is two bones a metre each, so the lower one is a metre however far the blend has got - under an effector blend.
			Assert.AreEqual( 1.0f, lengths[AnimGraphBuilder.IKBlendMode.Effector], 0.02f,
				"an effector blend should leave the limb its own length" );

			Assert.IsTrue( lengths[AnimGraphBuilder.IKBlendMode.Pose]
				< lengths[AnimGraphBuilder.IKBlendMode.Effector] - 0.01f,
				$"a pose blend averages two poses and shortens it - got {lengths[AnimGraphBuilder.IKBlendMode.Pose]}" );
		}
		finally
		{
			File.Delete( clip );
		}
	}

	//The game's movement instead of the animation's: the root goes where the velocity says, not where the clip does.
	[TestMethod]
	public void RootMotionOverrideCarriesTheRoot()
	{
		if ( !Ready() ) return;

		var clip = TempPath( ".anim_c" );
		BuildRamp( clip, 0.0f, 0.0f );

		using var rig = new Rig();

		try
		{
			var builder = AnimGraph.Builder;

			var velocity = builder.VectorParameter( "velocity" );

			var pose = builder.Clip( Source( clip ), new AnimGraphBuilder.ClipSettings { Looping = true } );

			var moved = builder.RootMotionOverride( pose, new AnimGraphBuilder.RootMotionOverrideSettings
			{
				MovingVelocity = velocity,
				Flags = AnimGraphBuilder.RootMotionOverrideFlags.MoveX
					| AnimGraphBuilder.RootMotionOverrideFlags.MoveY,
			} );

			var running = rig.Play( builder, moved, "root motion" );

			running.SetVector( "velocity", new Vector3( 2.0f, 0.0f, 0.0f ) );

			for ( var i = 0; i < 10; i++ ) running.Update( 0.1f );

			var at = Bone( rig.Instance, 0 ).Position;

			//A second at two metres a second, along the axis the graph said it drives and no other.
			Assert.AreEqual( 2.0f, at.x, 0.05f, "the root should have been carried along x" );
			Assert.AreEqual( 0.0f, at.z, 0.01f, "and not along an axis the flags did not name" );
		}
		finally
		{
			File.Delete( clip );
		}
	}

	//Played in the time asked for rather than its own.
	[TestMethod]
	public void DurationScalePlaysInTheTimeAskedFor()
	{
		if ( !Ready() ) return;

		var clip = TempPath( ".anim_c" );
		BuildRamp( clip, 0.0f, 90.0f );

		using var rig = new Rig();

		try
		{
			var builder = AnimGraph.Builder;

			var pose = builder.Clip( Source( clip ), new AnimGraphBuilder.ClipSettings { Looping = true } );

			//Half the clip's own length, so a quarter of a second in should be half way through it.
			var running = rig.Play( builder, builder.DurationScale( pose, 0.5f ), "duration" );

			running.Update( 0.25f );

			Assert.AreEqual( 45.0f, SpineAngle( rig.Instance ), 3.0f,
				"a second-long clip played in half a second is halfway through after a quarter" );
		}
		finally
		{
			File.Delete( clip );
		}
	}

	//A transition that starts the state it enters where the one it leaves had got to, rather than at the beginning.
	[TestMethod]
	public void TransitionMatchesTheTimeItCameFrom()
	{
		if ( !Ready() ) return;

		var from = TempPath( ".anim_c" );
		var to = TempPath( ".anim_c" );

		//Two clips of the same length going different distances, so where the second one starts can be read straight off the angle.
		BuildRamp( from, 0.0f, 90.0f );
		BuildRamp( to, 0.0f, 180.0f );

		using var rig = new Rig();

		try
		{
			AnimGraphInstance Build( AnimGraphBuilder.TransitionTimeMode mode )
			{
				var builder = AnimGraph.Builder;

				var go = builder.BoolParameter( "go" );
				var machine = builder.StateMachine();

				var first = machine.AddState( builder.Clip( Source( from ),
					new AnimGraphBuilder.ClipSettings { Looping = true } ) );

				var second = machine.AddState( builder.Clip( Source( to ),
					new AnimGraphBuilder.ClipSettings { Looping = true } ) );

				//Instant, so what is measured is where the second clip starts rather than a blend of the two.
				machine.AddTransition( first, second,
					machine.CreateTransition( second, new AnimGraphBuilder.TransitionSettings { TimeMode = mode } ),
					new AnimGraphBuilder.StateMachineBuilder.TransitionRouteSettings { Condition = go } );

				return rig.Play( builder, machine, $"transition {mode}" );
			}

			//Without matching, the entered state starts at its beginning.
			var plain = Build( AnimGraphBuilder.TransitionTimeMode.None );

			plain.Update( 0.5f );
			plain.SetBool( "go", true );
			plain.Update( 0.0f );

			Assert.AreEqual( 0.0f, SpineAngle( rig.Instance ), 3.0f, "an unmatched transition starts the state again" );

			//With it, half way through the first is half way through the second - which on a clip that goes twice as far is twice the angle.
			var matched = Build( AnimGraphBuilder.TransitionTimeMode.MatchSourceTime );

			matched.Update( 0.5f );
			matched.SetBool( "go", true );
			matched.Update( 0.0f );

			Assert.AreEqual( 90.0f, SpineAngle( rig.Instance ), 5.0f,
				"a matched transition starts the state where the one it left had got to" );
		}
		finally
		{
			File.Delete( from );
			File.Delete( to );
		}
	}

	//The same, by what the artist marked rather than by the clock: two clips are in step when the same foot is down, whatever their lengths.
	[TestMethod]
	public void TransitionMatchesTheMarksInstead()
	{
		if ( !Ready() ) return;

		var from = TempPath( ".anim_c" );
		var to = TempPath( ".anim_c" );

		//The mark is at the start of one clip and three quarters of the way through the
		//other, so matching by mark and matching by time cannot be confused for each other.
		BuildSlide( from, 0.0f, 1.0f,
			builder => builder.AddEvent( "step", 2, 0.0f, 0.1f, 0 ) );

		BuildSlide( to, 0.0f, 1.0f,
			builder => builder.AddEvent( "step", 2, 0.75f, 0.85f, 0 ) );

		using var rig = new Rig();

		try
		{
			var builder = AnimGraph.Builder;

			var go = builder.BoolParameter( "go" );
			var machine = builder.StateMachine();

			var first = machine.AddState( builder.Clip( Source( from ),
				new AnimGraphBuilder.ClipSettings { Looping = true } ) );

			var second = machine.AddState( builder.Clip( Source( to ),
				new AnimGraphBuilder.ClipSettings { Looping = true } ) );

			machine.AddTransition( first, second,
				machine.CreateTransition( second, new AnimGraphBuilder.TransitionSettings
				{
					SyncMatch = AnimGraphBuilder.TransitionSyncMatch.EventId
						| AnimGraphBuilder.TransitionSyncMatch.EventPercentage,
				} ),
				new AnimGraphBuilder.StateMachineBuilder.TransitionRouteSettings { Condition = go } );

			var running = rig.Play( builder, machine, "sync" );

			//Left half way through the mark, so the state entered should be half way through
			//its own copy of it - four fifths of the way along a clip that slides a metre.
			running.Update( 0.05f );
			running.SetBool( "go", true );
			running.Update( 0.0f );

			Assert.AreEqual( 0.8f, Bone( rig.Instance, 1 ).Position.x, 0.02f,
				"the entered state should have gone to its own copy of the mark" );
		}
		finally
		{
			File.Delete( from );
			File.Delete( to );
		}
	}

	//A graph reacting to its own animation: a transition that waits for the clip it is playing to say a foot went down.
	[TestMethod]
	public void FootstepOnAClipDrivesATransition()
	{
		if ( !Ready() ) return;

		var walk = TempPath( ".anim_c" );
		var stop = TempPath( ".anim_c" );

		//The foot goes down half way through the clip, and nowhere else.
		BuildRamp( walk, 0.0f, 0.0f,
			builder => builder.AddEvent( "step", 1, 0.5f, 0.6f, 0 ) );

		BuildRamp( stop, 90.0f, 90.0f );

		using var rig = new Rig();

		try
		{
			var builder = AnimGraph.Builder;

			var machine = builder.StateMachine();

			var moving = machine.AddState( builder.Clip( Source( walk ),
				new AnimGraphBuilder.ClipSettings { Looping = true } ) );

			var stopped = machine.AddState( builder.Clip( Source( stop ),
				new AnimGraphBuilder.ClipSettings { Looping = true } ) );

			machine.AddTransition( moving, stopped, machine.CreateTransition( stopped ),
				new AnimGraphBuilder.StateMachineBuilder.TransitionRouteSettings
				{
					Condition = builder.FootstepCondition( AnimGraphBuilder.FootPhase.LeftFootDown ),
				} );

			var running = rig.Play( builder, machine, "footstep" );

			//Before the mark: nothing has been said, so nothing has changed.
			running.Update( 0.1f );
			running.Update( 0.1f );

			Assert.AreEqual( 0.0f, SpineAngle( rig.Instance ), 1.0f, "the clip has not marked anything yet" );

			//Over it, and then the frame after - which is when a graph can act on what its own animation said, because it decides before it poses.
			running.Update( 0.35f );
			running.Update( 0.0f );

			Assert.AreEqual( 90.0f, SpineAngle( rig.Instance ), 1.0f,
				"the footstep the clip marked should have taken the transition" );
		}
		finally
		{
			File.Delete( walk );
			File.Delete( stop );
		}
	}

	//A layer weighed by the states inside it: one state is a whole animation, the next is nothing at all, and the layer itself says neither.
	[TestMethod]
	public void StateMachineLayerIsWeighedByItsStates()
	{
		if ( !Ready() ) return;

		var under = TempPath( ".anim_c" );
		var over = TempPath( ".anim_c" );

		BuildRamp( under, 0.0f, 0.0f );
		BuildRamp( over, 90.0f, 90.0f );

		using var rig = new Rig();

		try
		{
			var builder = AnimGraph.Builder;

			var go = builder.BoolParameter( "go" );
			var machine = builder.StateMachine();

			//The state the machine starts in says its layer weighs nothing; the one it moves to says it weighs everything.
			var off = machine.AddState( builder.Clip( Source( over ),
					new AnimGraphBuilder.ClipSettings { Looping = true } ),
				new AnimGraphBuilder.StateMachineBuilder.StateSettings
				{
					LayerWeight = builder.Constant( 0.0f ),
				} );

			var on = machine.AddState( builder.Clip( Source( over ),
					new AnimGraphBuilder.ClipSettings { Looping = true } ),
				new AnimGraphBuilder.StateMachineBuilder.StateSettings
				{
					LayerWeight = builder.Constant( 1.0f ),
				} );

			machine.AddTransition( off, on, machine.CreateTransition( on ),
				new AnimGraphBuilder.StateMachineBuilder.TransitionRouteSettings { Condition = go } );

			var layers = builder.Layers( builder.Clip( Source( under ),
				new AnimGraphBuilder.ClipSettings { Looping = true } ) );

			layers.AddStateMachineLayer( machine );

			var running = rig.Play( builder, layers, "state layer" );

			running.Update( 0.1f );
			Assert.AreEqual( 0.0f, SpineAngle( rig.Instance ), 1.0f,
				"a state that says its layer weighs nothing should not be laid on" );

			running.SetBool( "go", true );
			running.Update( 0.0f );
			running.Update( 0.1f );

			Assert.AreEqual( 90.0f, SpineAngle( rig.Instance ), 1.0f,
				"and one that says it weighs everything should be" );
		}
		finally
		{
			File.Delete( under );
			File.Delete( over );
		}
	}

	//Clips spread along an axis nobody wrote down: each sits at the speed its own root motion travels at.
	[TestMethod]
	public void VelocityBlendSpreadsClipsByTheirOwnSpeed()
	{
		if ( !Ready() ) return;

		var slow = TempPath( ".anim_c" );
		var fast = TempPath( ".anim_c" );

		BuildTravel( slow, 1.0f );
		BuildTravel( fast, 3.0f );

		using var rig = new Rig();

		try
		{
			AnimGraphInstance Build( float speed )
			{
				var builder = AnimGraph.Builder;

				var wanted = builder.Constant( speed );

				var blend = builder.VelocityBlend( wanted,
					builder.Clip( Source( slow ), new AnimGraphBuilder.ClipSettings { Looping = true } ),
					builder.Clip( Source( fast ), new AnimGraphBuilder.ClipSettings { Looping = true } ) );

				return rig.Play( builder, blend, $"velocity {speed}" );
			}

			//A quarter of a second at each speed: the slower clip has carried the root a quarter of a metre, the faster one three quarters.
			Build( 1.0f ).Update( 0.25f );
			Assert.AreEqual( 0.25f, Bone( rig.Instance, 0 ).Position.x, 0.03f, "asked for what the slow clip does" );

			Build( 3.0f ).Update( 0.25f );
			Assert.AreEqual( 0.75f, Bone( rig.Instance, 0 ).Position.x, 0.03f, "asked for what the fast clip does" );

			//Halfway between the two clips' own speeds, which is halfway between where they carry it.
			Build( 2.0f ).Update( 0.25f );
			Assert.AreEqual( 0.5f, Bone( rig.Instance, 0 ).Position.x, 0.05f, "between them is a blend of them" );

			//Beyond the fastest is the fastest, not an extrapolation.
			Build( 20.0f ).Update( 0.25f );
			Assert.AreEqual( 0.75f, Bone( rig.Instance, 0 ).Position.x, 0.03f );
		}
		finally
		{
			File.Delete( slow );
			File.Delete( fast );
		}
	}

	//One pose of several by a number, picked when the node is entered and held after that.
	[TestMethod]
	public void ParameterizedSelectorPicksByTheWholeNumber()
	{
		if ( !Ready() ) return;

		var first = TempPath( ".anim_c" );
		var second = TempPath( ".anim_c" );

		BuildRamp( first, 0.0f, 0.0f );
		BuildRamp( second, 90.0f, 90.0f );

		using var rig = new Rig();

		try
		{
			AnimGraphInstance Build()
			{
				var builder = AnimGraph.Builder;

				var which = builder.FloatParameter( "which", 0.0f, 8.0f );

				var selector = builder.ParameterizedSelector( which, new[]
				{
					builder.Clip( Source( first ), new AnimGraphBuilder.ClipSettings { Looping = true } ),
					builder.Clip( Source( second ), new AnimGraphBuilder.ClipSettings { Looping = true } ),
				} );

				return rig.Play( builder, selector, "selector" );
			}

			var running = Build();

			running.SetFloat( "which", 0.0f );
			running.Update( 0.1f );
			Assert.AreEqual( 0.0f, SpineAngle( rig.Instance ), 1.0f, "zero picks the first" );

			running = Build();

			running.SetFloat( "which", 1.6f );
			running.Update( 0.1f );
			Assert.AreEqual( 90.0f, SpineAngle( rig.Instance ), 1.0f, "the whole part picks, so 1.6 is one" );

			running = Build();

			running.SetFloat( "which", 2.0f );
			running.Update( 0.1f );
			Assert.AreEqual( 0.0f, SpineAngle( rig.Instance ), 1.0f, "counting past the end goes round again" );

			//Held: the same number picks the same pose, and a different one does not swap it under a character that is already playing it.
			running.SetFloat( "which", 1.0f );
			running.Update( 0.1f );
			Assert.AreEqual( 0.0f, SpineAngle( rig.Instance ), 1.0f, "picked on entry and kept" );
		}
		finally
		{
			File.Delete( first );
			File.Delete( second );
		}
	}

	//A target that is one of the model's own bones, and the numbers a graph works out from one.
	[TestMethod]
	public void BoneTargetsAreWhereThatBoneIs()
	{
		if ( !Ready() ) return;

		var straight = TempPath( ".anim_c" );
		var turned = TempPath( ".anim_c" );

		BuildRamp( straight, 0.0f, 0.0f );
		BuildRamp( turned, 90.0f, 90.0f );

		using var rig = new Rig();

		try
		{
			var builder = AnimGraph.Builder;

			//How far away the spine is, which on this skeleton is a metre up. Read as a
			//blend weight, so what the graph worked out can be seen in where a bone ended up.
			var distance = builder.TargetInfo( builder.BoneTarget( "spine" ),
				AnimGraphBuilder.TargetInfoType.Distance );

			var blended = builder.Blend(
				builder.Clip( Source( straight ), new AnimGraphBuilder.ClipSettings { Looping = true } ),
				builder.Clip( Source( turned ), new AnimGraphBuilder.ClipSettings { Looping = true } ),
				distance );

			var running = rig.Play( builder, blended, "bone target" );

			running.Update( 0.1f );

			Assert.AreEqual( 90.0f, SpineAngle( rig.Instance ), 1.0f,
				"a metre away is a weight of one, which is the second pose" );

			//A target nothing has been put in is not a target at the origin: the blend should read nothing at all rather than zero distance.
			var second = AnimGraph.Builder;

			var target = second.TargetParameter( "reach" );

			var known = second.Blend(
				second.Clip( Source( straight ), new AnimGraphBuilder.ClipSettings { Looping = true } ),
				second.Clip( Source( turned ), new AnimGraphBuilder.ClipSettings { Looping = true } ),
				second.IsTargetSet( target ) );

			var asking = rig.Play( second, known, "is target set" );

			asking.Update( 0.1f );
			Assert.AreEqual( 0.0f, SpineAngle( rig.Instance ), 1.0f, "nothing set is nothing to reach for" );

			asking.SetTarget( "reach", new Transform( new Vector3( 1, 0, 1 ), Rotation.Identity, 1.0f ) );
			asking.Update( 0.1f );

			Assert.AreEqual( 90.0f, SpineAngle( rig.Instance ), 1.0f, "and something set is something" );
		}
		finally
		{
			File.Delete( straight );
			File.Delete( turned );
		}
	}

	//A pose the game hands in, and the fallback while it has not.
	[TestMethod]
	public void ExternalPoseIsPlayedOnceTheGameSetsIt()
	{
		if ( !Ready() ) return;

		using var rig = new Rig();

		var builder = AnimGraph.Builder;
		var running = rig.Play( builder, builder.ExternalPose( "ragdoll" ), "external pose" );

		//Nothing in the slot: the model rests, which is what an empty slot has to look like rather than a crash.
		running.Update( 0.1f );
		Assert.AreEqual( 0.0f, SpineAngle( rig.Instance ), 1.0f );

		var turned = Rotation.FromAxis( Vector3.Forward, 90.0f );

		running.SetExternalPose( "ragdoll", new[]
		{
			new Transform( Vector3.Zero, Rotation.Identity, 1.0f ),
			new Transform( new Vector3( 0, 0, 1 ), turned, 1.0f ),
			new Transform( new Vector3( 0, 0, 1 ), Rotation.Identity, 1.0f ),
		} );

		running.Update( 0.1f );
		Assert.AreEqual( 90.0f, SpineAngle( rig.Instance ), 1.0f, "the pose the game put in should be what is played" );

		running.ClearExternalPose( "ragdoll" );
		running.Update( 0.1f );

		Assert.AreEqual( 0.0f, SpineAngle( rig.Instance ), 1.0f, "and taking it out should put it back to rest" );
	}

	//A whole graph plugged into a slot, with something to play until it is.
	[TestMethod]
	public void ExternalGraphPlaysWhatIsPluggedIntoIt()
	{
		if ( !Ready() ) return;

		var fallback = TempPath( ".anim_c" );
		var plugged = TempPath( ".anim_c" );

		BuildRamp( fallback, 0.0f, 0.0f );
		BuildRamp( plugged, 90.0f, 90.0f );

		using var rig = new Rig();

		try
		{
			var builder = AnimGraph.Builder;

			var slot = builder.ExternalGraphSlot( "upper",
				builder.Clip( Source( fallback ), new AnimGraphBuilder.ClipSettings { Looping = true } ) );

			var running = rig.Play( builder, slot, "external graph" );

			running.Update( 0.1f );
			Assert.AreEqual( 0.0f, SpineAngle( rig.Instance ), 1.0f, "an empty slot plays its fallback" );

			var inner = AnimGraph.Builder;

			running.SetExternalGraph( "upper", AnimGraph.FromDefinition(
				inner.Build( inner.Clip( Source( plugged ), new AnimGraphBuilder.ClipSettings { Looping = true } ) ),
				"plugged" ) );

			running.Update( 0.1f );
			Assert.AreEqual( 90.0f, SpineAngle( rig.Instance ), 1.0f, "and a filled one plays what was put in it" );
		}
		finally
		{
			File.Delete( fallback );
			File.Delete( plugged );
		}
	}

	//One of several, picked at random: which one is not worth asserting, that it is one of them is.
	[TestMethod]
	public void VariationPlaysOneOfWhatItWasGiven()
	{
		if ( !Ready() ) return;

		var first = TempPath( ".anim_c" );
		var second = TempPath( ".anim_c" );

		BuildRamp( first, 0.0f, 0.0f );
		BuildRamp( second, 90.0f, 90.0f );

		using var rig = new Rig();

		try
		{
			var seen = new bool[2];

			//Enough goes that both being picked is near certain, and each one a fresh graph because a variation holds what it chose.
			for ( var attempt = 0; attempt < 40; attempt++ )
			{
				var builder = AnimGraph.Builder;

				var variation = builder.Variation();

				variation.Add( builder.Clip( Source( first ),
					new AnimGraphBuilder.ClipSettings { Looping = true } ), 1 );

				variation.Add( builder.Clip( Source( second ),
					new AnimGraphBuilder.ClipSettings { Looping = true } ), 1 );

				var running = rig.Play( builder, variation, "variation" );
				running.Update( 0.1f );

				var angle = SpineAngle( rig.Instance );

				if ( angle < 1.0f ) seen[0] = true;
				else if ( angle > 89.0f ) seen[1] = true;
				else Assert.Fail( $"a variation should play one of its options, not a blend - got {angle}" );
			}

			Assert.IsTrue( seen[0] && seen[1], "forty goes at two options should have picked both" );
		}
		finally
		{
			File.Delete( first );
			File.Delete( second );
		}
	}

	//Being shot moves the character, and then stops moving it. The five parameters this reads have been set by the citizen helper
	//since long before there was a node to read them, so the failure this guards is a quiet one: everything is wired, nothing moves.
	[TestMethod]
	public void ABlowShovesTheBoneItLandsOn()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var modelPath = TempPath( ".mdl_c" );
		var skeleton = BuildSkeleton( modelPath );
		var instance = CoreAnimRuntime.CreateInstance( skeleton );

		try
		{
			var builder = AnimGraph.Builder;

			var hit = builder.BoolParameter( "hit" );
			var bone = builder.FloatParameter( "hit_bone", 0.0f, 8.0f );
			var direction = builder.VectorParameter( "hit_direction" );
			var strength = builder.FloatParameter( "hit_strength", 0.0f, 1.0f );

			var graph = AnimGraph.FromDefinition( builder.Build(
				builder.HitReact( builder.ReferencePose(), hit, bone, default, direction, strength ) ), "hit" );

			Assert.IsNotNull( graph );

			var running = new AnimGraphInstance( graph, instance, skeleton, null );
			Assert.IsTrue( running.IsValid );

			//Nothing has happened yet, so the spine is where the pose put it.
			running.Update( 0.1f );
			var resting = SpineAngle( instance );

			//Hit the spine, hard.
			running.SetFloat( "hit_bone", 1.0f );
			running.SetFloat( "hit_strength", 1.0f );
			running.SetVector( "hit_direction", Vector3.Forward );
			running.SetBool( "hit", true );

			running.Update( 0.05f );
			running.Update( 0.05f );

			var struck = SpineAngle( instance );

			Assert.AreNotEqual( resting, struck, 0.5f, "a blow should move the bone it lands on" );

			//And it settles back, rather than staying bent for ever.
			for ( var step = 0; step < 200; step++ ) running.Update( 0.016f );

			Assert.AreEqual( resting, SpineAngle( instance ), 1.0f,
				"and the spring should bring it back to where the animation had it" );
		}
		finally
		{
			try { File.Delete( modelPath ); } catch ( IOException ) { }
		}
	}
}
