using Rinten.Native.Box3D;
using static Rinten.Native.Box3D.Box3D;
using static Rinten.Native.Box3D.Box3DMath;

namespace Rinten;

//What a shape is, and what its surface is made of. Making one is on the body it belongs to - see CorePhysics.Bodies -
//because Box3D creates a shape onto a body and there is no such thing as a loose one. What is here is everything after that.
internal static unsafe partial class CorePhysics
{
	public static void ShapeDestroy( ulong shape )
	{
		if ( !ResolveShape( shape, out var id ) ) return;

		b3DestroyShape( id, true );
		ForgetGeometry( shape );
	}

	public static bool ShapeValid( ulong shape ) => ResolveShape( shape, out _ );

	public static ulong ShapeGetBody( ulong shape )
	{
		if ( !ResolveShape( shape, out var id ) ) return 0;
		return Pack( b3Shape_GetBody( id ) );
	}

	public static ShapeType ShapeGetType( ulong shape )
	{
		if ( !ResolveShape( shape, out var id ) ) return ShapeType.Unknown;
		return FromBox3D( b3Shape_GetType( id ) );
	}

	public static bool ShapeIsTrigger( ulong shape )
	{
		if ( !ResolveShape( shape, out var id ) ) return false;
		return b3Shape_IsSensor( id );
	}

	public static bool ShapeContactEventsEnabled( ulong shape )
	{
		if ( !ResolveShape( shape, out var id ) ) return false;
		return b3Shape_AreContactEventsEnabled( id );
	}

	public static void ShapeEnableContactEvents( ulong shape, bool enabled )
	{
		if ( !ResolveShape( shape, out var id ) ) return;
		b3Shape_EnableContactEvents( id, enabled );
	}

	public static float ShapeGetFriction( ulong shape )
	{
		if ( !ResolveShape( shape, out var id ) ) return 0.0f;
		return b3Shape_GetFriction( id );
	}

	public static void ShapeSetFriction( ulong shape, float friction )
	{
		if ( !ResolveShape( shape, out var id ) ) return;
		b3Shape_SetFriction( id, friction );
	}

	public static float ShapeGetRestitution( ulong shape )
	{
		if ( !ResolveShape( shape, out var id ) ) return 0.0f;
		return b3Shape_GetRestitution( id );
	}

	public static void ShapeSetRestitution( ulong shape, float restitution )
	{
		if ( !ResolveShape( shape, out var id ) ) return;
		b3Shape_SetRestitution( id, restitution );
	}

	public static float ShapeGetRollingResistance( ulong shape )
	{
		if ( !ResolveShape( shape, out var id ) ) return 0.0f;
		return b3Shape_GetSurfaceMaterial( id ).rollingResistance;
	}

	public static void ShapeSetRollingResistance( ulong shape, float resistance )
	{
		if ( !ResolveShape( shape, out var id ) ) return;

		b3SurfaceMaterial material = b3Shape_GetSurfaceMaterial( id );
		material.rollingResistance = resistance;
		b3Shape_SetSurfaceMaterial( id, material );
	}

	public static float ShapeGetDensity( ulong shape )
	{
		if ( !ResolveShape( shape, out var id ) ) return 0.0f;
		return b3Shape_GetDensity( id );
	}

	public static void ShapeSetDensity( ulong shape, float density, bool updateBodyMass )
	{
		if ( !ResolveShape( shape, out var id ) ) return;
		b3Shape_SetDensity( id, density, updateBodyMass );
	}

	public static Vector3 ShapeGetSurfaceVelocity( ulong shape )
	{
		if ( !ResolveShape( shape, out var id ) ) return Vector3.Zero;
		return FromBox3D( b3Shape_GetSurfaceMaterial( id ).tangentVelocity );
	}

	public static void ShapeSetSurfaceVelocity( ulong shape, Vector3 velocity )
	{
		if ( !ResolveShape( shape, out var id ) ) return;

		b3SurfaceMaterial material = b3Shape_GetSurfaceMaterial( id );
		material.tangentVelocity = ToBox3D( velocity );
		b3Shape_SetSurfaceMaterial( id, material );
	}

	/// <summary>The sphere this shape is, if it is one.</summary>
	public static bool ShapeGetSphere( ulong shape, out Vector3 center, out float radius )
	{
		center = Vector3.Zero;
		radius = 0.0f;

		if ( !ResolveShape( shape, out var id ) ) return false;
		if ( b3Shape_GetType( id ) != b3ShapeType.b3_sphereShape ) return false;

		b3Sphere sphere = b3Shape_GetSphere( id );
		center = FromBox3D( sphere.center );
		radius = sphere.radius;
		return true;
	}

	/// <summary>The capsule this shape is, if it is one.</summary>
	public static bool ShapeGetCapsule( ulong shape, out Vector3 a, out Vector3 b, out float radius )
	{
		a = Vector3.Zero;
		b = Vector3.Zero;
		radius = 0.0f;

		if ( !ResolveShape( shape, out var id ) ) return false;
		if ( b3Shape_GetType( id ) != b3ShapeType.b3_capsuleShape ) return false;

		b3Capsule capsule = b3Shape_GetCapsule( id );
		a = FromBox3D( capsule.center1 );
		b = FromBox3D( capsule.center2 );
		radius = capsule.radius;
		return true;
	}

