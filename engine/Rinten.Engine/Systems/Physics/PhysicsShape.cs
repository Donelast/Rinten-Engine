using NativeEngine;
using System.Runtime.InteropServices;

namespace Rinten;

/// <summary>Represents a basic, convex shape. A <see cref="PhysicsBody">PhysicsBody</see> consists of one or more of these.</summary>
[Expose, ActionGraphIgnore]
public sealed partial class PhysicsShape : IHandle
{
	//The simulation's side of this shape.
	internal IPhysicsShapeBackend Backend => backend;
	private IPhysicsShapeBackend backend;

	private bool BackendValid => backend?.IsValid ?? false;

	#region IHandle
	//The native handle. Only for navmesh generation, which still fills native containers. Default with a managed backend.


	//IHandle implementation
	void IHandle.HandleInit( IntPtr ptr )
	{
		//See Bind: our own simulation hands the shape over itself.
	}

	void IHandle.HandleDestroy()
	{
		backend = null;
	}

	bool IHandle.HandleValid() => BackendValid;
	#endregion

	internal PhysicsShape( HandleCreationData _ )
	{
		Tags = new TagAccessor( this );
	}

	//A shape with no backend yet, for a backend that creates the managed object first. See PhysicsBody.CreateDetached.
	internal static PhysicsShape CreateDetached() => new( new HandleCreationData() );

	//Bind this shape to a simulation.
	internal void AttachBackend( IPhysicsShapeBackend created ) => backend = created;

	/// <summary>The physics body we belong to.</summary>
	[ActionGraphInclude]
	public PhysicsBody Body => backend.GetBody();

	[Obsolete]
	public Vector3 Scale => 1.0f;

	internal PhysicsShapeType ShapeType => backend.GetShapeType();

	internal BBox LocalBounds => backend.LocalBounds();
	internal BBox BuildBounds() => backend.BuildBounds();

	//The bone index that this physics shape represents
	internal int BoneIndex { get; set; } = -1;

	/// <summary>The collider object that created / owns this shape</summary>
	[ActionGraphInclude]
	public Collider Collider { get; set; }

	/// <summary>This is a trigger (!)</summary>
	[ActionGraphInclude]
	public bool IsTrigger
	{
		get => backend.IsTrigger();
		set => backend.SetTrigger( value );
	}

	/// <summary>Set the local velocity of the surface so things can slide along it, like a conveyor belt</summary>
	public Vector3 SurfaceVelocity
	{
		get => backend.GetLocalVelocity();
		set => backend.SetLocalVelocity( value );
	}

	/// <summary>Enable contact, trace and touch</summary>
	public void EnableAllCollision()
	{
		var mask = CollisionFunctionMask.EnableSolidContact | CollisionFunctionMask.EnableTouchEvent;
		backend.AddCollisionFunctionMask( (byte)mask );
	}

	/// <summary>Disable contact, trace and touch</summary>
	public void DisableAllCollision()
	{
		var mask = CollisionFunctionMask.EnableSolidContact | CollisionFunctionMask.EnableTouchEvent;
		backend.RemoveCollisionFunctionMask( (byte)mask );
	}

	void SetCollisionFunctionFlag( CollisionFunctionMask flag, bool on ) { if ( on ) { backend.AddCollisionFunctionMask( (byte)flag ); } else { backend.RemoveCollisionFunctionMask( (byte)flag ); } }
	bool GetCollisionFunctionFlag( CollisionFunctionMask flag ) => (backend.GetCollisionFunctionMask() & (byte)flag) != 0;

	/// <summary>Controls whether this shape has solid collisions.</summary>
	public bool EnableSolidCollisions
	{
		get { return GetCollisionFunctionFlag( CollisionFunctionMask.EnableSolidContact ); }
		set { SetCollisionFunctionFlag( CollisionFunctionMask.EnableSolidContact, value ); }
	}

