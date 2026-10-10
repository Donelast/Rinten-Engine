using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Rinten.Native.Box2D;
using static Rinten.Native.Box2D.Box2D;

namespace Rinten;

//2D physics world over Box2D on world XY (see Box2DPlane); SI units, unscaled
internal sealed unsafe partial class Box2DWorldBackend : IPhysicsWorldBackend
{
	//Box2D's solver wants a few sub-steps a step; fewer and stacks go soft.
	private const int MinSubSteps = 4;

	private readonly PhysicsWorld owner;
	private b2WorldId world;
	private PhysicsBody referenceBody;

	public Box2DWorldBackend( PhysicsWorld owner )
	{
		this.owner = owner;

		var def = b2DefaultWorldDef();

		//Earth. The scene sets its own on creation; this is what a world nobody configured falls at.
		def.gravity = Box2DPlane.Vec( 0.0f, -9.81f );
		def.enableContinuous = true;

		//One worker: the step runs on whichever thread asked for it, callbacks into
		//managed code come back on that thread, and the browser has no others to give.
		def.workerCount = 1;

		world = b2CreateWorld( &def );
	}

	internal b2WorldId Id => world;

	public bool IsValid => b2World_IsValid( world );

	public void Destroy( PhysicsWorld _ )
	{
		if ( !IsValid ) return;

		foreach ( var body in owner.Bodies.ToArray() )
		{
			if ( body.Backend is Box2DBodyBackend b2 )
				b2.DestroyNative();
		}

		b2DestroyWorld( world );
		world = default;
		referenceBody = null;

		FreeRules();
	}

	//-- bodies ---------------------------------------------------------------

	private b2BodyId CreateNativeBody( b2BodyType type )
	{
		var def = b2DefaultBodyDef();
		def.type = type;
		return b2CreateBody( world, &def );
	}

	public void AddBody( PhysicsBody body )
	{
		//Dynamic at the origin, like the Box3D world: whoever made it sets the type and transform straight after.
		body.AttachBackend( new Box2DBodyBackend( this, CreateNativeBody( b2BodyType.b2_dynamicBody ), body ) );
	}

	public void RemoveBody( PhysicsBody body )
	{
		if ( body?.Backend is Box2DBodyBackend b2 )
			b2.DestroyNative();
	}

	public PhysicsBody GetWorldReferenceBody()
	{
		if ( referenceBody.IsValid() ) return referenceBody;

		referenceBody = PhysicsBody.CreateDetached( owner );
		referenceBody.AttachBackend( new Box2DBodyBackend( this, CreateNativeBody( b2BodyType.b2_staticBody ), referenceBody ) );
		return referenceBody;
	}

	public void SetWorldReferenceBody( PhysicsBody body ) => referenceBody = body;

	//-- simulation -----------------------------------------------------------

	public void StepSimulation( float timestep, int steps )
	{
		if ( !IsValid ) return;

		b2World_Step( world, timestep, Math.Max( steps, MinSubSteps ) );
		ProcessJointEvents();
	}

	public Vector3 GetGravity() => IsValid ? Box2DPlane.Point( b2World_GetGravity( world ) ) : default;

	/// <summary>Gravity in the plane. Its depth has nowhere to go and is dropped.</summary>
	public void SetGravity( Vector3 gravity )
	{
		if ( IsValid ) b2World_SetGravity( world, Box2DPlane.Vec( gravity ) );
	}

	public PhysicsSimulationMode GetSimulation()
		=> IsValid && b2World_IsContinuousEnabled( world ) ? PhysicsSimulationMode.Continuous : PhysicsSimulationMode.Discrete;

	public void SetSimulation( PhysicsSimulationMode simulation )
	{
		if ( IsValid ) b2World_EnableContinuous( world, simulation == PhysicsSimulationMode.Continuous );
	}

	public bool IsSleepingEnabled() => IsValid && b2World_IsSleepingEnabled( world );
	public void EnableSleeping() { if ( IsValid ) b2World_EnableSleeping( world, true ); }
	public void DisableSleeping() { if ( IsValid ) b2World_EnableSleeping( world, false ); }

