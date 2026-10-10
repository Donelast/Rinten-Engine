using Rinten.Physics;
using System;

namespace PhysicsTests;

//Conical joint limits (swing cone, twist stops) checked on a hanging limb with .mdl-style frames
[TestClass]
public class BallJointTest
{
	//How long the limb is, and so how far its centre hangs below the pivot.
	const float LimbLength = 1.0f;

	//Long enough for a limb to swing across its cone and stop moving.
	const int SettleSteps = 600;

	const float Delta = 1.0f / 120.0f;

	//A limb hanging off a fixed anchor, jointed the way a ragdoll's is.
	static Rig Hang( Vector3 coneAxis, float swingLimit = 30.0f, Vector2? twist = null, Vector3? gravity = null )
	{
		var world = new PhysicsWorld
		{
			Gravity = gravity ?? new Vector3( 0, -9.81f, 0 ),
			SleepingEnabled = false,
		};

		//The pivot is the world origin; the limb hangs a metre below it.
		var anchor = new PhysicsBody( world ) { BodyType = PhysicsBodyType.Static };
		anchor.AddBoxShape( Vector3.Zero, Rotation.Identity, new Vector3( 0.1f ) );

		var limb = new PhysicsBody( world )
		{
			BodyType = PhysicsBodyType.Dynamic,
			Transform = new Transform( Vector3.Down * LimbLength ),
		};

		limb.AddBoxShape( Vector3.Zero, Rotation.Identity, new Vector3( 0.1f, LimbLength * 0.5f, 0.1f ) );
		limb.RebuildMass();

		//The anchor frame, aimed so its forward runs down the cone's axis. Both bodies are given the same world frame, which is what leaves the joint
		//at no deflection to begin with. The up is only there to fill the frame out, so it is whichever of two it can be without lying along the aim.
		var up = MathF.Abs( coneAxis.Normal.Dot( Vector3.Backward ) ) > 0.99f ? Vector3.Up : Vector3.Backward;
		var frame = Rotation.LookAt( coneAxis, up );

		var joint = PhysicsJoint.CreateBallSocket(
			new PhysicsPoint( anchor, Vector3.Zero, frame ),
			new PhysicsPoint( limb, Vector3.Up * LimbLength, frame ) );

		joint.SwingLimitEnabled = true;
		joint.SwingLimit = new Vector2( 0, swingLimit );

		if ( twist is { } stops )
		{
			joint.TwistLimitEnabled = true;
			joint.TwistLimit = stops;
		}

		return new Rig( world, anchor, limb, frame );
	}

	//An anchor, the limb hung off it, and the frame the joint was built on.
	sealed record Rig( PhysicsWorld World, PhysicsBody Anchor, PhysicsBody Limb, Rotation Frame )
	{
		public void Run( int steps = SettleSteps )
		{
			for ( var i = 0; i < steps; i++ )
				World.Step( Delta, 4 );
		}

		//How far off the cone's axis the limb has leaned, in degrees.
		public float Swing => Vector3.GetAngle(
			( Anchor.Transform.Rotation * Frame ).Forward,
			( Limb.Transform.Rotation * Frame ).Forward );

		//How far the limb has turned about that axis, in degrees.
		public float Twist
		{
			get
			{
				var relative = ( Anchor.Transform.Rotation * Frame ).Inverse * ( Limb.Transform.Rotation * Frame );
				return MathF.Atan2( relative.z, relative.w ).RadianToDegree() * 2.0f;
			}
		}

		//Where the limb's end of the joint actually is in the world.
		public Vector3 Pivot => Limb.Transform.PointToWorld( Vector3.Up * LimbLength );
	}

	//The first thing a joint is: two bodies pinned at one point. A limb whose pivot wanders is one hanging off nothing.
	[TestMethod]
	public void TheLimbStaysOnItsPivot()
	{
		var rig = Hang( Vector3.Down );
		rig.Run();

		var drift = rig.Pivot.Distance( Vector3.Zero );

		Assert.IsTrue( drift < 0.01f,
			$"the limb's pivot should sit on the anchor's, drifted {drift:0.####}m" );

		rig.World.Delete();
	}

	//A limb inside its cone must settle; a joint born outside its cone makes ragdolls shake
	[TestMethod]
	public void ALimbInsideItsConeComesToRest()
	{
		var rig = Hang( Vector3.Down );
		rig.Run();

		Assert.IsTrue( rig.Swing < 2.0f,
			$"a limb hung down its own cone should stay down it, leaned {rig.Swing:0.#}deg" );

		Assert.IsTrue( rig.Limb.AngularVelocity.Length < 0.05f,
			$"a limb touching no limit should stop, still turning at {rig.Limb.AngularVelocity.Length:0.###} rad/s" );

		rig.World.Delete();
	}

	//The cone is a stop, so a limb pulled sideways hangs at the edge of it and no further. Gravity across the
	//cone's axis is the cleanest way to lean on it: the limb goes as far over as it is allowed and stays there.
	[TestMethod]
	public void TheConeStopsTheLimbAtItsSwingLimit()
	{
		//Cone down, gravity along -X: the limb wants a full 90 degrees over and the cone should catch it at 30.
		var rig = Hang( Vector3.Down, swingLimit: 30.0f, gravity: new Vector3( -9.81f, 0, 0 ) );
		rig.Run();

		Assert.IsTrue( rig.Swing > 25.0f,
			$"gravity across the cone should carry the limb out to the stop, it only reached {rig.Swing:0.#}deg" );

		Assert.IsTrue( rig.Swing < 35.0f,
			$"the limb should not get past a 30deg cone, it reached {rig.Swing:0.#}deg" );

		rig.World.Delete();
	}

	//Cone and twist share one frame; leaning must not be bought with twist the limb doesn't have
	[TestMethod]
	public void BothStopsHoldAtOnce()
	{
		var rig = Hang( Vector3.Down, swingLimit: 60.0f, twist: new Vector2( -5, 5 ),
			gravity: new Vector3( -9.81f, 0, 0 ) );
		rig.Run();

		Assert.IsTrue( rig.Swing is > 45.0f and < 65.0f,
			$"a 60deg cone should let the limb lean and then stop it, it sits at {rig.Swing:0.#}deg" );

		Assert.IsTrue( MathF.Abs( rig.Twist ) < 8.0f,
			$"a 5deg twist stop should hold while the limb leans, it turned {rig.Twist:0.#}deg" );

		rig.World.Delete();
	}

	//Twist is the turn about the cone's own axis, and it has two stops of its own. Spun about the
	//limb, the joint should take the turn out of it at the stop rather than let it keep going round.
	[TestMethod]
	public void TheTwistLimitHoldsTheLimb()
	{
		var rig = Hang( Vector3.Down, twist: new Vector2( -20, 20 ), gravity: Vector3.Zero );

		//Spun about the limb's own length, which is the axis twist is measured on.
		rig.Limb.AngularVelocity = Vector3.Down * 8.0f;
		rig.Run();

		Assert.IsTrue( MathF.Abs( rig.Twist ) < 30.0f,
			$"a 20deg twist stop should hold, the limb got to {rig.Twist:0.#}deg" );

		Assert.IsTrue( rig.Limb.AngularVelocity.Length < 0.5f,
			$"the stop should take the spin out of the limb, still turning at {rig.Limb.AngularVelocity.Length:0.###} rad/s" );

		rig.World.Delete();
	}
}
