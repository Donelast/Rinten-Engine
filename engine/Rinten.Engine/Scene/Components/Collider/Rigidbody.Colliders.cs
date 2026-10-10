namespace Rinten;

sealed public partial class Rigidbody : Component, Component.ExecuteInEditor, IGameObjectNetworkEvents, IScenePhysicsEvents
{
	readonly HashSet<Collider> Colliders = new();

	//Lets child colliders reconfigure; each attaches to its nearest parent Rigidbody
	void BroadcastToColliders()
	{
		foreach ( var c in GetComponentsInChildren<Collider>( false, true ) )
		{
			c.OnRigidBodyEnabled( this );
		}
	}

	//Tell all the colliders we're fucking off. Clear the list.
	void FreeColliders()
	{
		foreach ( var c in Colliders )
		{
			c.OnRigidBodyDisabled( this );
		}

		Colliders.Clear();
	}

	//Called by a collider to tell the Rigidbody that it's part of it
	internal void OnColliderAdded( Collider collider )
	{
		EnsureBodyCreated();

		//when adding a collider we want to sync to the transform so it won't be offset by a lerp amount in any way
		if ( _body.IsValid() )
		{
			_body.Transform = WorldTransform;
		}

		Colliders.Add( collider );

		//Reapply these so they propagate down to the shapes
		if ( _body.IsValid() )
		{
			_body.EnableTouch = CollisionEventsEnabled;
			_body.EnableTouchPersists = CollisionUpdateEventsEnabled;
		}
	}

	//Called by a collider to tell the Rigidbody that it's no longer part of it
	internal void OnColliderRemoved( Collider collider )
	{
		Colliders.Remove( collider );
	}
}

