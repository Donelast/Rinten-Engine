using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rinten.Native.Box3D;
using static Rinten.Native.Box3D.Box3D;
using static Rinten.Native.Box3D.Box3DMath;

namespace Rinten;

//Triangulates shapes for navmesh input, debug drawing and buoyancy; curved parts are coarse
internal static unsafe partial class CorePhysics
{
	//Writes triangles out, counting past the end rather than stopping there, so one pass can be used to size a buffer and the next to fill it.
	private struct TriangleWriter
	{
		public float* Out;
		public int Capacity;
		public int Count;

		public void Add( b3Vec3 a, b3Vec3 b, b3Vec3 c )
		{
			if ( Out != null && Count < Capacity )
			{
				float* at = Out + ( (nint)Count * 9 );

				at[0] = a.x; at[1] = a.y; at[2] = a.z;
				at[3] = b.x; at[4] = b.y; at[5] = b.z;
				at[6] = c.x; at[7] = c.y; at[8] = c.z;
			}

			++Count;
		}
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static B3Bool OnQueryTriangle( b3Vec3 a, b3Vec3 b, b3Vec3 c, int index, void* context )
		=> WriteTriangle( a, b, c, index, context );

	//What both entries do once the arguments are in hand.
	private static B3Bool WriteTriangle( b3Vec3 a, b3Vec3 b, b3Vec3 c, int index, void* context )
	{
		( (TriangleWriter*)context )->Add( a, b, c );
		return true;
	}

	//A convex hull's faces are convex polygons of any size, walked as a ring of half-edges. Fanning each
	//ring from its first corner triangulates it, which is always valid because the polygon is convex.
	private static void TriangulateHull( b3HullData* hull, ref TriangleWriter writer )
	{
		if ( hull == null ) return;

		b3Vec3* points = b3GetHullPoints( hull );
		b3HullHalfEdge* edges = b3GetHullEdges( hull );
		b3HullFace* faces = b3GetHullFaces( hull );

		if ( points == null || edges == null || faces == null ) return;

		for ( int f = 0; f < hull->faceCount; ++f )
		{
			byte first = faces[f].edge;

			byte previous = edges[first].next;
			byte current = edges[previous].next;

			//A ring can only be as long as the half-edge array; a malformed hull would otherwise spin here forever.
			for ( int guard = 0; guard < hull->edgeCount && current != first; ++guard )
			{
				writer.Add( points[edges[first].origin], points[edges[previous].origin], points[edges[current].origin] );

				previous = current;
				current = edges[current].next;
			}
		}
	}

	//A sphere as a longitude/latitude grid. Coarse on purpose: the callers are navmesh input and debug drawing, and neither is improved by a finer ball.
	private static void TriangulateSphere( b3Vec3 center, float radius, ref TriangleWriter writer )
	{
		const int Segments = 12;
		const int Rings = 6;
		const float Pi = 3.14159265358979323846f;

		static b3Vec3 At( b3Vec3 center, float radius, int ring, int segment )
		{
			float phi = Pi * ring / Rings;
			float theta = 2.0f * Pi * ( segment % Segments ) / Segments;

			return Vec3(
				center.x + radius * MathF.Sin( phi ) * MathF.Cos( theta ),
				center.y + radius * MathF.Sin( phi ) * MathF.Sin( theta ),
				center.z + radius * MathF.Cos( phi ) );
		}

		for ( int ring = 0; ring < Rings; ++ring )
		{
			for ( int segment = 0; segment < Segments; ++segment )
			{
				b3Vec3 topLeft = At( center, radius, ring, segment );
				b3Vec3 topRight = At( center, radius, ring, segment + 1 );
				b3Vec3 bottomLeft = At( center, radius, ring + 1, segment );
				b3Vec3 bottomRight = At( center, radius, ring + 1, segment + 1 );

				//The poles are a single point, so one of the two triangles there is degenerate and worth skipping rather than emitting.
				if ( ring != 0 ) writer.Add( topLeft, bottomLeft, topRight );
				if ( ring != Rings - 1 ) writer.Add( topRight, bottomLeft, bottomRight );
			}
		}
	}

