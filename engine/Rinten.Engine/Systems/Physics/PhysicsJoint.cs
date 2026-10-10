
namespace Rinten.Physics;

/// <summary>A physics constraint.</summary>
public partial class PhysicsJoint : IHandle
{
	//The simulation's side of this joint.
	internal IPhysicsJointBackend Backend => backend;
	private protected IPhysicsJointBackend backend;

	private protected bool BackendValid => backend?.IsValid ?? false;

	#region IHandle
	//A pointer to the actual native object


	//IHandle implementation
	void IHandle.HandleInit( IntPtr ptr )
	{
		//See Bind: our own simulation hands the joint over itself.
	}
	void IHandle.HandleDestroy()
	{
		backend = null;

		World = null;
	}
	bool IHandle.HandleValid() => BackendValid;
	#endregion

	internal PhysicsJoint() { }
	internal PhysicsJoint( HandleCreationData _ ) { }

	//Bind this joint to a simulation. Mirrors what HandleInit does on the native handle path, for backends that create the managed object first.
	internal void AttachBackend( IPhysicsJointBackend created, PhysicsWorld inWorld )
	{
		backend = created;
		World = inWorld;
	}

	internal PhysicsJointType JointType => backend.GetJointType();

	/// <summary>Removes this joint.</summary>
	public void Remove()
	{
		if ( !BackendValid ) return;
		if ( World == null ) return;

		World.Backend.RemoveJoint( this );
	}

	internal void InternalJointBroken()
	{
		onBreak?.Invoke();
	}

	internal void WakeBodies()
	{
		if ( Body1.IsValid() )
		{
			Body1.Backend.Wake();
		}

		if ( Body2.IsValid() )
		{
			Body2.Backend.Wake();
		}
	}

	event Action onBreak;

	/// <summary>Called when the joint breaks.</summary>
	public event Action OnBreak
	{
		add
		{
			onBreak += value;
		}
		remove => onBreak -= value;
	}

	/// <summary>The <see cref="PhysicsWorld"/> this joint belongs to.</summary>
	public PhysicsWorld World { get; private set; }

	/// <summary>The source physics body this joint is attached to.</summary>
	public PhysicsBody Body1 => BackendValid ? backend.GetBody1() : null;

	/// <summary>The target physics body this joint is constraining.</summary>
	public PhysicsBody Body2 => BackendValid ? backend.GetBody2() : null;
	/// <summary>A specific point this joint is attached at on <see cref="Body1"/></summary>
	public PhysicsPoint Point1
	{
		get
		{
			backend.GetLocalFrameA( out var position, out var rotation );
			return new( backend.GetBody1(), position, rotation );
		}
		set => backend.SetLocalFrameA( value.LocalPosition, value.LocalRotation );
	}

	/// <summary>A specific point this joint is attached at on <see cref="Body2"/></summary>
	public PhysicsPoint Point2
	{
		get
		{
			backend.GetLocalFrameB( out var position, out var rotation );
			return new( backend.GetBody2(), position, rotation );
		}
		set => backend.SetLocalFrameB( value.LocalPosition, value.LocalRotation );
	}

	[Obsolete]
	public bool IsActive
	{
		get => true;
		set
		{

		}
	}

	/// <summary>Enables or disables collisions between the 2 constrained physics bodies.</summary>
	public bool Collisions
	{
		get => backend.IsCollisionEnabled();
		set => backend.SetEnableCollision( value );
	}

	/// <summary>Strength of the linear constraint. If it takes any more energy than this, it'll break.</summary>
	public float Strength
	{
		get => backend.GetMaxLinearImpulse();
		set => backend.SetMaxLinearImpulse( value );
	}

	/// <summary>Strength of the angular constraint. If it takes any more energy than this, it'll break.</summary>
	public float AngularStrength
	{
		get => backend.GetMaxAngularImpulse();
		set => backend.SetMaxAngularImpulse( value );
	}

	internal float LinearImpulse => backend.GetLinearImpulse();
	internal float AngularImpulse => backend.GetAngularImpulse();

	/// <summary>Creates an almost solid constraint between two physics bodies.</summary>
	public static FixedJoint CreateFixed( PhysicsPoint a, PhysicsPoint b )
	{
		ArgumentNullException.ThrowIfNull( a.Body, nameof( a ) );
		ArgumentNullException.ThrowIfNull( b.Body, nameof( b ) );

		Assert.AreEqual( a.Body.World, b.Body.World );
		Assert.AreNotEqual( a.Body, b.Body );

		var joint = a.Body.World.Backend.AddWeldJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform ) as FixedJoint;
		if ( !joint.IsValid() )
			throw new Exception( $"Unable to create joint" );

