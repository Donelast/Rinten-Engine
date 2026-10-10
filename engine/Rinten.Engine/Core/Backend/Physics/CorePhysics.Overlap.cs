using Rinten.Native.Box3D;
using static Rinten.Native.Box3D.Box3D;
using static Rinten.Native.Box3D.Box3DMath;

namespace Rinten;

//Penetration and shape wireframes via Box3D's collide functions, without stepping a world
internal static unsafe partial class CorePhysics
{
	//One shape's geometry, read back in its body's frame, in whichever of the three convex forms Box3D's collide functions take.
	private struct ConvexShape
	{
		public b3ShapeType Type;
		public b3Sphere Sphere;
		public b3Capsule Capsule;
		public b3HullData* Hull;
	}

	private static bool ReadConvex( b3ShapeId id, out ConvexShape shape )
	{
		shape = default;
		shape.Type = b3Shape_GetType( id );

		switch ( shape.Type )
		{
			case b3ShapeType.b3_sphereShape: shape.Sphere = b3Shape_GetSphere( id ); return true;
			case b3ShapeType.b3_capsuleShape: shape.Capsule = b3Shape_GetCapsule( id ); return true;
			case b3ShapeType.b3_hullShape: shape.Hull = b3Shape_GetHull( id ); return shape.Hull != null;
			default: return false;
		}
	}

	//The deepest point of the manifold, or zero if the two do not touch. The normal comes back in A's frame, pointing from A towards B.
	private static float CollideConvex( in ConvexShape a, in ConvexShape b, b3Transform bToA, out b3Vec3 normal )
	{
		normal = default;

		b3LocalManifoldPoint* points = stackalloc b3LocalManifoldPoint[B3Constants.B3_MAX_MANIFOLD_POINTS];
		b3LocalManifold manifold = default;
		manifold.points = points;

		b3SimplexCache simplexCache = default;
		b3SATCache satCache = default;

		//Box3D has a collide function per ordered pair of kinds, and only in one order. Where ours is the
		//other way round the query is run reversed and the answer turned around, which is what flip tracks.
		bool flip = false;

		fixed ( ConvexShape* pa = &a, pb = &b )
		{
			const int Capacity = B3Constants.B3_MAX_MANIFOLD_POINTS;

			if ( a.Type == b3ShapeType.b3_sphereShape && b.Type == b3ShapeType.b3_sphereShape )
			{
				b3CollideSpheres( &manifold, Capacity, &pa->Sphere, &pb->Sphere, bToA );
			}
			else if ( a.Type == b3ShapeType.b3_capsuleShape && b.Type == b3ShapeType.b3_sphereShape )
			{
				b3CollideCapsuleAndSphere( &manifold, Capacity, &pa->Capsule, &pb->Sphere, bToA );
			}
			else if ( a.Type == b3ShapeType.b3_sphereShape && b.Type == b3ShapeType.b3_capsuleShape )
			{
				b3CollideCapsuleAndSphere( &manifold, Capacity, &pb->Capsule, &pa->Sphere, b3InvertTransform( bToA ) );
				flip = true;
			}
			else if ( a.Type == b3ShapeType.b3_capsuleShape && b.Type == b3ShapeType.b3_capsuleShape )
			{
				b3CollideCapsules( &manifold, Capacity, &pa->Capsule, &pb->Capsule, bToA );
			}
			else if ( a.Type == b3ShapeType.b3_hullShape && b.Type == b3ShapeType.b3_sphereShape )
			{
				b3CollideHullAndSphere( &manifold, Capacity, a.Hull, &pb->Sphere, bToA, &simplexCache );
			}
			else if ( a.Type == b3ShapeType.b3_sphereShape && b.Type == b3ShapeType.b3_hullShape )
			{
				b3CollideHullAndSphere( &manifold, Capacity, b.Hull, &pa->Sphere, b3InvertTransform( bToA ), &simplexCache );
				flip = true;
			}
			else if ( a.Type == b3ShapeType.b3_hullShape && b.Type == b3ShapeType.b3_capsuleShape )
			{
				b3CollideHullAndCapsule( &manifold, Capacity, a.Hull, &pb->Capsule, bToA, &simplexCache );
			}
			else if ( a.Type == b3ShapeType.b3_capsuleShape && b.Type == b3ShapeType.b3_hullShape )
			{
				b3CollideHullAndCapsule( &manifold, Capacity, b.Hull, &pa->Capsule, b3InvertTransform( bToA ), &simplexCache );
				flip = true;
			}
			else if ( a.Type == b3ShapeType.b3_hullShape && b.Type == b3ShapeType.b3_hullShape )
			{
				b3CollideHulls( &manifold, Capacity, a.Hull, b.Hull, bToA, &satCache );
			}
			else
			{
				return 0.0f;
			}
		}

		float deepest = 0.0f;

		for ( int i = 0; i < manifold.pointCount; ++i )
		{
			if ( points[i].separation < deepest )
				deepest = points[i].separation;
		}

		if ( deepest >= 0.0f ) return 0.0f;

		//Reversing the query put the normal in B's frame, so it has to come back to A's - and it points the wrong way besides.
		normal = flip
			? b3RotateVector( bToA.q, b3Neg( manifold.normal ) )
			: manifold.normal;

		return deepest;
	}