	public void SetMaximumLinearSpeed( float speed )
	{
		if ( IsValid ) b2World_SetMaximumLinearSpeed( world, speed );
	}

	//A joint whose force threshold was crossed this step breaks.
	private void ProcessJointEvents()
	{
		var events = b2World_GetJointEvents( world );
		if ( events.count <= 0 ) return;

		//Copied out first: a break handler removes joints, which Box2D is still holding this array for.
		var broken = new List<Physics.PhysicsJoint>( events.count );
		for ( var i = 0; i < events.count; i++ )
		{
			if ( Box2DRegistry.Joints.TryGetValue( Box2DPlane.Key( events.jointEvents[i].jointId ), out var joint ) )
				broken.Add( joint );
		}

		foreach ( var joint in broken )
			PhysicsEngine.OnPhysicsJointBreak( joint );
	}

	//-- collision rules ------------------------------------------------------

	//The rules shapes compile their tags against, or null while everything collides.
	internal CoreCollisionRules CollisionRules { get; private set; }

	CoreCollisionRules IPhysicsWorldBackend.CompiledCollisionRules => CollisionRules;

	/// <summary>Tag-pair rules as on Box3D; Box2D cannot touch without pushing, so Trigger pairs act as Ignore</summary>
	public void SetCollisionRulesFromJson( string rules )
	{
		CollisionRules = CoreCollisionRules.Compile( owner?.CollisionRules );

		if ( CollisionRules is null )
		{
			if ( IsValid ) b2World_SetCustomFilterCallback( world, null, null );
			FreeRules();
		}
		else
		{
			if ( rulesTable == null )
				rulesTable = (byte*)NativeMemory.AllocZeroed( CoreCollisionRules.TableSize );
			CollisionRules.CopyTo( new Span<byte>( rulesTable, CoreCollisionRules.TableSize ) );

			if ( IsValid ) b2World_SetCustomFilterCallback( world, CustomFilterEntry(), rulesTable );
		}

		foreach ( var body in owner.Bodies )
		{
			if ( body?.Backend is Box2DBodyBackend b2 )
				b2.RefreshTags();
		}
	}

	//The compiled rule table, in native memory: the filter reads it on the thread the world steps on, so it can neither move nor be collected.
	private byte* rulesTable;

	private void FreeRules()
	{
		if ( rulesTable == null ) return;

		NativeMemory.Free( rulesTable );
		rulesTable = null;
	}

	//-- events ---------------------------------------------------------------

	public void ProcessIntersections() => Events.Dispatch( owner, this );

	internal Box2DEvents Events { get; } = new();

	//-- joints ---------------------------------------------------------------

	internal enum JointKind
	{
		Fixed,
		Spring,
		Hinge,
		Slider,
		BallSocket,
		Control,
		Wheel,
		Upright,
		Filter,
	}