	//A capsule as a tube between two hemispheres.
	private static void TriangulateCapsule( b3Vec3 a, b3Vec3 b, float radius, ref TriangleWriter writer )
	{
		const int Segments = 12;
		const float Pi = 3.14159265358979323846f;

		b3Vec3 axis = b3Sub( b, a );
		float length = b3Length( axis );

		if ( length < 1e-6f )
		{
			TriangulateSphere( a, radius, ref writer );
			return;
		}

		axis = b3MulSV( 1.0f / length, axis );

		//Any direction not along the axis gives a usable pair of ring vectors.
		b3Vec3 guide = b3AbsFloat( axis.z ) < 0.9f ? Vec3( 0.0f, 0.0f, 1.0f ) : Vec3( 1.0f, 0.0f, 0.0f );
		b3Vec3 u = b3Normalize( b3Cross( axis, guide ) );
		b3Vec3 v = b3Cross( axis, u );

		static b3Vec3 Ring( b3Vec3 centre, b3Vec3 u, b3Vec3 v, float radius, int segment )
		{
			float theta = 2.0f * Pi * ( segment % Segments ) / Segments;
			float c = radius * MathF.Cos( theta );
			float s = radius * MathF.Sin( theta );

			return Vec3(
				centre.x + u.x * c + v.x * s,
				centre.y + u.y * c + v.y * s,
				centre.z + u.z * c + v.z * s );
		}

		for ( int segment = 0; segment < Segments; ++segment )
		{
			b3Vec3 a0 = Ring( a, u, v, radius, segment );
			b3Vec3 a1 = Ring( a, u, v, radius, segment + 1 );
			b3Vec3 b0 = Ring( b, u, v, radius, segment );
			b3Vec3 b1 = Ring( b, u, v, radius, segment + 1 );

			//Wound so the faces point outwards, which is what lets the volume of the enclosed solid be read off the triangles.
			writer.Add( a0, a1, b0 );
			writer.Add( a1, b1, b0 );

			//The end caps as fans. Blunter than hemispheres, but a capsule's ends only have to be there for something to stand on.
			writer.Add( a1, a0, b3MulAdd( a, -radius, axis ) );
			writer.Add( b0, b1, b3MulAdd( b, radius, axis ) );
		}
	}

	//Returns how many triangles there are, which may be more than were written: call it once with an empty span to size a buffer and again to fill it. Nine floats a triangle.
	/// <summary>This shape as triangles, in the body's own frame.</summary>
	public static int ShapeTriangulate( ulong shape, Span<float> into )
		=> ShapeTriangulate( shape, null, null, into );

	/// <summary>The same, keeping only what reaches into a box given in world space.</summary>
	public static int ShapeTriangulate( ulong shape, in BBox bounds, Span<float> into )
	{
		Vector3 min = bounds.Mins;
		Vector3 max = bounds.Maxs;

		return ShapeTriangulate( shape, &min, &max, into );
	}

	private static int ShapeTriangulate( ulong shape, Vector3* boundsMin, Vector3* boundsMax, Span<float> into )
	{
		if ( !ResolveShape( shape, out var id ) ) return 0;

		fixed ( float* output = into )
		{
			TriangleWriter writer = new() { Out = into.IsEmpty ? null : output, Capacity = into.Length / 9, Count = 0 };

			//The query bounds arrive in world space but the geometry is in the body's, so the box has to come back the other way. Rotating an axis-aligned box
			//does not leave one, so this is the box around the rotated box - larger than asked for, never smaller, which is the safe direction for a filter.
			b3AABB local = new()
			{
				lowerBound = Vec3( -B3_HUGE, -B3_HUGE, -B3_HUGE ),
				upperBound = Vec3( B3_HUGE, B3_HUGE, B3_HUGE ),
			};

			if ( boundsMin != null && boundsMax != null )
			{
				b3Transform bodyTransform = b3Body_GetTransform( b3Shape_GetBody( id ) );

				b3Vec3 mins = Vec3( B3_HUGE, B3_HUGE, B3_HUGE );
				b3Vec3 maxs = Vec3( -B3_HUGE, -B3_HUGE, -B3_HUGE );

				for ( int corner = 0; corner < 8; ++corner )
				{
					b3Vec3 world = Vec3(
						( corner & 1 ) != 0 ? boundsMax->x : boundsMin->x,
						( corner & 2 ) != 0 ? boundsMax->y : boundsMin->y,
						( corner & 4 ) != 0 ? boundsMax->z : boundsMin->z );

					b3Vec3 point = b3InvTransformPoint( bodyTransform, world );

					mins = b3Min( mins, point );
					maxs = b3Max( maxs, point );
				}

				local = new b3AABB { lowerBound = mins, upperBound = maxs };
			}

			switch ( b3Shape_GetType( id ) )
			{
				case b3ShapeType.b3_meshShape:
				{
					b3Mesh mesh = b3Shape_GetMesh( id );
					b3QueryMesh( &mesh, local, QueryTriangleEntry(), &writer );
					break;
				}

				case b3ShapeType.b3_heightShape:
				{
					b3HeightFieldData* field = b3Shape_GetHeightField( id );
					if ( field != null )
						b3QueryHeightField( field, local, QueryTriangleEntry(), &writer );
					break;
				}

				//The rest are small enough that walking all of them and letting the caller's own bounds test sort it out beats writing a clipper.
				case b3ShapeType.b3_hullShape:
					TriangulateHull( b3Shape_GetHull( id ), ref writer );
					break;

				case b3ShapeType.b3_sphereShape:
				{
					b3Sphere sphere = b3Shape_GetSphere( id );
					TriangulateSphere( sphere.center, sphere.radius, ref writer );
					break;
				}

				case b3ShapeType.b3_capsuleShape:
				{
					b3Capsule capsule = b3Shape_GetCapsule( id );
					TriangulateCapsule( capsule.center1, capsule.center2, capsule.radius, ref writer );
					break;
				}

				default:
					break;
			}

			return writer.Count;
		}
	}

