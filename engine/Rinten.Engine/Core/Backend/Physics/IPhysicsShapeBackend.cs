namespace Rinten;

//Everything PhysicsShape asks of the simulation underneath it.
internal interface IPhysicsShapeBackend
{
	bool IsValid { get; }

	PhysicsBody GetBody();

	PhysicsShapeType GetShapeType();

	//-- collision filtering --------------------------------------------------

	//Bits of CollisionFunctionMask: solid contact, touch events, persisting touch events.
	//Kept as a mask because that is how the shape stores it and callers flip single bits.
	byte GetCollisionFunctionMask();
	void AddCollisionFunctionMask( byte mask );
	void RemoveCollisionFunctionMask( byte mask );

	bool IsTrigger();
	void SetTrigger( bool trigger );

	void SetIgnoreTraces( bool ignore );
	void SetHasNoMass( bool noMass );

	//-- tags -----------------------------------------------------------------

	bool HasTag( uint tag );
	bool AddTag( uint tag );
	bool RemoveTag( uint tag );
	bool ClearTags();

	//-- material -------------------------------------------------------------

	string GetMaterialName();
	void SetMaterialIndex( string name );

	//Per-triangle material, for meshes and heightfields.
	void SetSurfaceIndex( int surfaceIndex, int index );

	float GetFriction();
	void SetFriction( float friction );

	void SetElasticity( float elasticity );
	void SetRollingResistance( float resistance );

	//Surface velocity, for conveyor belts and the like.
	Vector3 GetLocalVelocity();
	void SetLocalVelocity( Vector3 velocity );

	//-- geometry -------------------------------------------------------------

	Sphere AsSphere();
	Capsule AsCapsule();

	BBox BuildBounds();

	//Bounds with the shape at the origin, unrotated.
	BBox LocalBounds();

	void UpdateSphereShape( Vector3 center, float radius );
	void UpdateCapsuleShape( Vector3 center1, Vector3 center2, float radius );
	void UpdateBoxShape( Vector3 center, Rotation rotation, Vector3 extents );
	void UpdateHullShape( Vector3 position, Rotation rotation, int vertexCount, IntPtr vertices );
	void UpdateMeshShape( int vertexCount, IntPtr vertices, int indexCount, IntPtr indices );
	void UpdateHeightShape( IntPtr heights, IntPtr materials, int x, int y, int width, int height, float sizeScale, float heightScale );

	//Triangles for this shape, for debug drawing and for building navmeshes. Returns managed arrays: how the simulation stores them is its business.
	void GetTriangulation( out Vector3[] positions, out uint[] indices );

	//Appends body-local triangles near world bounds to reused buffers; used per shape per navmesh tile
	void AppendTriangulation( BBox bounds, List<Vector3> positions, List<int> indices );

	//Line segments outlining the shape, as consecutive pairs of points.
	Vector3[] GetOutlineSegments();

	//-- queries --------------------------------------------------------------

	bool IsTouching( PhysicsShape shape, bool triggersOnly );

	void ResetProxy();
}