	public Physics.PhysicsJoint AddWeldJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Fixed, a, b, frameA, frameB, static h => new Physics.FixedJoint( h ) );

	public Physics.PhysicsJoint AddSpringJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Spring, a, b, frameA, frameB, static h => new Physics.SpringJoint( h ) );

	/// <summary>Box2D turns a revolute joint about +Z, which is the only way anything turns in the plane, so the frames go over as they are - no turn onto an axis, as the Box3D world needs.</summary>
	public Physics.PhysicsJoint AddRevoluteJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Hinge, a, b, frameA, frameB, static h => new Physics.HingeJoint( h ) );

	/// <summary>A slider moves along its frame's right (local X), which is what Box2D slides along.</summary>
	public Physics.PhysicsJoint AddPrismaticJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Slider, a, b, frameA, frameB, static h => new Physics.SliderJoint( h ) );

	/// <summary>A ball socket in the plane has only one way to turn: it is a revolute joint.</summary>
	public Physics.PhysicsJoint AddSphericalJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.BallSocket, a, b, frameA, frameB, static h => new Physics.BallSocketJoint( h ) );

	public Physics.PhysicsJoint AddMotorJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Control, a, b, frameA, frameB, static h => new Physics.ControlJoint( h ) );

	/// <summary>The wheel's suspension runs along its frame's right (local X), as Box2D's does.</summary>
	public Physics.PhysicsJoint AddWheelJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Wheel, a, b, frameA, frameB, static h => new Physics.WheelJoint( h ) );

	/// <summary>Keeping two bodies' angles together and nothing else. Box2D has no parallel joint; a motor joint with no linear force and an angular spring is the same constraint in the plane.</summary>
	public Physics.PhysicsJoint AddParallelJoint( PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB )
		=> Create( JointKind.Upright, a, b, frameA, frameB, static h => new Physics.UprightJoint( h ) );

	public Physics.PhysicsJoint AddFilterJoint( PhysicsBody a, PhysicsBody b )
		=> Create( JointKind.Filter, a, b, Transform.Zero, Transform.Zero, static h => new Physics.PhysicsJoint( h ) );

	private Physics.PhysicsJoint Create( JointKind kind, PhysicsBody a, PhysicsBody b, Transform frameA, Transform frameB,
		Func<HandleCreationData, Physics.PhysicsJoint> construct )
	{
		if ( !IsValid ) return null;
		if ( a?.Backend is not Box2DBodyBackend bodyA || !bodyA.IsValid ) return null;
		if ( b?.Backend is not Box2DBodyBackend bodyB || !bodyB.IsValid ) return null;

		void Fill( ref b2JointDef def )
		{
			def.bodyIdA = bodyA.Id;
			def.bodyIdB = bodyB.Id;
			def.localFrameA = bodyA.LocalFrame( frameA );
			def.localFrameB = bodyB.LocalFrame( frameB );

			//Constrained bodies are usually meant to overlap - a wheel in its arch - so they do not collide unless something says otherwise.
			def.collideConnected = false;
		}

		b2JointId id;

		switch ( kind )
		{
			case JointKind.Fixed:
			{
				var def = b2DefaultWeldJointDef();
				Fill( ref def.@base );
				id = b2CreateWeldJoint( world, &def );
				break;
			}

			case JointKind.Spring:
			{
				var def = b2DefaultDistanceJointDef();
				Fill( ref def.@base );

				//The length it was made at, as the Box3D joint takes it.
				var pa = bodyA.WorldPoint( def.@base.localFrameA.p );
				var pb = bodyB.WorldPoint( def.@base.localFrameB.p );
				def.length = MathF.Max( (pa - pb).Length, 0.005f );

				id = b2CreateDistanceJoint( world, &def );
				break;
			}

			case JointKind.Hinge:
			case JointKind.BallSocket:
			{
				var def = b2DefaultRevoluteJointDef();
				Fill( ref def.@base );
				id = b2CreateRevoluteJoint( world, &def );
				break;
			}

			case JointKind.Slider:
			{
				var def = b2DefaultPrismaticJointDef();
				Fill( ref def.@base );
				id = b2CreatePrismaticJoint( world, &def );
				break;
			}

			case JointKind.Control:
			{
				var def = b2DefaultMotorJointDef();
				Fill( ref def.@base );
				id = b2CreateMotorJoint( world, &def );
				break;
			}

			case JointKind.Wheel:
			{
				var def = b2DefaultWheelJointDef();
				Fill( ref def.@base );
				id = b2CreateWheelJoint( world, &def );
				break;
			}

			case JointKind.Upright:
			{
				var def = b2DefaultMotorJointDef();
				Fill( ref def.@base );
				def.maxVelocityForce = 0.0f;
				def.maxVelocityTorque = 0.0f;
				def.maxSpringForce = 0.0f;
				def.linearHertz = 0.0f;
				id = b2CreateMotorJoint( world, &def );
				break;
			}

			case JointKind.Filter:
			{
				var def = b2DefaultFilterJointDef();
				Fill( ref def.@base );
				id = b2CreateFilterJoint( world, &def );
				break;
			}

			default:
				return null;
		}

		if ( !b2Joint_IsValid( id ) ) return null;

		var backend = new Box2DJointBackend( id, kind, owner, a, b );
		bodyA.Joints.Add( backend );
		bodyB.Joints.Add( backend );

		var managed = construct( new HandleCreationData() );
		managed.AttachBackend( backend, owner );
		Box2DRegistry.Joints[Box2DPlane.Key( id )] = managed;
		return managed;
	}

	public void RemoveJoint( Physics.PhysicsJoint joint )
	{
		if ( joint?.Backend is Box2DJointBackend b2 )
			b2.DestroyNative();
	}

	//-- aggregates -----------------------------------------------------------

	/// <summary>A model's bodies and joints, built from its physics description exactly as the Box3D world builds them - the shapes go through the body, which lays them on the plane.</summary>
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

			CorePhysicsWorldBackend.AddPartShapes( body, part );

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

			if ( body.Backend is Box2DBodyBackend b2 )
				b2.SetAggregate( group );

			backend.Add( body, part.BoneName );
		}

		CorePhysicsWorldBackend.AddDescribedJoints( description, backend );

		return group;
	}

	public PhysicsGroup CreateAggregateInstance( string resourceName, Transform start, ulong node, PhysicsMotionType motion )
		=> CreateAggregateInstance( Model.Load( resourceName ), start, node, motion );

	public void DestroyAggregateInstance( PhysicsGroup group )
	{
		if ( group?.Backend is CorePhysicsGroupBackend core )
			core.RemoveAll();
	}

	//-- debug ----------------------------------------------------------------

	private SceneWorld debugScene;

	public SceneWorld GetDebugScene() => debugScene;
	public void SetDebugScene( SceneWorld scene ) => debugScene = scene;

	/// <summary>The shapes as the plane holds them, under the same physics_draw switches as the Box3D world, drawn at each body's depth.</summary>
	public void Draw( PhysicsWorld.DebugDrawCallback callback )
	{
		EnginePhysics.TickStatsLog();

		if ( !EnginePhysics.DebugDraw ) return;
		if ( callback is null ) return;

		foreach ( var body in owner.Bodies )
		{
			if ( body?.Backend is not Box2DBodyBackend b2 || !b2.IsValid ) continue;

			var colour = body.BodyType switch
			{
				PhysicsBodyType.Static => new Color( 0.5f, 0.5f, 0.5f ),
				PhysicsBodyType.Keyframed => new Color( 0.4f, 0.6f, 1.0f ),
				_ => body.Sleeping ? new Color( 0.4f, 0.4f, 0.7f ) : new Color( 0.4f, 1.0f, 0.4f ),
			};

			var transform = body.Transform;

			foreach ( var shape in body.Shapes )
			{
				if ( shape?.Backend is not Box2DShapeBackend s ) continue;

				var points = s.GetOutlineSegments();

				for ( var i = 0; i + 1 < points.Length; i += 2 )
				{
					var segment = new PhysicsWorld.DrawSegmentArgs
					{
						P1 = transform.PointToWorld( points[i] ),
						P2 = transform.PointToWorld( points[i + 1] ),
						Color = shape.IsTrigger ? colour.WithAlpha( 0.4f ) : colour,
					};

					var args = new PhysicsWorld.DebugDrawArgs { Segment = &segment };
					callback( &args );
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
				callback( &args );
			}
		}
	}
}

//Box2D ids back to the managed objects that own them. Shapes and bodies are keyed by every native id they
//own; one managed shape can be several Box2D shapes, when a hull had more corners than a Box2D polygon takes.
internal static class Box2DRegistry
{
	public static readonly ConcurrentDictionary<ulong, PhysicsShape> Shapes = new();
	public static readonly ConcurrentDictionary<ulong, PhysicsBody> Bodies = new();
	public static readonly ConcurrentDictionary<ulong, Physics.PhysicsJoint> Joints = new();

	public static PhysicsShape Shape( b2ShapeId id )
		=> Shapes.TryGetValue( Box2DPlane.Key( id ), out var shape ) && shape.IsValid() ? shape : null;

	public static PhysicsBody Body( b2BodyId id )
		=> Bodies.TryGetValue( Box2DPlane.Key( id ), out var body ) && body.IsValid() ? body : null;
}