	/// <summary>The sphere this shape is, as the engine's own type. Default if it is not one.</summary>
	public static Sphere ShapeAsSphere( ulong shape )
	{
		if ( !ShapeGetSphere( shape, out var center, out var radius ) ) return default;
		return new Sphere( center, radius );
	}

	/// <summary>The capsule this shape is, as the engine's own type. Default if it is not one.</summary>
	public static Capsule ShapeAsCapsule( ulong shape )
	{
		if ( !ShapeGetCapsule( shape, out var a, out var b, out var radius ) ) return default;
		return new Capsule( a, b, radius );
	}

	public static BBox ShapeGetBounds( ulong shape )
	{
		if ( !ResolveShape( shape, out var id ) ) return default;
		return FromBox3D( b3Shape_GetAABB( id ) );
	}

	/// <summary>One entry of the per-triangle material table a mesh or height field carries.</summary>
	public static void ShapeSetMeshMaterial( ulong shape, int slot,
		float friction, float restitution, float rollingResistance )
	{
		if ( !ResolveShape( shape, out var id ) ) return;

		if ( slot < 0 || slot >= b3Shape_GetMeshMaterialCount( id ) ) return;

		b3SurfaceMaterial material = b3Shape_GetMeshSurfaceMaterial( id, slot );
		material.friction = friction;
		material.restitution = restitution;
		material.rollingResistance = rollingResistance;

		b3Shape_SetMeshMaterial( id, material, slot );
	}

	//Box3D decides whether a shape is a sensor when it is created and will not be told otherwise, so changing it means making a second shape with the same geometry and destroying the first. The caller gets the new handle and must forget the old one.
	/// <summary>The same shape again, made a trigger or made solid.</summary>
	public static ulong ShapeRebuildTrigger( ulong shape, bool trigger )
	{
		if ( !ResolveShape( shape, out var id ) ) return 0;

		b3BodyId owner = b3Shape_GetBody( id );

		b3ShapeDef def = MakeShapeDef( b3Shape_GetDensity( id ), trigger );
		def.baseMaterial = b3Shape_GetSurfaceMaterial( id );

		//A mesh or height field also carries a table of per-triangle materials, and the
		//new shape has to be given the same one or the terrain forgets what it is made of.
		int materialCount = b3Shape_GetMeshMaterialCount( id );
		b3SurfaceMaterial[] materials = null;

		if ( materialCount > 0 )
		{
			materials = new b3SurfaceMaterial[materialCount];
			for ( int i = 0; i < materialCount; ++i )
				materials[i] = b3Shape_GetMeshSurfaceMaterial( id, i );
		}

		//The old shape's mass is already in the body's. Letting the new one add its own on top would double
		//it, so the rebuild leaves the mass alone and the managed side puts it back if it was ever set by hand.
		def.updateBodyMass = false;

		b3ShapeId replacement = default;

		fixed ( b3SurfaceMaterial* table = materials )
		{
			if ( materials is not null )
			{
				def.materials = table;
				def.materialCount = materialCount;
			}

			switch ( b3Shape_GetType( id ) )
			{
				case b3ShapeType.b3_sphereShape:
				{
					b3Sphere sphere = b3Shape_GetSphere( id );
					replacement = b3CreateSphereShape( owner, &def, &sphere );
					break;
				}

				case b3ShapeType.b3_capsuleShape:
				{
					b3Capsule capsule = b3Shape_GetCapsule( id );
					replacement = b3CreateCapsuleShape( owner, &def, &capsule );
					break;
				}

				case b3ShapeType.b3_hullShape:
				{
					//Hulls are cloned on create, so the old shape can still be destroyed afterwards without taking the new one's points with it.
					b3HullData* hull = b3Shape_GetHull( id );
					if ( hull != null ) replacement = b3CreateHullShape( owner, &def, hull );
					break;
				}

				case b3ShapeType.b3_meshShape:
				{
					b3Mesh mesh = b3Shape_GetMesh( id );
					if ( mesh.data != null )
						replacement = b3CreateMeshShape( owner, &def, mesh.data, mesh.scale );
					break;
				}

				case b3ShapeType.b3_heightShape:
				{
					b3HeightFieldData* field = b3Shape_GetHeightField( id );
					if ( field != null ) replacement = b3CreateHeightFieldShape( owner, &def, field );
					break;
				}

				default:
					break;
			}
		}

		if ( B3_IS_NULL( replacement ) ) return 0;

		b3Shape_SetUserData( replacement, b3Shape_GetUserData( id ) );

		//Meshes and height fields are borrowed, not cloned, so the new shape is now the one keeping the geometry alive. Handing
		//the entry over rather than copying it means destroying the old shape does not free what the new one is still pointing at.
		lock ( borrowedLock )
		{
			if ( borrowedGeometry.Remove( shape, out var borrowed ) )
				borrowedGeometry[Pack( replacement )] = borrowed;
		}

		return Pack( replacement );
	}
}
