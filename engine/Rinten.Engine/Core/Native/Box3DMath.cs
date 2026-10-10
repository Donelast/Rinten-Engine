namespace Rinten.Native.Box3D;

//Box3D's header-only inline maths, ported op for op so physics rounding matches across machines
internal static class Box3DMath
{
	//float.Epsilon is the denormal minimum; FLT_MIN is the normal one.
	private const float FltMin = 1.17549435e-38f;

	public static b3Vec3 Vec3( float x, float y, float z ) => new() { x = x, y = y, z = z };
	public static b3Quat Quat( float x, float y, float z, float w ) => new() { v = Vec3( x, y, z ), s = w };

	public static readonly b3Quat b3Quat_identity = Quat( 0.0f, 0.0f, 0.0f, 1.0f );
	public static readonly b3Vec3 b3Vec3_zero = Vec3( 0.0f, 0.0f, 0.0f );
	public static readonly b3Matrix3 b3Mat3_zero = default;

	public static float b3AbsFloat( float a ) => a < 0 ? -a : a;
	public static float b3MinFloat( float a, float b ) => a < b ? a : b;
	public static float b3MaxFloat( float a, float b ) => a > b ? a : b;

	public static b3Vec3 b3Add( b3Vec3 a, b3Vec3 b ) => Vec3( a.x + b.x, a.y + b.y, a.z + b.z );
	public static b3Vec3 b3Sub( b3Vec3 a, b3Vec3 b ) => Vec3( a.x - b.x, a.y - b.y, a.z - b.z );
	public static b3Vec3 b3Neg( b3Vec3 a ) => Vec3( -a.x, -a.y, -a.z );
	public static float b3Dot( b3Vec3 a, b3Vec3 b ) => a.x * b.x + a.y * b.y + a.z * b.z;
	public static float b3Length( b3Vec3 v ) => MathF.Sqrt( b3Dot( v, v ) );
	public static float b3LengthSquared( b3Vec3 a ) => a.x * a.x + a.y * a.y + a.z * a.z;

	public static b3Vec3 b3Normalize( b3Vec3 a )
	{
		float lengthSquared = a.x * a.x + a.y * a.y + a.z * a.z;

		if ( lengthSquared > 1000.0f * FltMin )
		{
			float s = 1.0f / MathF.Sqrt( lengthSquared );
			return Vec3( s * a.x, s * a.y, s * a.z );
		}

		return Vec3( 0.0f, 0.0f, 0.0f );
	}

	public static b3Vec3 b3MulAdd( b3Vec3 a, float s, b3Vec3 b ) => Vec3( a.x + s * b.x, a.y + s * b.y, a.z + s * b.z );
	public static b3Vec3 b3MulSub( b3Vec3 a, float s, b3Vec3 b ) => Vec3( a.x - s * b.x, a.y - s * b.y, a.z - s * b.z );
	public static b3Vec3 b3MulSV( float s, b3Vec3 a ) => Vec3( s * a.x, s * a.y, s * a.z );

	public static b3Vec3 b3Cross( b3Vec3 a, b3Vec3 b )
	{
		b3Vec3 c;
		c.x = a.y * b.z - a.z * b.y;
		c.y = a.z * b.x - a.x * b.z;
		c.z = a.x * b.y - a.y * b.x;
		return c;
	}

	public static b3Vec3 b3Lerp( b3Vec3 a, b3Vec3 b, float alpha )
	{
		return Vec3(
			( 1.0f - alpha ) * a.x + alpha * b.x,
			( 1.0f - alpha ) * a.y + alpha * b.y,
			( 1.0f - alpha ) * a.z + alpha * b.z );
	}

	public static b3Vec3 b3Min( b3Vec3 a, b3Vec3 b ) => Vec3( b3MinFloat( a.x, b.x ), b3MinFloat( a.y, b.y ), b3MinFloat( a.z, b.z ) );
	public static b3Vec3 b3Max( b3Vec3 a, b3Vec3 b ) => Vec3( b3MaxFloat( a.x, b.x ), b3MaxFloat( a.y, b.y ), b3MaxFloat( a.z, b.z ) );