	/// <summary>Controls whether this shape can fire touch events for its owning entity. (Entity.StartTouch, Touch and EndTouch)</summary>
	public bool EnableTouch
	{
		get { return GetCollisionFunctionFlag( CollisionFunctionMask.EnableTouchEvent ); }
		set { SetCollisionFunctionFlag( CollisionFunctionMask.EnableTouchEvent, value ); }
	}

	/// <summary>Controls whether this shape can fire continuous touch events for its owning entity (i.e. calling Entity.Touch every frame). Persist events are only sent while the contact is awake, sleeping contacts don't emit them.</summary>
	public bool EnableTouchPersists
	{
		get { return GetCollisionFunctionFlag( CollisionFunctionMask.EnableTouchPersists ); }
		set { SetCollisionFunctionFlag( CollisionFunctionMask.EnableTouchPersists, value ); }
	}

	/// <summary>Is this a MeshShape</summary>
	public bool IsMeshShape => ShapeType == PhysicsShapeType.SHAPE_MESH;

	/// <summary>Is this a HullShape</summary>
	public bool IsHullShape => ShapeType == PhysicsShapeType.SHAPE_HULL;

	/// <summary>Is this a SphereShape</summary>
	public bool IsSphereShape => ShapeType == PhysicsShapeType.SHAPE_SPHERE;

	/// <summary>Is this a CapsuleShape</summary>
	public bool IsCapsuleShape => ShapeType == PhysicsShapeType.SHAPE_CAPSULE;

	/// <summary>Is this a HeightfieldShape</summary>
	public bool IsHeightfieldShape => ShapeType == PhysicsShapeType.SHAPE_HEIGHTFIELD;

	/// <summary>Get sphere properties if we're a sphere type</summary>
	public Sphere Sphere
	{
		get
		{
			if ( !IsSphereShape )
				throw new Exception( "PhysicsShape is not type Sphere" );

			return backend.AsSphere();
		}
	}

	/// <summary>Get capsule properties if we're a capsule type</summary>
	public Capsule Capsule
	{
		get
		{
			if ( !IsCapsuleShape )
				throw new Exception( "PhysicsShape is not type Capsule" );

			return backend.AsCapsule();
		}
	}

	//Recreate the collision mesh (Only if this physics shape is type Capsule)
	internal void UpdateCapsuleShape( Vector3 center1, Vector3 center2, float radius )
	{
		if ( !IsCapsuleShape && !IsSphereShape )
			throw new Exception( "PhysicsShape is not type Capsule" );

		backend.UpdateCapsuleShape( center1, center2, radius );

		Dirty();
	}

	//Recreate the collision mesh (Only if this physics shape is type Hull)
	internal void UpdateBoxShape( Vector3 center, Rotation rotation, Vector3 extents )
	{
		if ( !IsHullShape )
			throw new Exception( "PhysicsShape is not type hull" );

		backend.UpdateBoxShape( center, rotation, extents );

		Dirty();
	}

	/// <summary>Recreate the collision mesh (Only if this physics shape is type Mesh)</summary>
	public void UpdateMesh( List<Vector3> vertices, List<int> indices )
	{
		UpdateMesh( CollectionsMarshal.AsSpan( vertices ), CollectionsMarshal.AsSpan( indices ) );
	}

	/// <summary>Recreate the mesh of the shape (Only if this physics shape is type Mesh)</summary>
	public unsafe void UpdateMesh( Span<Vector3> vertices, Span<int> indices )
	{
		if ( ShapeType != PhysicsShapeType.SHAPE_MESH )
			throw new Exception( "PhysicsShape is not type Mesh" );

		if ( vertices.Length == 0 )
			return;

		if ( indices.Length == 0 )
			return;

		var vertexCount = vertices.Length;

		foreach ( var i in indices )
		{
			if ( i < 0 || i >= vertexCount )
				throw new ArgumentOutOfRangeException( $"Index ({i}) out of range ({vertexCount - 1})" );
		}

		fixed ( Vector3* vertices_ptr = vertices )
		fixed ( int* indices_ptr = indices )
		{
			backend.UpdateMeshShape( vertices.Length, (IntPtr)vertices_ptr, indices.Length, (IntPtr)indices_ptr );
		}

		Dirty();
	}

