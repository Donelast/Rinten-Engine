namespace Rinten;

//Everything PhysicsGroup asks of the simulation underneath it. A group is a set of bodies and the
//joints between them - a ragdoll, a vehicle - instanced together from one piece of baked collision.
internal interface IPhysicsGroupBackend
{
	bool IsValid { get; }

	PhysicsWorld GetWorld();

	//-- whole-group state ----------------------------------------------------

	bool IsAsleep();
	void WakeUp();
	void PutToSleep();

	void SetVelocity( Vector3 velocity );
	void AddVelocity( Vector3 velocity );

	void SetAngularVelocity( Vector3 velocity );
	void AddAngularVelocity( Vector3 velocity );

	void SetLinearDamping( float damping );
	void SetAngularDamping( float damping );

	float GetTotalMass();
	void SetTotalMass( float mass );

	void SetSurfaceProperties( string name );

	//Origin of the group's first body.
	Vector3 GetOrigin();
	Vector3 GetMassCenter();

	//-- members --------------------------------------------------------------

	int GetBodyCount();
	PhysicsBody GetBodyHandle( int index );
	string GetBodyName( int index );
	uint GetBodyNameHash( int index );
	PhysicsBody GetBodyByNameHash( int hash );
	int GetBodyIndex( PhysicsBody body );
	PhysicsBody FindBodyByName( string name );

	int GetJointCount();
	Physics.PhysicsJoint GetJointHandle( int index );
	void RemoveJoint( Physics.PhysicsJoint joint );
}
