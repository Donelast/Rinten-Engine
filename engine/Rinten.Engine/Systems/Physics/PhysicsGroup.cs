using NativeEngine;

namespace Rinten
{
	/// <summary>Represents a set of <see cref="PhysicsBody">PhysicsBody</see> objects. Think ragdoll.</summary>
	[Expose, ActionGraphIgnore]
	public sealed class PhysicsGroup : IHandle
	{
		//The simulation's side of this group.
		internal IPhysicsGroupBackend Backend => backend;
		private IPhysicsGroupBackend backend;

		private bool BackendValid => backend?.IsValid ?? false;

		#region IHandle
		//A pointer to the actual native object


		//IHandle implementation
		internal int HandleValue { get; set; }
		void IHandle.HandleInit( IntPtr ptr )
		{
			//See Bind: our own simulation hands the group over itself.
		}
		void IHandle.HandleDestroy()
		{
			backend = null;
		}
		bool IHandle.HandleValid() => BackendValid;
		#endregion

		internal PhysicsGroup() { }
		internal PhysicsGroup( HandleCreationData _ ) { }

		//Bind this group to a simulation, for backends that build the managed object themselves.
		internal void AttachBackend( IPhysicsGroupBackend created )
		{
			backend = created;
		}

		/// <summary>The world in which this group belongs</summary>
		[ActionGraphInclude]
		public PhysicsWorld World => backend.GetWorld();

		//TODO: How useful is this in its current form? Should it be removed, or at least renamed to Position?
		/// <summary>Returns position of the first physics body of this group, or zero vector if it has none.</summary>
		public Vector3 Pos
		{
			get => backend.GetOrigin();
		}

		/// <summary>Returns the center of mass for this group of physics bodies.</summary>
		[ActionGraphInclude]
		public Vector3 MassCenter
		{
			get => backend.GetMassCenter();
		}

		/// <summary>Adds given amount of velocity (<see cref="PhysicsBody.ApplyForce"/>) to all physics bodies in this group.</summary>
		[ActionGraphInclude]
		public void AddVelocity( Vector3 vel )
		{
			backend.AddVelocity( vel );
		}

		/// <summary>Adds given amount of angular velocity to all physics bodies in this group.</summary>
		[ActionGraphInclude]
		public void AddAngularVelocity( Vector3 vel )
		{
			backend.AddAngularVelocity( vel );
		}

		/// <summary>Adds given amount of linear impulse (<see cref="PhysicsBody.ApplyImpulse"/>) to all physics bodies in this group.</summary>
		[ActionGraphInclude]
		public void ApplyImpulse( Vector3 vel, bool withMass = false )
		{
			for ( int i = 0; i < BodyCount; ++i )
			{
				var body = GetBody( i );
				if ( !body.IsValid() )
					continue;

				if ( withMass )
				{
					body.ApplyImpulse( vel * body.Mass );
				}
				else
				{
					body.ApplyImpulse( vel );
				}
			}
		}


		/// <summary>Adds given amount of angular linear impulse (<see cref="PhysicsBody.ApplyAngularImpulse"/>) to all physics bodies in this group.</summary>
		[ActionGraphInclude]
		public void ApplyAngularImpulse( Vector3 vel, bool withMass = false )
		{
			for ( int i = 0; i < BodyCount; ++i )
			{
				var body = GetBody( i );
				if ( !body.IsValid() )
					continue;

				if ( withMass )
				{
					body.ApplyAngularImpulse( vel * body.Mass );
				}
				else
				{
					body.ApplyAngularImpulse( vel );
				}
			}
		}

		/// <summary>Sets <see cref="PhysicsBody.Velocity"/> on all bodies of this group.</summary>
		[ActionGraphInclude]
		public Vector3 Velocity
		{
			set => backend.SetVelocity( value );
		}

		/// <summary>Sets <see cref="PhysicsBody.AngularVelocity"/> on all bodies of this group.</summary>
		[ActionGraphInclude]
		public Vector3 AngularVelocity
		{
			set => backend.SetAngularVelocity( value );
		}

		/// <summary>Physics bodies automatically go to sleep after a certain amount of time of inactivity to save on performance. You can use this to wake the body up, or prematurely send it to sleep.</summary>
		[ActionGraphInclude]
		public bool Sleeping
		{
			get => backend.IsAsleep();

			set
			{
				if ( value ) backend.PutToSleep();
				else backend.WakeUp();
			}
		}

		/// <summary>Calls <see cref="PhysicsBody.RebuildMass()"/> on all bodies of this group.</summary>
		public void RebuildMass()
		{
			for ( int i = 0; i < BodyCount; ++i )
			{
				var body = GetBody( i );
				if ( !body.IsValid() )
					continue;

				body.RebuildMass();
			}
		}

		/// <summary>The total mass of all the <b>dynamic</b> <see cref="PhysicsBody">PhysicsBodies</see> in this group. When setting the total mass, it will be set on each body proportionally to each of their old masses, i.e. if a body had 25% of previous total mass, it will have 25% of new total mass.</summary>
		[ActionGraphInclude]
		public float Mass
		{
			get => backend.GetTotalMass();
			set => backend.SetTotalMass( value );
		}

		/// <summary>Sets <see cref="PhysicsBody.LinearDamping"/> on all bodies in this group.</summary>
		[ActionGraphInclude]
		public float LinearDamping
		{
			set => backend.SetLinearDamping( value );
		}

		/// <summary>Sets <see cref="PhysicsBody.AngularDamping"/> on all bodies in this group.</summary>
		[ActionGraphInclude]
		public float AngularDamping
		{
			set => backend.SetAngularDamping( value );
		}

		/// <summary>Returns all physics bodies that belong to this physics group.</summary>
		[ActionGraphInclude]
		public IEnumerable<PhysicsBody> Bodies
		{
			get
			{
				var bodyCount = BodyCount;
				for ( int i = 0; i < bodyCount; ++i )
					yield return GetBody( i );
			}
		}

		/// <summary>Returns amount of physics bodies that belong to this physics group.</summary>
		[ActionGraphInclude]
		public int BodyCount => backend.GetBodyCount();

		//Gets a PhysicsBody at given index within this physics group. See BodyCount.
		[ActionGraphInclude, Pure]
		public PhysicsBody GetBody( int groupIndex ) => backend.GetBodyHandle( groupIndex ); //Throw on OOB

		/// <summary>Returns a <see cref="PhysicsBody"/> by its <see cref="PhysicsBody.GroupName"/> within this group.</summary>
		[ActionGraphInclude, Pure]
		public PhysicsBody GetBody( string groupName ) => backend.FindBodyByName( groupName );

		/// <summary>Any and all joints that are attached to any body in this group.</summary>
		[ActionGraphInclude]
		public IEnumerable<PhysicsJoint> Joints
		{
			get
			{
				var jointCount = backend.GetJointCount();
				for ( int i = 0; i < jointCount; ++i )
					yield return backend.GetJointHandle( i );
			}
		}

		internal void RemoveJoint( PhysicsJoint joint )
		{
			if ( joint.IsValid() )
			{
				backend.RemoveJoint( joint );
			}
		}

		/// <summary>Sets the physical properties of each <see cref="PhysicsShape">PhysicsShape</see> of this group.</summary>
		[ActionGraphInclude]
		public void SetSurface( string name )
		{
			backend.SetSurfaceProperties( name );
		}

		/// <summary>Delete this group, and all of its bodies</summary>
		public void Remove()
		{
			var physicsWorld = backend.GetWorld();
			if ( physicsWorld is null ) return;

			physicsWorld.Backend.DestroyAggregateInstance( this );
		}
	}
}