	//The normal comes back in world space, pointing out of A, and the depth is positive. False when they do not touch, and when neither has a convex shape - a mesh or a height field is not tested this way.
	/// <summary>How far body B is inside body A, with B optionally placed somewhere it is not yet.</summary>
	public static bool BodyPenetration( ulong bodyA, ulong bodyB, in Transform? transformB,
		out Vector3 normal, out float depth )
	{
		normal = Vector3.Zero;
		depth = 0.0f;

		if ( !ResolveBody( bodyA, out var idA ) || !ResolveBody( bodyB, out var idB ) ) return false;

		int countA = b3Body_GetShapeCount( idA );
		int countB = b3Body_GetShapeCount( idB );
		if ( countA <= 0 || countB <= 0 ) return false;

		Span<b3ShapeId> shapesA = countA <= StackShapes ? stackalloc b3ShapeId[countA] : new b3ShapeId[countA];
		Span<b3ShapeId> shapesB = countB <= StackShapes ? stackalloc b3ShapeId[countB] : new b3ShapeId[countB];

		int writtenA = GetShapes( idA, shapesA );
		int writtenB = GetShapes( idB, shapesB );

		b3Transform xfA = b3Body_GetTransform( idA );
		b3Transform xfB = transformB is { } placed ? ToBox3D( placed ) : b3Body_GetTransform( idB );

		b3Transform bToA = b3InvMulTransforms( xfA, xfB );

		float deepest = 0.0f;
		b3Vec3 deepestNormal = default;

		for ( int i = 0; i < writtenA; ++i )
		{
			if ( !ReadConvex( shapesA[i], out var a ) ) continue;

			for ( int j = 0; j < writtenB; ++j )
			{
				if ( !ReadConvex( shapesB[j], out var b ) ) continue;

				float separation = CollideConvex( a, b, bToA, out var pairNormal );

				if ( separation < deepest )
				{
					deepest = separation;
					deepestNormal = pairNormal;
				}
			}
		}

		if ( deepest >= 0.0f ) return false;

		normal = FromBox3D( b3RotateVector( xfA.q, deepestNormal ) );
		depth = -deepest;
		return true;
	}