	public static b3Vec3 b3RotateVector( b3Quat q, b3Vec3 v )
	{
		//v + 2 * cross(q.v, cross(q.v, v) + q.s * v)
		b3Vec3 t1 = b3Cross( q.v, v );
		b3Vec3 t2 = b3MulAdd( t1, q.s, v );
		b3Vec3 t3 = b3Cross( q.v, t2 );
		return b3MulAdd( v, 2.0f, t3 );
	}

	public static b3Vec3 b3InvRotateVector( b3Quat q, b3Vec3 v )
	{
		//v + 2 * cross(q.v, cross(q.v, v) - q.s * v)
		b3Vec3 t1 = b3Cross( q.v, v );
		b3Vec3 t2 = b3MulSub( t1, q.s, v );
		b3Vec3 t3 = b3Cross( q.v, t2 );
		return b3MulAdd( v, 2.0f, t3 );
	}

	public static float b3DotQuat( b3Quat a, b3Quat b ) => a.v.x * b.v.x + a.v.y * b.v.y + a.v.z * b.v.z + a.s * b.s;

	public static b3Quat b3MulQuat( b3Quat q1, b3Quat q2 )
	{
		b3Vec3 t1 = b3Cross( q1.v, q2.v );
		b3Vec3 t2 = b3MulAdd( t1, q1.s, q2.v );
		b3Vec3 t3 = b3MulAdd( t2, q2.s, q1.v );
		return new b3Quat { v = t3, s = q1.s * q2.s - b3Dot( q1.v, q2.v ) };
	}

	public static b3Quat b3InvMulQuat( b3Quat q1, b3Quat q2 )
	{
		b3Vec3 t1 = b3Cross( q2.v, q1.v );
		b3Vec3 t2 = b3MulAdd( t1, q1.s, q2.v );
		b3Vec3 t3 = b3MulSub( t2, q2.s, q1.v );
		return new b3Quat { v = t3, s = q1.s * q2.s + b3Dot( q1.v, q2.v ) };
	}

	public static b3Quat b3Conjugate( b3Quat q ) => Quat( -q.v.x, -q.v.y, -q.v.z, q.s );

	public static b3Quat b3NormalizeQuat( b3Quat q )
	{
		float lengthSq = b3DotQuat( q, q );
		if ( lengthSq > 1000.0f * FltMin )
		{
			float s = 1.0f / MathF.Sqrt( lengthSq );
			return Quat( s * q.v.x, s * q.v.y, s * q.v.z, s * q.s );
		}

		return b3Quat_identity;
	}

	public static b3Transform b3InvMulTransforms( b3Transform a, b3Transform b )
	{
		b3Transform result;
		result.p = b3InvRotateVector( a.q, b3Sub( b.p, a.p ) );
		result.q = b3InvMulQuat( a.q, b.q );
		return result;
	}

	public static b3Transform b3InvertTransform( b3Transform t )
	{
		b3Transform result;
		result.p = b3InvRotateVector( t.q, b3Neg( t.p ) );
		result.q = b3Conjugate( t.q );
		return result;
	}

	public static b3Vec3 b3TransformPoint( b3Transform t, b3Vec3 v )
	{
		b3Vec3 rv = b3RotateVector( t.q, v );
		return b3Add( rv, t.p );
	}

	public static b3Vec3 b3InvTransformPoint( b3Transform t, b3Vec3 v ) => b3InvRotateVector( t.q, b3Sub( v, t.p ) );

	public static b3Vec3 b3MulMV( b3Matrix3 m, b3Vec3 a )
	{
		return Vec3(
			m.cx.x * a.x + m.cy.x * a.y + m.cz.x * a.z,
			m.cx.y * a.x + m.cy.y * a.y + m.cz.y * a.z,
			m.cx.z * a.x + m.cy.z * a.y + m.cz.z * a.z );
	}

	public static b3Matrix3 b3MulMM( b3Matrix3 a, b3Matrix3 b )
	{
		b3Matrix3 result;
		result.cx = b3MulMV( a, b.cx );
		result.cy = b3MulMV( a, b.cy );
		result.cz = b3MulMV( a, b.cz );
		return result;
	}