	/// <summary>Recreate the hull of the shape (Only if this physics shape is type Hull)</summary>
	public unsafe void UpdateHull( Vector3 position, Rotation rotation, Span<Vector3> points )
	{
		if ( ShapeType != PhysicsShapeType.SHAPE_HULL )
			throw new Exception( "PhysicsShape is not type Hull" );

		if ( points.Length == 0 )
			return;

		fixed ( Vector3* points_ptr = points )
		{
			backend.UpdateHullShape( position, rotation, points.Length, (IntPtr)points_ptr );
		}

		Dirty();
	}

	/// <summary>Controls physical properties of this shape.</summary>
	public string SurfaceMaterial
	{
		get => backend.GetMaterialName();
		set
		{
			//The model's surface arrives here as a name; the resolved resource is looked up again on the next read.
			_surface = null;
			backend.SetMaterialIndex( value );
		}
	}

	Surface _surface;

	[ActionGraphInclude]
	public Surface Surface
	{
		get
		{
			if ( _surface is null && !string.IsNullOrWhiteSpace( SurfaceMaterial ) )
			{
				_surface = Surface.FindByName( SurfaceMaterial );
			}

			return _surface;
		}
		set
		{
			_surface = value;

			UpdateSurface();
		}
	}

	internal void UpdateSurface()
	{
		backend.SetMaterialIndex( _surface?.ResourceName );
	}

	//Put the surface's friction and elasticity back, after a per-collider override of them was cleared.
	//Whatever material the shape carries - a collider's Surface or the model's own - is what comes back.
	internal void ReapplySurface()
	{
		backend.SetMaterialIndex( backend.GetMaterialName() );
	}

	/// <summary>Multiple surfaces referenced by mesh or heightfield collision.</summary>
	[ActionGraphInclude]
	public Surface[] Surfaces
	{
		set => SetSurfaces( value );
	}

	private unsafe void SetSurfaces( Surface[] surfaces )
	{
		if ( surfaces is null )
			return;

		for ( var i = 0; i < surfaces.Length; i++ )
		{
			var surface = surfaces[i];
			backend.SetSurfaceIndex( surface is null ? -1 : surface.Index, i );
		}
	}

	/// <summary>Remove this shape. After calling this the shape should be considered released and not used again.</summary>
	public void Remove()
	{
		if ( !BackendValid ) return;
		if ( !Body.IsValid() ) return;

		Body.RemoveShape( this );
	}

	/// <summary>Triangulate this shape.</summary>
	public void Triangulate( out Vector3[] positions, out uint[] indices )
	{
		backend.GetTriangulation( out positions, out indices );
	}

	internal IEnumerable<Line> GetOutline()
	{
		var points = backend.GetOutlineSegments();

		for ( int i = 0; i + 1 < points.Length; i += 2 )
		{
			yield return new Line( points[i], points[i + 1] );
		}
	}

	/// <summary>The friction value</summary>
	public float Friction
	{
		get => backend.GetFriction();
		set => backend.SetFriction( value );
	}

	internal float Elasticity
	{
		set => backend.SetElasticity( value );
	}

	internal float RollingResistance
	{
		set => backend.SetRollingResistance( value );
	}

	void Dirty()
	{
		OnDirty?.Invoke();
	}

	//Called when anything significant changed about this physics object. Like its position, or its enabled status.
	internal Action OnDirty;

	internal bool IsTouching( PhysicsShape shape, bool triggersOnly )
	{
		if ( !shape.IsValid() )
			return false;

		return backend.IsTouching( shape, triggersOnly );
	}
}