	//Returns how many points there are - two per segment - which may be more than were written, so one call sizes the buffer and the next fills it. Meshes and height fields have no outline: they are drawn as triangles.
	/// <summary>The shape's edges as line segments, in the body's own frame.</summary>
	public static int ShapeOutline( ulong shape, Span<float> into )
	{
		if ( !ResolveShape( shape, out var id ) ) return 0;

		const int Segments = 16;
		const float Pi = 3.14159265358979323846f;

		int count = 0;
		int capacity = into.Length / 3;

		fixed ( float* output = into )
		{
			float* outPoints = into.IsEmpty ? null : output;

			void Line( b3Vec3 a, b3Vec3 b )
			{
				if ( outPoints != null && count + 1 < capacity )
				{
					float* at = outPoints + ( (nint)count * 3 );

					at[0] = a.x; at[1] = a.y; at[2] = a.z;
					at[3] = b.x; at[4] = b.y; at[5] = b.z;
				}

				count += 2;
			}

			switch ( b3Shape_GetType( id ) )
			{
				case b3ShapeType.b3_hullShape:
				{
					b3HullData* hull = b3Shape_GetHull( id );
					if ( hull == null ) break;

					b3Vec3* points = b3GetHullPoints( hull );
					b3HullHalfEdge* edges = b3GetHullEdges( hull );
					if ( points == null || edges == null ) break;

					//Half-edges come in pairs facing opposite ways along the same edge. Taking only the lower of each pair draws every edge exactly once.
					for ( int e = 0; e < hull->edgeCount; ++e )
					{
						if ( edges[e].twin < e ) continue;
						Line( points[edges[e].origin], points[edges[edges[e].twin].origin] );
					}

					break;
				}

				case b3ShapeType.b3_sphereShape:
				{
					b3Sphere sphere = b3Shape_GetSphere( id );

					//Three great circles: enough to read as a ball from any angle.
					for ( int axis = 0; axis < 3; ++axis )
					{
						for ( int s = 0; s < Segments; ++s )
						{
							b3Vec3 At( int segment )
							{
								float theta = 2.0f * Pi * ( segment % Segments ) / Segments;
								float c = sphere.radius * MathF.Cos( theta );
								float d = sphere.radius * MathF.Sin( theta );

								if ( axis == 0 ) return Vec3( sphere.center.x, sphere.center.y + c, sphere.center.z + d );
								if ( axis == 1 ) return Vec3( sphere.center.x + c, sphere.center.y, sphere.center.z + d );
								return Vec3( sphere.center.x + c, sphere.center.y + d, sphere.center.z );
							}

							Line( At( s ), At( s + 1 ) );
						}
					}

					break;
				}

				case b3ShapeType.b3_capsuleShape:
				{
					b3Capsule capsule = b3Shape_GetCapsule( id );

					b3Vec3 axis = b3Sub( capsule.center2, capsule.center1 );
					float length = b3Length( axis );
					axis = length > 1e-6f ? b3MulSV( 1.0f / length, axis ) : Vec3( 0.0f, 0.0f, 1.0f );

					b3Vec3 guide = b3AbsFloat( axis.z ) < 0.9f ? Vec3( 0.0f, 0.0f, 1.0f ) : Vec3( 1.0f, 0.0f, 0.0f );
					b3Vec3 u = b3Normalize( b3Cross( axis, guide ) );
					b3Vec3 v = b3Cross( axis, u );

					b3Vec3 Ring( b3Vec3 centre, int segment )
					{
						float theta = 2.0f * Pi * ( segment % Segments ) / Segments;
						float c = capsule.radius * MathF.Cos( theta );
						float s = capsule.radius * MathF.Sin( theta );

						return Vec3(
							centre.x + u.x * c + v.x * s,
							centre.y + u.y * c + v.y * s,
							centre.z + u.z * c + v.z * s );
					}

					for ( int s = 0; s < Segments; ++s )
					{
						Line( Ring( capsule.center1, s ), Ring( capsule.center1, s + 1 ) );
						Line( Ring( capsule.center2, s ), Ring( capsule.center2, s + 1 ) );

						//Four rails down the side, a quarter turn apart.
						if ( s % ( Segments / 4 ) == 0 )
							Line( Ring( capsule.center1, s ), Ring( capsule.center2, s ) );
					}

					break;
				}

				default:
					break;
			}
		}

		return count;
	}
}
