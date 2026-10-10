namespace Rinten;

//A physics world on our own core, over Box3D.
internal sealed unsafe class CorePhysicsWorldBackend : IPhysicsWorldBackend
{
	private readonly PhysicsWorld owner;
	private ulong world;

	//Every world has one static body that world geometry and the ground hang off; we make it ourselves.
	private PhysicsBody referenceBody;

	public CorePhysicsWorldBackend( PhysicsWorld owner )
	{
		this.owner = owner;
		world = CorePhysics.WorldCreate();
	}

	internal ulong Handle => world;

	public bool IsValid => world != 0 && CorePhysics.WorldValid( world );

	public void Destroy( PhysicsWorld _ )
	{
		if ( world == 0 ) return;

		CorePhysics.WorldDestroy( world );
		world = 0;
		referenceBody = null;
	}

	//-- bodies ---------------------------------------------------------------

	public void AddBody( PhysicsBody body )
	{
		//Bodies start at the origin, unrotated, and dynamic; whoever created it sets the
		//transform and type straight after.
		var handle = CorePhysics.BodyCreate( world, CorePhysics.MotionType.Dynamic,
			Vector3.Zero, Rotation.Identity );

		body.AttachBackend( new CorePhysicsBodyBackend( handle, body ) );
	}

	public void RemoveBody( PhysicsBody body )
	{
		if ( body?.Backend is CorePhysicsBodyBackend core )
			core.DestroyNative();
	}

	public PhysicsBody GetWorldReferenceBody()
	{
		if ( referenceBody.IsValid() ) return referenceBody;

		referenceBody = PhysicsBody.CreateDetached( owner );

		var handle = CorePhysics.BodyCreate( world, CorePhysics.MotionType.Static,
			Vector3.Zero, Rotation.Identity );

		referenceBody.AttachBackend( new CorePhysicsBodyBackend( handle, referenceBody ) );
		return referenceBody;
	}

	public void SetWorldReferenceBody( PhysicsBody body ) => referenceBody = body;

	//-- simulation -----------------------------------------------------------

	public void StepSimulation( float timestep, int steps )
	{
		CorePhysics.WorldStep( world, timestep, steps );
		EnginePhysics.TickStatsLog();
	}

	public Vector3 GetGravity() => CorePhysics.WorldGetGravity( world );
	public void SetGravity( Vector3 gravity ) => CorePhysics.WorldSetGravity( world, gravity );

	public PhysicsSimulationMode GetSimulation()
		=> CorePhysics.WorldContinuousEnabled( world )
			? PhysicsSimulationMode.Continuous
			: PhysicsSimulationMode.Discrete;

	public void SetSimulation( PhysicsSimulationMode simulation )
		=> CorePhysics.WorldEnableContinuous( world, simulation == PhysicsSimulationMode.Continuous );

	public bool IsSleepingEnabled() => CorePhysics.WorldSleepingEnabled( world );
	public void EnableSleeping() => CorePhysics.WorldEnableSleeping( world, true );
	public void DisableSleeping() => CorePhysics.WorldEnableSleeping( world, false );

	public void SetMaximumLinearSpeed( float speed ) => CorePhysics.WorldSetMaxLinearSpeed( world, speed );

	//The rules the core filters contacts with, or null while everything collides. Shapes ask for it when their tags change.
	internal CoreCollisionRules CollisionRules { get; private set; }

	CoreCollisionRules IPhysicsWorldBackend.CompiledCollisionRules => CollisionRules;

	/// <summary>Tag-pair collision filtering. The rules are right here on the managed side, so the JSON is ignored and the owner's own <see cref="PhysicsWorld.CollisionRules"/> is compiled instead.</summary>
	public void SetCollisionRulesFromJson( string rules )
	{
		CollisionRules = CoreCollisionRules.Compile( owner?.CollisionRules );

		if ( CollisionRules is null )
		{
			CorePhysics.WorldClearCollisionRules( world );
		}
		else
		{
			CollisionRules.Upload( world );
		}

		//Shapes made before the rules arrived have to be told again.
		foreach ( var body in owner.Bodies )
		{
			if ( body?.Backend is CorePhysicsBodyBackend core )
				core.RefreshTags();
		}
	}

