namespace Rinten;

//What PhysicsWorld asks of the simulation, in managed types only
internal interface IPhysicsWorldBackend
{
	bool IsValid { get; }

	//The collision rules as this world filters contacts with them, or null while everything collides - what a trace that
	//takes an object's rules resolves its hits against, so the two cannot disagree. See CoreCollisionRules.Resolve.
	CoreCollisionRules CompiledCollisionRules => null;

	//Tear down the simulation. Takes its owner because a world is destroyed through the physics system rather than through the world itself.
	void Destroy( PhysicsWorld owner );

	//-- bodies ---------------------------------------------------------------

	//Give body a home in this world. The managed object already exists; this attaches the simulation's side of it.
	void AddBody( PhysicsBody body );

	void RemoveBody( PhysicsBody body );

	//The static body every world has, that world geometry hangs off.
	PhysicsBody GetWorldReferenceBody();
	void SetWorldReferenceBody( PhysicsBody body );

	//-- simulation -----------------------------------------------------------

	void StepSimulation( float timestep, int steps );

	Vector3 GetGravity();
	void SetGravity( Vector3 gravity );

	PhysicsSimulationMode GetSimulation();
	void SetSimulation( PhysicsSimulationMode simulation );

	bool IsSleepingEnabled();
	void EnableSleeping();
	void DisableSleeping();

	void SetMaximumLinearSpeed( float speed );

	void SetCollisionRulesFromJson( string rules );

	//Drain this step's touch/trigger/hit events into a callback. The pointer is an unmanaged function pointer the caller keeps alive.
	void ProcessIntersections();

	//-- joints ---------------------------------------------------------------

	Physics.PhysicsJoint AddWeldJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );
	Physics.PhysicsJoint AddSpringJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );
	Physics.PhysicsJoint AddRevoluteJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );
	Physics.PhysicsJoint AddPrismaticJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );
	Physics.PhysicsJoint AddSphericalJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );
	Physics.PhysicsJoint AddMotorJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );
	Physics.PhysicsJoint AddWheelJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );
	Physics.PhysicsJoint AddParallelJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 );
	Physics.PhysicsJoint AddFilterJoint( PhysicsBody body1, PhysicsBody body2 );

	void RemoveJoint( Physics.PhysicsJoint joint );

	//-- aggregates -----------------------------------------------------------

	//Instance a model's baked collision into this world.
	PhysicsGroup CreateAggregateInstance( Model model, Transform start, ulong sceneNodeHandle, PhysicsMotionType motionType );

	//Instance baked collision named by resource path, for map geometry.
	PhysicsGroup CreateAggregateInstance( string resourceName, Transform start, ulong sceneNodeHandle, PhysicsMotionType motionType );

	void DestroyAggregateInstance( PhysicsGroup group );

	//-- overlap queries ------------------------------------------------------ "What is inside this volume", as opposed to a trace's
	//"what is along this line". Results are appended to a caller-owned list so a caller that runs these every frame can reuse one.

	int OverlapSphere( Vector3 center, float radius, List<PhysicsShape> results );
	int OverlapBox( BBox box, List<PhysicsShape> results );

	//Overlap the convex hull of a set of points - a frustum, say.
	int OverlapConvex( ReadOnlySpan<Vector3> points, List<PhysicsShape> results );

	//-- traces ---------------------------------------------------------------

	//The first hit along a trace that passes the request's filters, or a miss ending where it
	//was aimed. With sweptBody, that body's own shapes are swept rather than the request's shape.
	PhysicsTraceResult Trace( in PhysicsTrace.Request request, Func<PhysicsShape, bool> filter, PhysicsBody sweptBody );

	//Every hit along a trace that passes, nearest first.
	PhysicsTraceResult[] TraceAll( in PhysicsTrace.Request request, Func<PhysicsShape, bool> filter, PhysicsBody sweptBody );

	//-- debug ----------------------------------------------------------------

	SceneWorld GetDebugScene();
	void SetDebugScene( SceneWorld world );

	//Emit debug geometry for this world into a callback.
	void Draw( PhysicsWorld.DebugDrawCallback callback );
}
