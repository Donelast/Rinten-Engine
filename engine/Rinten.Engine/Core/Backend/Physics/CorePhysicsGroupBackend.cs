using Rinten.Physics;

namespace Rinten;

//A physics group on our core: just the list of ordinary bodies and joints built for a model
internal sealed class CorePhysicsGroupBackend : IPhysicsGroupBackend
{
	private readonly PhysicsWorld world;
	private readonly List<PhysicsBody> bodies = new();
	private readonly List<string> names = new();
	private readonly List<PhysicsJoint> joints = new();

	private bool removed;

	public CorePhysicsGroupBackend( PhysicsWorld world )
	{
		this.world = world;
	}

	/// <summary>A group is alive while it still has a body. Removing the last one leaves nothing for anything to be asked of.</summary>
	public bool IsValid => !removed && bodies.Count > 0;

	public PhysicsWorld GetWorld() => world;

	internal void Add( PhysicsBody body, string name )
	{
		bodies.Add( body );
		names.Add( name ?? string.Empty );
	}

	internal void Add( PhysicsJoint joint ) => joints.Add( joint );

	//Take the whole group out of the world, joints first.
	internal void RemoveAll()
	{
		if ( removed ) return;
		removed = true;

		foreach ( var joint in joints )
			joint?.Remove();

		joints.Clear();

		//The bodies go last: a joint whose bodies are gone has nothing to hold.
		foreach ( var body in bodies )
			body?.Remove();

		bodies.Clear();
		names.Clear();
	}

	private IEnumerable<PhysicsBody> Live
	{
		get
		{
			foreach ( var body in bodies )
			{
				if ( body.IsValid() )
					yield return body;
			}
		}
	}

	//-- whole-group state ----------------------------------------------------

	/// <summary>Asleep only when every body is: one moving part means the group moves.</summary>
	public bool IsAsleep()
	{
		var any = false;

		foreach ( var body in Live )
		{
			any = true;
			if ( !body.Sleeping ) return false;
		}

		return any;
	}

	public void WakeUp()
	{
		foreach ( var body in Live )
			body.Sleeping = false;
	}

	public void PutToSleep()
	{
		foreach ( var body in Live )
			body.Sleeping = true;
	}

	public void SetVelocity( Vector3 velocity )
	{
		foreach ( var body in Live )
			body.Velocity = velocity;
	}

	public void AddVelocity( Vector3 velocity )
	{
		foreach ( var body in Live )
			body.Velocity += velocity;
	}

	public void SetAngularVelocity( Vector3 velocity )
	{
		foreach ( var body in Live )
			body.AngularVelocity = velocity;
	}

	public void AddAngularVelocity( Vector3 velocity )
	{
		foreach ( var body in Live )
			body.AngularVelocity += velocity;
	}

	public void SetLinearDamping( float damping )
	{
		foreach ( var body in Live )
			body.LinearDamping = damping;
	}

	public void SetAngularDamping( float damping )
	{
		foreach ( var body in Live )
			body.AngularDamping = damping;
	}

	/// <summary>Only the dynamic bodies count. A keyframed one is moved by whatever drives it and its mass never enters the simulation.</summary>
	public float GetTotalMass()
	{
		var total = 0.0f;

		foreach ( var body in Live )
		{
			if ( body.BodyType == PhysicsBodyType.Dynamic )
				total += body.Mass;
		}

		return total;
	}

	/// <summary>Share the new total out the way the old one was shared, so a body that was a quarter of the weight stays a quarter of it.</summary>
	public void SetTotalMass( float mass )
	{
		var total = GetTotalMass();
		if ( total <= 0.0f ) return;

		var scale = mass / total;

		foreach ( var body in Live )
		{
			if ( body.BodyType == PhysicsBodyType.Dynamic )
				body.Mass *= scale;
		}
	}

	public void SetSurfaceProperties( string name )
	{
		foreach ( var body in Live )
			body.SurfaceMaterial = name;
	}

	public Vector3 GetOrigin()
	{
		foreach ( var body in Live )
			return body.Position;

		return Vector3.Zero;
	}

	/// <summary>The group's centre of mass: each body's, weighted by its mass.</summary>
	public Vector3 GetMassCenter()
	{
		var total = 0.0f;
		var weighted = Vector3.Zero;

		foreach ( var body in Live )
		{
			var mass = body.BodyType == PhysicsBodyType.Dynamic ? body.Mass : 0.0f;
			if ( mass <= 0.0f ) continue;

			total += mass;
			weighted += body.MassCenter * mass;
		}

		return total > 0.0f ? weighted / total : GetOrigin();
	}

	//-- members --------------------------------------------------------------

	public int GetBodyCount() => bodies.Count;

	public PhysicsBody GetBodyHandle( int index )
		=> index >= 0 && index < bodies.Count ? bodies[index] : null;

	public string GetBodyName( int index )
		=> index >= 0 && index < names.Count ? names[index] : null;

	//A name reduced to a number, for looking bodies up without comparing strings. Other backends may hash these differently and will not agree,
	//but nothing compares one backend's hashes against the other's - the number only has to mean the same thing to whoever handed it out.
	private static uint Hash( string name )
	{
		uint hash = 2166136261;

		foreach ( var c in name )
		{
			hash ^= char.ToLowerInvariant( c );
			hash *= 16777619;
		}

		return hash;
	}

	public uint GetBodyNameHash( int index ) => Hash( GetBodyName( index ) ?? string.Empty );

	public PhysicsBody GetBodyByNameHash( int hash )
	{
		for ( var i = 0; i < names.Count; ++i )
		{
			if ( Hash( names[i] ) == unchecked((uint)hash) )
				return bodies[i];
		}

		return null;
	}

	public int GetBodyIndex( PhysicsBody body ) => bodies.IndexOf( body );

	public PhysicsBody FindBodyByName( string name )
	{
		for ( var i = 0; i < names.Count; ++i )
		{
			if ( string.Equals( names[i], name, StringComparison.OrdinalIgnoreCase ) )
				return bodies[i];
		}

		return null;
	}

	public int GetJointCount() => joints.Count;

	public PhysicsJoint GetJointHandle( int index )
		=> index >= 0 && index < joints.Count ? joints[index] : null;

	public void RemoveJoint( PhysicsJoint joint )
	{
		joints.Remove( joint );
		joint?.Remove();
	}
}