	//-- buoyancy -----------------------------------------------------------------

	//Running total of a submerged region: how much of it there is and where its middle is.
	private struct SubmergedVolume
	{
		public float Volume;
		public b3Vec3 Moment;

		/// <summary>The signed volume of the tetrahedron from the origin to this triangle. Summed over a closed surface this is the volume it encloses, and the volume-weighted corner sum is four times its centroid moment.</summary>
		public void AddTetrahedron( b3Vec3 a, b3Vec3 b, b3Vec3 c )
		{
			float signedVolume = b3Dot( a, b3Cross( b, c ) ) / 6.0f;

			Volume += signedVolume;
			Moment = b3MulAdd( Moment, signedVolume * 0.25f, Vec3( a.x + b.x + c.x, a.y + b.y + c.y, a.z + b.z + c.z ) );
		}
	}

	//Adds the part of one triangle below the plane. Points are relative to a point on the plane, which is what lets the flat
	//lid over the cut be left out: every triangle of it would sit in a plane through the origin and enclose no volume at all.
	private static void AddClippedTriangle( ref SubmergedVolume into, b3Vec3 normal, b3Vec3 a, b3Vec3 b, b3Vec3 c )
	{
		float da = b3Dot( normal, a );
		float db = b3Dot( normal, b );
		float dc = b3Dot( normal, c );

		int below = ( da < 0.0f ? 1 : 0 ) + ( db < 0.0f ? 1 : 0 ) + ( dc < 0.0f ? 1 : 0 );
		if ( below == 0 ) return;

		if ( below == 3 )
		{
			into.AddTetrahedron( a, b, c );
			return;
		}

		static b3Vec3 Cut( b3Vec3 from, float dFrom, b3Vec3 to, float dTo )
		{
			float t = dFrom / ( dFrom - dTo );
			return b3Lerp( from, to, t );
		}

		//Rotate the corners so the odd one out is always first, which turns four cases into two.
		float dA = da, dB = db, dC = dc;

		if ( below == 1 )
		{
			if ( db < 0.0f )
			{
				( a, b ) = ( b, a ); ( dA, dB ) = ( dB, dA );
				( b, c ) = ( c, b ); ( dB, dC ) = ( dC, dB );
			}
			else if ( dc < 0.0f )
			{
				( a, c ) = ( c, a ); ( dA, dC ) = ( dC, dA );
				( b, c ) = ( c, b ); ( dB, dC ) = ( dC, dB );
			}

			//One corner under: a single triangle down to where the edges cross.
			into.AddTetrahedron( a, Cut( a, dA, b, dB ), Cut( a, dA, c, dC ) );
			return;
		}

		if ( da >= 0.0f )
		{
			( a, b ) = ( b, a ); ( dA, dB ) = ( dB, dA );
			( b, c ) = ( c, b ); ( dB, dC ) = ( dC, dB );
		}
		else if ( db >= 0.0f )
		{
			( b, c ) = ( c, b ); ( dB, dC ) = ( dC, dB );
			( a, b ) = ( b, a ); ( dA, dB ) = ( dB, dA );
		}

		//Two corners under: a quadrilateral, as two triangles. After the rotation above, c is the one above the surface.
		b3Vec3 onAC = Cut( a, dA, c, dC );
		b3Vec3 onBC = Cut( b, dB, c, dC );

		into.AddTetrahedron( a, b, onBC );
		into.AddTetrahedron( a, onBC, onAC );
	}