		return joint;
	}

	/// <summary>Creates a constraint like a rope, where it has no minimum length but its max length is restrained.</summary>
	public static SpringJoint CreateLength( PhysicsPoint a, PhysicsPoint b, float maxLength )
	{
		ArgumentNullException.ThrowIfNull( a.Body, nameof( a ) );
		ArgumentNullException.ThrowIfNull( b.Body, nameof( b ) );

		Assert.AreEqual( a.Body.World, b.Body.World );
		Assert.AreNotEqual( a.Body, b.Body );

		var joint = a.Body.World.Backend.AddSpringJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform ) as SpringJoint;
		if ( !joint.IsValid() )
			throw new Exception( $"Unable to create joint" );

		joint.MaxLength = maxLength;
		joint.MinLength = 0;

		return joint;
	}

	/// <summary>Creates a constraint that will try to stay the same length, like a spring, or a rod.</summary>
	public static SpringJoint CreateSpring( PhysicsPoint a, PhysicsPoint b, float minLength, float maxLength )
	{
		ArgumentNullException.ThrowIfNull( a.Body, nameof( a ) );
		ArgumentNullException.ThrowIfNull( b.Body, nameof( b ) );

		Assert.AreEqual( a.Body.World, b.Body.World );
		Assert.AreNotEqual( a.Body, b.Body );

		var joint = a.Body.World.Backend.AddSpringJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform ) as SpringJoint;
		if ( !joint.IsValid() )
			throw new Exception( $"Unable to create joint" );

		joint.MaxLength = maxLength;
		joint.MinLength = minLength;

		return joint;
	}

	public static HingeJoint CreateHinge( PhysicsPoint a, PhysicsPoint b )
	{
		ArgumentNullException.ThrowIfNull( a.Body, nameof( a ) );
		ArgumentNullException.ThrowIfNull( b.Body, nameof( b ) );

		Assert.AreEqual( a.Body.World, b.Body.World );
		Assert.AreNotEqual( a.Body, b.Body );

		var joint = a.Body.World.Backend.AddRevoluteJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform ) as HingeJoint;
		if ( !joint.IsValid() )
			throw new Exception( $"Unable to create joint" );

		return joint;
	}

	public static HingeJoint CreateHinge( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
	{
		ArgumentNullException.ThrowIfNull( body1, nameof( body1 ) );
		ArgumentNullException.ThrowIfNull( body2, nameof( body2 ) );

		Assert.AreEqual( body1.World, body2.World );
		Assert.AreNotEqual( body1, body2 );

		if ( !body2.MotionEnabled && body1.MotionEnabled )
		{
			(body1, body2) = (body2, body1);
			(localFrame1, localFrame2) = (localFrame2, localFrame1);
		}

		var joint = body1.World.Backend.AddRevoluteJoint( body1, body2, localFrame1, localFrame2 ) as HingeJoint;
		if ( !joint.IsValid() )
			throw new Exception( $"Unable to create joint" );

		return joint;
	}

	/// <summary>Creates a slider constraint between two physics bodies via <see cref="PhysicsPoint"/>s.</summary>
	public static SliderJoint CreateSlider( PhysicsPoint a, PhysicsPoint b, float minLength, float maxLength )
	{
		ArgumentNullException.ThrowIfNull( a.Body, nameof( a ) );
		ArgumentNullException.ThrowIfNull( b.Body, nameof( b ) );

		Assert.AreEqual( a.Body.World, b.Body.World );
		Assert.AreNotEqual( a.Body, b.Body );

		var joint = a.Body.World.Backend.AddPrismaticJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform ) as SliderJoint;
		if ( !joint.IsValid() )
			throw new Exception( $"Unable to create joint" );

		joint.MaxLength = maxLength;
		joint.MinLength = minLength;

		return joint;
	}

	/// <summary>Creates a ball socket constraint.</summary>
	public static BallSocketJoint CreateBallSocket( PhysicsBody body1, PhysicsBody body2, Vector3 origin )
	{
		ArgumentNullException.ThrowIfNull( body1, nameof( body1 ) );
		ArgumentNullException.ThrowIfNull( body2, nameof( body2 ) );

		Assert.AreEqual( body1.World, body2.World );
		Assert.AreNotEqual( body1, body2 );

		var anchor = new Transform( origin );
		var localFrame1 = anchor.ToLocal( body1.Transform );
		var localFrame2 = anchor.ToLocal( body2.Transform );

		var joint = body1.World.Backend.AddSphericalJoint( body1, body2, localFrame1, localFrame2 ) as BallSocketJoint;
		if ( !joint.IsValid() )
			throw new Exception( $"Unable to create joint" );

		return joint;
	}

	/// <summary>Creates a ball socket constraint.</summary>
	public static BallSocketJoint CreateBallSocket( PhysicsPoint a, PhysicsPoint b )
	{
		ArgumentNullException.ThrowIfNull( a.Body, nameof( a ) );
		ArgumentNullException.ThrowIfNull( b.Body, nameof( b ) );

		Assert.AreEqual( a.Body.World, b.Body.World );
		Assert.AreNotEqual( a.Body, b.Body );

		var joint = a.Body.World.Backend.AddSphericalJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform ) as BallSocketJoint;
		if ( !joint.IsValid() ) throw new Exception( $"Unable to create joint" );

		return joint;
	}

	public static ControlJoint CreateControl( PhysicsPoint a, PhysicsPoint b )
	{
		ArgumentNullException.ThrowIfNull( a.Body, nameof( a ) );
		ArgumentNullException.ThrowIfNull( b.Body, nameof( b ) );

		Assert.AreEqual( a.Body.World, b.Body.World );
		Assert.AreNotEqual( a.Body, b.Body );

		var joint = a.Body.World.Backend.AddMotorJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform ) as ControlJoint;
		if ( !joint.IsValid() )
			throw new Exception( $"Unable to create joint" );

		return joint;
	}

	internal static WheelJoint CreateWheel( PhysicsPoint a, PhysicsPoint b )
	{
		ArgumentNullException.ThrowIfNull( a.Body, nameof( a ) );
		ArgumentNullException.ThrowIfNull( b.Body, nameof( b ) );

		Assert.AreEqual( a.Body.World, b.Body.World );
		Assert.AreNotEqual( a.Body, b.Body );

		var joint = a.Body.World.Backend.AddWheelJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform ) as WheelJoint;
		return !joint.IsValid() ? throw new Exception( $"Unable to create joint" ) : joint;
	}

	internal static PhysicsJoint CreateFilter( PhysicsBody a, PhysicsBody b )
	{
		ArgumentNullException.ThrowIfNull( a, nameof( a ) );
		ArgumentNullException.ThrowIfNull( b, nameof( b ) );

		Assert.AreEqual( a.World, b.World );
		Assert.AreNotEqual( a, b );

		var joint = a.World.Backend.AddFilterJoint( a, b );
		return !joint.IsValid() ? throw new Exception( $"Unable to create joint" ) : joint;
	}

	public static UprightJoint CreateUpright( PhysicsPoint a, PhysicsPoint b )
	{
		ArgumentNullException.ThrowIfNull( a.Body, nameof( a ) );
		ArgumentNullException.ThrowIfNull( b.Body, nameof( b ) );

		Assert.AreEqual( a.Body.World, b.Body.World );
		Assert.AreNotEqual( a.Body, b.Body );

		var joint = a.Body.World.Backend.AddParallelJoint( a.Body, b.Body, a.LocalTransform, b.LocalTransform ) as UprightJoint;
		return !joint.IsValid() ? throw new Exception( $"Unable to create joint" ) : joint;
	}

	[Obsolete]
	public static HingeJoint CreateHinge( PhysicsBody body1, PhysicsBody body2, Vector3 center, Vector3 axis )
	{
		throw new Exception( $"Unable to create joint" );

	}

	[Obsolete]
	public static SliderJoint CreateSlider( PhysicsBody body1, PhysicsBody body2, Vector3 origin1, Vector3 origin2, Vector3 axis, float minLength, float maxLength )
	{
		throw new Exception( $"Unable to create joint" );

	}

	[Obsolete]
	public static PulleyJoint CreatePulley( PhysicsBody body1, PhysicsBody body2, Vector3 anchor1, Vector3 ground1, Vector3 anchor2, Vector3 ground2 )
	{
		throw new Exception( $"We don't have pulley!" );
	}

	public sealed override int GetHashCode() => base.GetHashCode();
	public sealed override bool Equals( object obj ) => base.Equals( obj );
}