	/// <summary>Touch, trigger and hit events. The native callback (an unmanaged function pointer filling a notification struct) is ignored; our core and dispatches from managed values instead.</summary>
	public void ProcessIntersections() => Events.Dispatch( owner, this );

	//This world's contact bookkeeping. Per world, because more than one physics world is alive at once and they do not all step on the same thread.
	internal CorePhysicsEvents Events { get; } = new();

	//-- joints --------------------------------------------------------------- Box3D has a direct equivalent for every joint Rinten has, so
	//each of these is one call. The managed subclass carries the joint's kind, which is why the factory takes it rather than deriving it.

	//Matches sbc_joint_type.
	private enum JointKind
	{
		Fixed = 0,
		Spring = 1,
		Hinge = 2,
		Slider = 3,
		BallSocket = 4,
		Control = 5,
		Wheel = 6,
		Upright = 7,
		Filter = 8,
	}

	public Physics.PhysicsJoint AddWeldJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Fixed, a, b, frameA, frameB, static h => new Physics.FixedJoint( h ) );

	public Physics.PhysicsJoint AddSpringJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Spring, a, b, frameA, frameB, static h => new Physics.SpringJoint( h ) );

	//Turns a joint frame so Box3D's revolute Z axis lies along the frame's up
	static Transform ToHingeAxis( Transform frame ) => frame.WithRotation( frame.Rotation * Rotation.FromPitch( 90 ) );

	public Physics.PhysicsJoint AddRevoluteJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Hinge, a, b, ToHingeAxis( frameA ), ToHingeAxis( frameB ),
			static h => new Physics.HingeJoint( h ) );

	//Turns a joint frame so its X lies along the forward it slides on. Box3D slides a prismatic along frame A's X ("body B may slide along the
	//x-axis in local frame A"), and a slider here is authored along the frame's forward - which was that same X until the engine turned Y-up.
	static Transform ToSlideAxis( Transform frame ) => frame.WithRotation( frame.Rotation * Rotation.FromYaw( 90 ) );

	public Physics.PhysicsJoint AddPrismaticJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Slider, a, b, ToSlideAxis( frameA ), ToSlideAxis( frameB ),
			static h => new Physics.SliderJoint( h ) );

	//Turns a joint frame so Box3D's cone Z axis lies along the authored forward (-Z here)
	static Transform ToConeAxis( Transform frame ) => frame.WithRotation( frame.Rotation * Rotation.FromYaw( 180 ) );

	public Physics.PhysicsJoint AddSphericalJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.BallSocket, a, b, ToConeAxis( frameA ), ToConeAxis( frameB ),
			static h => new Physics.BallSocketJoint( h ) );

	public Physics.PhysicsJoint AddMotorJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Control, a, b, frameA, frameB, static h => new Physics.ControlJoint( h ) );

	public Physics.PhysicsJoint AddWheelJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Wheel, a, b, frameA, frameB, static h => new Physics.WheelJoint( h ) );

	public Physics.PhysicsJoint AddParallelJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Upright, a, b, frameA, frameB, static h => new Physics.UprightJoint( h ) );

	public Physics.PhysicsJoint AddFilterJoint( PhysicsBody a, PhysicsBody b )
		=> Create( JointKind.Filter, a, b, Transform.Zero, Transform.Zero, static h => new Physics.PhysicsJoint( h ) );

	private unsafe Physics.PhysicsJoint Create( JointKind kind, PhysicsBody a, PhysicsBody b,
		Transform frameA, Transform frameB, Func<HandleCreationData, Physics.PhysicsJoint> construct )
	{
		if ( a?.Backend is not CorePhysicsBodyBackend coreA ) return null;
		if ( b?.Backend is not CorePhysicsBodyBackend coreB ) return null;

		var handle = CorePhysics.JointCreate( world, (CorePhysics.JointType)(int)kind,
			coreA.Handle, coreB.Handle, frameA, frameB );
		if ( handle == 0 ) return null;

		var managed = construct( new HandleCreationData() );
		managed.AttachBackend( new CorePhysicsJointBackend( handle, owner ), owner );
		return managed;
	}

	public void RemoveJoint( Physics.PhysicsJoint joint )
	{
		if ( joint?.Backend is CorePhysicsJointBackend core )
			core.DestroyNative();
	}

	//-- aggregates -- a group is just the ordinary bodies and joints built from a model's physics

	/// <summary>Build every body and joint a model's physics describes, as one group.</summary>
	public PhysicsGroup CreateAggregateInstance( Model model, Transform start, ulong node, PhysicsMotionType motion )
	{
		var description = model?.Physics;
		if ( description is null || description.Parts.Count == 0 ) return null;

		var group = new PhysicsGroup();
		var backend = new CorePhysicsGroupBackend( owner );

		group.AttachBackend( backend );

		var bodyType = motion switch
		{
			PhysicsMotionType.Static => PhysicsBodyType.Static,
			PhysicsMotionType.Keyframed => PhysicsBodyType.Keyframed,
			_ => PhysicsBodyType.Dynamic,
		};

		foreach ( var part in description.Parts )
		{
			var body = new PhysicsBody( owner )
			{
				BodyType = bodyType,
				Transform = start.ToWorld( part.Transform ),
			};

			AddPartShapes( body, part );

			//The description's numbers only mean anything when they were actually set; zero means the model never said, not that it weighs nothing.
			if ( part.Mass > 0 ) body.Mass = part.Mass;
			if ( part.LinearDamping > 0 ) body.LinearDamping = part.LinearDamping;
			if ( part.AngularDamping > 0 ) body.AngularDamping = part.AngularDamping;

			if ( part.OverrideMassCenter )
			{
				body.OverrideMassCenter = true;
				body.LocalMassCenter = part.MassCenterOverride;
			}

			if ( part.GravityScale != 1.0f )
			{
				body.DefaultGravityScale = part.GravityScale;
				body.GravityScale = part.GravityScale;
			}

			if ( body.Backend is CorePhysicsBodyBackend core )
				core.SetAggregate( group );

			backend.Add( body, part.BoneName );
		}

		AddDescribedJoints( description, backend );

		return group;
	}

	/// <summary>The same, from a model that has not been loaded yet.</summary>
	public PhysicsGroup CreateAggregateInstance( string resourceName, Transform start, ulong node, PhysicsMotionType motion )
		=> CreateAggregateInstance( Model.Load( resourceName ), start, node, motion );

	internal static void AddPartShapes( PhysicsBody body, PhysicsGroupDescription.BodyPart part )
	{
		foreach ( var sphere in part.Spheres )
		{
			var shape = body.AddSphereShape( sphere.Sphere.Center, sphere.Sphere.Radius );
			if ( shape.IsValid() ) shape.Surface = sphere.Surface;
		}

		foreach ( var capsule in part.Capsules )
		{
			var shape = body.AddCapsuleShape( capsule.Capsule.CenterA, capsule.Capsule.CenterB, capsule.Capsule.Radius );
			if ( shape.IsValid() ) shape.Surface = capsule.Surface;
		}

		foreach ( var hull in part.Hulls )
		{
			var shape = body.AddShape( hull, Transform.Zero );
			if ( shape.IsValid() ) shape.Surface = hull.Surface;
		}

		foreach ( var mesh in part.Meshes )
		{
			var shape = body.AddShape( mesh, Transform.Zero, false, true );
			if ( !shape.IsValid() ) continue;

			shape.Surface = mesh.Surface;
			shape.Surfaces = mesh.Surfaces;
		}
	}

	//The joints between the parts, exactly as a ragdoll builds them - the description is the same one either way.
	internal static void AddDescribedJoints( PhysicsGroupDescription description, CorePhysicsGroupBackend group )
	{
		foreach ( var desc in description.Joints )
		{
			var body1 = group.GetBodyHandle( desc.Body1 );
			var body2 = group.GetBodyHandle( desc.Body2 );

			if ( !body1.IsValid() || !body2.IsValid() ) continue;

			var point1 = new PhysicsPoint( body1, desc.Frame1.Position, desc.Frame1.Rotation );
			var point2 = new PhysicsPoint( body2, desc.Frame2.Position, desc.Frame2.Rotation );

			Physics.PhysicsJoint joint = null;

			switch ( desc.Type )
			{
				case PhysicsGroupDescription.JointType.Hinge:
				{
					var hinge = Physics.PhysicsJoint.CreateHinge( point1, point2 );

					if ( desc.EnableTwistLimit )
					{
						hinge.MinAngle = desc.TwistMin;
						hinge.MaxAngle = desc.TwistMax;
					}

					if ( desc.EnableAngularMotor )
					{
						var axis = body1.Transform.ToWorld( desc.Frame1 ).Rotation.Up;
						hinge.Backend.SetAngularMotor( axis.Dot( desc.AngularTargetVelocity ), desc.MaxTorque );
					}

					joint = hinge;
					break;
				}

				case PhysicsGroupDescription.JointType.Ball:
				{
					var ball = Physics.PhysicsJoint.CreateBallSocket( point1, point2 );

					if ( desc.EnableSwingLimit )
					{
						ball.SwingLimitEnabled = true;
						ball.SwingLimit = new Vector2( desc.SwingMin, desc.SwingMax );
					}

					if ( desc.EnableTwistLimit )
					{
						ball.TwistLimitEnabled = true;
						ball.TwistLimit = new Vector2( desc.TwistMin, desc.TwistMax );
					}

					joint = ball;
					break;
				}

				case PhysicsGroupDescription.JointType.Fixed:
				{
					var fixedJoint = Physics.PhysicsJoint.CreateFixed( point1, point2 );
					fixedJoint.SpringLinear = new PhysicsSpring( desc.LinearFrequency, desc.LinearDampingRatio );
					fixedJoint.SpringAngular = new PhysicsSpring( desc.AngularFrequency, desc.AngularDampingRatio );

					joint = fixedJoint;
					break;
				}
			}

			if ( !joint.IsValid() ) continue;

			//What the joint resists being moved with, where it is a kind that has any: the
			//same friction ModelPhysics puts on a ragdoll built from the same description.
			if ( desc.Friction > 0.0f )
			{
				switch ( joint )
				{
					case Physics.BallSocketJoint ball: ball.Friction = desc.Friction; break;
					case Physics.HingeJoint hinge: hinge.Friction = desc.Friction; break;
					case Physics.SliderJoint slider: slider.Friction = desc.Friction; break;
				}
			}

			group.Add( joint );
		}
	}

	public void DestroyAggregateInstance( PhysicsGroup group )
	{
		if ( group?.Backend is CorePhysicsGroupBackend core )
			core.RemoveAll();
	}

	//-- overlap queries ------------------------------------------------------

	//How many shapes one overlap query can report.
	private const int MaxOverlapResults = 256;

	public int OverlapSphere( Vector3 center, float radius, List<PhysicsShape> results )
	{
		//A sphere is one point inflated by its radius.
		Span<Vector3> point = stackalloc Vector3[1] { Vector3.Zero };
		return Overlap( center, point, radius, results );
	}

	public int OverlapBox( BBox box, List<PhysicsShape> results )
	{
		var center = box.Center;

		Span<Vector3> corners = stackalloc Vector3[8];
		WriteCorners( box, center, corners );

		return Overlap( center, corners, 0.0f, results );
	}

	public int OverlapConvex( ReadOnlySpan<Vector3> points, List<PhysicsShape> results )
	{
		if ( points.Length == 0 ) return 0;

		//The proxy is relative to an origin, and any point inside the hull will do; the centroid keeps the offsets small.
		var origin = Vector3.Zero;
		foreach ( var p in points ) origin += p;
		origin /= points.Length;

		Span<Vector3> relative = stackalloc Vector3[points.Length];
		for ( var i = 0; i < points.Length; i++ ) relative[i] = points[i] - origin;

		return Overlap( origin, relative, 0.0f, results );
	}

	private unsafe int Overlap( Vector3 origin, ReadOnlySpan<Vector3> points, float radius, List<PhysicsShape> results )
	{
		if ( results is null ) return 0;

		var flat = stackalloc float[points.Length * 3];
		for ( var i = 0; i < points.Length; i++ )
		{
			flat[i * 3 + 0] = points[i].x;
			flat[i * 3 + 1] = points[i].y;
			flat[i * 3 + 2] = points[i].z;
		}

		Span<ulong> handles = stackalloc ulong[MaxOverlapResults];

		var count = CorePhysics.WorldOverlap( world, origin,
			new ReadOnlySpan<float>( flat, points.Length * 3 ), radius, handles );

		var added = 0;
		for ( var i = 0; i < count; i++ )
		{
			var shape = CoreShapeRegistry.Get( handles[i] );
			if ( shape is null ) continue;

			results.Add( shape );
			added++;
		}

		return added;
	}

	private static void WriteCorners( BBox box, Vector3 origin, Span<Vector3> corners )
	{
		var min = box.Mins;
		var max = box.Maxs;

		for ( var i = 0; i < 8; i++ )
		{
			corners[i] = new Vector3(
				(i & 1) == 0 ? min.x : max.x,
				(i & 2) == 0 ? min.y : max.y,
				(i & 4) == 0 ? min.z : max.z ) - origin;
		}
	}

	//-- traces ---------------------------------------------------------------

	public PhysicsTraceResult Trace( in PhysicsTrace.Request request, Func<PhysicsShape, bool> filter, PhysicsBody sweptBody )
		=> CorePhysicsTrace.Run( this, request, filter, sweptBody );

	public PhysicsTraceResult[] TraceAll( in PhysicsTrace.Request request, Func<PhysicsShape, bool> filter, PhysicsBody sweptBody )
		=> CorePhysicsTrace.RunAll( this, request, filter, sweptBody );

	//-- debug ----------------------------------------------------------------

	private SceneWorld debugScene;

	public SceneWorld GetDebugScene() => debugScene;
	public void SetDebugScene( SceneWorld scene ) => debugScene = scene;

	/// <summary>Draw what the simulation is actually doing, through the world's callback.</summary>
	public void Draw( PhysicsWorld.DebugDrawCallback callback )
	{
		//Draw runs every frame whether or not the world is stepping, which in the editor is the only per-frame hook physics has.
		EnginePhysics.TickStatsLog();

		if ( !EnginePhysics.DebugDraw ) return;
		if ( callback is null ) return;

		var draw = callback;

		foreach ( var body in owner.Bodies )
		{
			if ( body?.Backend is not CorePhysicsBodyBackend ) continue;

			var colour = body.BodyType switch
			{
				PhysicsBodyType.Static => new Color( 0.5f, 0.5f, 0.5f ),
				PhysicsBodyType.Keyframed => new Color( 0.4f, 0.6f, 1.0f ),
				_ => body.Sleeping ? new Color( 0.4f, 0.4f, 0.7f ) : new Color( 0.4f, 1.0f, 0.4f ),
			};

			var transform = body.Transform;

			foreach ( var shape in body.Shapes )
			{
				if ( shape?.Backend is not CorePhysicsShapeBackend core ) continue;

				//The outline is in the body's frame, as the geometry is.
				var points = core.GetOutlineSegments();

				for ( var i = 0; i + 1 < points.Length; i += 2 )
				{
					var segment = new PhysicsWorld.DrawSegmentArgs
					{
						P1 = transform.PointToWorld( points[i] ),
						P2 = transform.PointToWorld( points[i + 1] ),
						Color = colour,
					};

					var args = new PhysicsWorld.DebugDrawArgs { Segment = &segment };
					draw( &args );
				}
			}

			if ( EnginePhysics.DebugDrawBounds )
			{
				var bounds = new PhysicsWorld.DrawBoundsArgs
				{
					Box = body.GetBounds(),
					Color = new Color( 1.0f, 1.0f, 0.0f, 0.25f ),
				};

				var args = new PhysicsWorld.DebugDrawArgs { Bounds = &bounds };
				draw( &args );
			}
		}
	}
}