	/// <summary>One step of buoyancy and drag on whatever part of a body is under a plane.</summary>
	public static void BodyBuoyancy( ulong body, Vector3 planeOrigin, Vector3 planeNormal,
		float fluidDensity, float linearDrag, float angularDrag,
		Vector3 fluidVelocity, Vector3 gravity, float deltaTime )
	{
		if ( deltaTime <= 0.0f || fluidDensity <= 0.0f ) return;

		if ( !ResolveBody( body, out var id ) ) return;
		if ( b3Body_GetType( id ) != b3BodyType.b3_dynamicBody ) return;

		int shapeCount = b3Body_GetShapeCount( id );
		if ( shapeCount <= 0 ) return;

		Span<b3ShapeId> shapes = shapeCount <= StackShapes ? stackalloc b3ShapeId[shapeCount] : new b3ShapeId[shapeCount];
		int written = GetShapes( id, shapes );

		b3Transform transform = b3Body_GetTransform( id );

		//The surface is given in world space and the geometry is in the body's, so the plane goes the other
		//way. Its origin becomes the origin the volume is integrated around, which is what the clip relies on.
		b3Vec3 localOrigin = b3InvTransformPoint( transform, ToBox3D( planeOrigin ) );
		b3Vec3 localNormal = b3InvRotateVector( transform.q, b3Normalize( ToBox3D( planeNormal ) ) );

		SubmergedVolume submerged = default;
		float totalVolume = 0.0f;

		float[] corners = null;

		for ( int i = 0; i < written; ++i )
		{
			ulong shape = Pack( shapes[i] );

			//Whole triangles, so the lid-free integration holds: a shape clipped by the query bounds would be an open surface and enclose nothing.
			int triangles = ShapeTriangulate( shape, Span<float>.Empty );
			if ( triangles <= 0 ) continue;

			int floats = triangles * 9;
			if ( corners is null || corners.Length < floats ) corners = new float[floats];

			ShapeTriangulate( shape, corners.AsSpan( 0, floats ) );

			SubmergedVolume whole = default;

			for ( int t = 0; t < triangles; ++t )
			{
				int at = t * 9;

				b3Vec3 a = b3Sub( Vec3( corners[at], corners[at + 1], corners[at + 2] ), localOrigin );
				b3Vec3 b = b3Sub( Vec3( corners[at + 3], corners[at + 4], corners[at + 5] ), localOrigin );
				b3Vec3 c = b3Sub( Vec3( corners[at + 6], corners[at + 7], corners[at + 8] ), localOrigin );

				whole.AddTetrahedron( a, b, c );
				AddClippedTriangle( ref submerged, localNormal, a, b, c );
			}

			totalVolume += b3AbsFloat( whole.Volume );
		}

		if ( submerged.Volume <= 0.0f || totalVolume <= 0.0f ) return;

		//The centroid comes out relative to the plane's origin, in the body's frame.
		b3Vec3 centre = b3TransformPoint( transform,
			b3MulAdd( localOrigin, 1.0f / submerged.Volume, submerged.Moment ) );

		b3Vec3 gravityMetres = ToBox3D( gravity );

		//Archimedes: the push up is the weight of the fluid pushed aside.
		float displaced = fluidDensity * submerged.Volume;
		b3Body_ApplyLinearImpulse( id, b3MulSV( -displaced * deltaTime, gravityMetres ), centre, true );

		//Drag only counts for the part that is actually in the fluid.
		float fraction = b3MinFloat( submerged.Volume / totalVolume, 1.0f );

		if ( linearDrag > 0.0f )
		{
			b3Vec3 relative = b3Sub( b3Body_GetLinearVelocity( id ), ToBox3D( fluidVelocity ) );

			//Scaling by the body's own mass makes the coefficient a plain fraction of speed lost per second, whatever the body weighs.
			float scale = -b3MinFloat( linearDrag * fraction * deltaTime, 1.0f ) * b3Body_GetMass( id );
			b3Body_ApplyLinearImpulse( id, b3MulSV( scale, relative ), b3Body_GetWorldCenterOfMass( id ), true );
		}

		if ( angularDrag > 0.0f )
		{
			b3Vec3 spin = b3Body_GetAngularVelocity( id );
			b3Matrix3 inertia = b3Body_GetLocalRotationalInertia( id );

			//Only the diagonal, and in the body's frame: enough for damping, which only has to be the right size and point the other way.
			b3Vec3 localSpin = b3InvRotateVector( transform.q, spin );
			float scale = -b3MinFloat( angularDrag * fraction * deltaTime, 1.0f );

			b3Vec3 localImpulse = Vec3(
				scale * localSpin.x * inertia.cx.x,
				scale * localSpin.y * inertia.cy.y,
				scale * localSpin.z * inertia.cz.z );

			b3Body_ApplyAngularImpulse( id, b3RotateVector( transform.q, localImpulse ), true );
		}
	}
}