	public static b3Matrix3 b3Transpose( b3Matrix3 m )
	{
		b3Matrix3 result;
		result.cx = Vec3( m.cx.x, m.cy.x, m.cz.x );
		result.cy = Vec3( m.cx.y, m.cy.y, m.cz.y );
		result.cz = Vec3( m.cx.z, m.cy.z, m.cz.z );
		return result;
	}

	public static b3Matrix3 b3MakeMatrixFromQuat( b3Quat q )
	{
		float xx = q.v.x * q.v.x;
		float yy = q.v.y * q.v.y;
		float zz = q.v.z * q.v.z;
		float xy = q.v.x * q.v.y;
		float xz = q.v.x * q.v.z;
		float xw = q.v.x * q.s;
		float yz = q.v.y * q.v.z;
		float yw = q.v.y * q.s;
		float zw = q.v.z * q.s;

		return new b3Matrix3
		{
			cx = Vec3( 1.0f - 2.0f * ( yy + zz ), 2.0f * ( xy + zw ), 2.0f * ( xz - yw ) ),
			cy = Vec3( 2.0f * ( xy - zw ), 1.0f - 2.0f * ( xx + zz ), 2.0f * ( yz + xw ) ),
			cz = Vec3( 2.0f * ( xz + yw ), 2.0f * ( yz - xw ), 1.0f - 2.0f * ( xx + yy ) ),
		};
	}

	//-- ids ------------------------------------------------------------------

	public static bool B3_IS_NULL( b3ShapeId id ) => id.index1 == 0;
	public static bool B3_ID_EQUALS( b3BodyId a, b3BodyId b ) => a.index1 == b.index1 && a.world0 == b.world0 && a.generation == b.generation;

	//-- hull data ------------------------------------------------------------ A hull is one allocation
	//with its arrays at offsets from its own address, which is what lets Box3D copy one with memcpy.

	public static unsafe b3Vec3* b3GetHullPoints( b3HullData* hull )
		=> hull->pointOffset == 0 ? null : (b3Vec3*)( (byte*)hull + hull->pointOffset );

	public static unsafe b3HullHalfEdge* b3GetHullEdges( b3HullData* hull )
		=> hull->edgeOffset == 0 ? null : (b3HullHalfEdge*)( (byte*)hull + hull->edgeOffset );

	public static unsafe b3HullFace* b3GetHullFaces( b3HullData* hull )
		=> hull->faceOffset == 0 ? null : (b3HullFace*)( (byte*)hull + hull->faceOffset );

	//-- constants ------------------------------------------------------------ constants.h defines these in terms of b3GetLengthUnitsPerMeter(),
	//which is left at its default of one - a unit is a metre on both sides of the backend - so they are read once and kept.

	/// <summary>B3_LINEAR_SLOP: the distance two shapes may overlap by design.</summary>
	public static readonly float B3_LINEAR_SLOP = 0.005f * Box3D.b3GetLengthUnitsPerMeter();

	/// <summary>B3_SPECULATIVE_DISTANCE: how far ahead a contact is made.</summary>
	public static readonly float B3_SPECULATIVE_DISTANCE = 4.0f * B3_LINEAR_SLOP;

	/// <summary>B3_HUGE: bigger than any coordinate a world will hold.</summary>
	public static readonly float B3_HUGE = 1.0e5f * Box3D.b3GetLengthUnitsPerMeter();

	/// <summary>B3_MAX_SHAPE_CAST_POINTS: the most points a cast proxy may carry.</summary>
	public const int B3_MAX_SHAPE_CAST_POINTS = 64;

	/// <summary>B3_HEIGHT_FIELD_HOLE: a material index that means "no triangle here".</summary>
	public const byte B3_HEIGHT_FIELD_HOLE = 0xFF;

	/// <summary>B3_MAX_MANIFOLD_POINTS: the most points one contact manifold holds.</summary>
	public const int B3_MAX_MANIFOLD_POINTS = 4;
}
