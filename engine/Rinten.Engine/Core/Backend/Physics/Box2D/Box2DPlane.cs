using Rinten.Native.Box2D;

namespace Rinten;

//Maps 3D engine values to Box2D: world XY side-on, Z kept aside, angles about +Z, metres unscaled
internal static class Box2DPlane
{
	/// <summary>How thick a 2D body's bounds are made, so a flat box still has a volume to select and cull.</summary>
	public const float BoundsHalfDepth = 0.5f;

	public static b2Vec2 Vec( Vector3 v ) => new() { x = v.x, y = v.y };
	public static b2Vec2 Vec( Vector2 v ) => new() { x = v.x, y = v.y };
	public static b2Vec2 Vec( float x, float y ) => new() { x = x, y = y };

	public static Vector2 Flat( b2Vec2 v ) => new( v.x, v.y );
	public static Vector2 Flat( Vector3 v ) => new( v.x, v.y );
	public static Vector3 Point( b2Vec2 v, float z = 0.0f ) => new( v.x, v.y, z );

	public static b2Rot Rot( float radians ) => new() { c = MathF.Cos( radians ), s = MathF.Sin( radians ) };
	public static float Angle( b2Rot r ) => MathF.Atan2( r.s, r.c );

	public static b2Transform Xf( Vector2 position, float radians ) => new() { p = Vec( position ), q = Rot( radians ) };

	/// <summary>A turn of <paramref name="radians"/> about world +Z.</summary>
	public static Rotation AboutZ( float radians )
	{
		var half = radians * 0.5f;
		return new Rotation( 0.0f, 0.0f, MathF.Sin( half ), MathF.Cos( half ) );
	}

	/// <summary>The heading of a rotation in the plane: how far it has turned about +Z.</summary>
	public static float AngleOf( in Rotation r )
	{
		var x = r * new Vector3( 1, 0, 0 );
		var y = r * new Vector3( 0, 1, 0 );

		if ( x.x * x.x + x.y * x.y >= y.x * y.x + y.y * y.y )
			return MathF.Atan2( x.y, x.x );

		return MathF.Atan2( y.y, y.x ) - MathF.PI * 0.5f;
	}

	public static Vector2 Rotate( Vector2 v, float radians )
	{
		var c = MathF.Cos( radians );
		var s = MathF.Sin( radians );
		return new Vector2( c * v.x - s * v.y, s * v.x + c * v.y );
	}

	public static b2Vec2 Apply( in b2Transform xf, b2Vec2 v )
		=> Vec( xf.q.c * v.x - xf.q.s * v.y + xf.p.x, xf.q.s * v.x + xf.q.c * v.y + xf.p.y );

	public static BBox Box( b2AABB aabb, float z )
		=> new( new Vector3( aabb.lowerBound.x, aabb.lowerBound.y, z - BoundsHalfDepth ),
			new Vector3( aabb.upperBound.x, aabb.upperBound.y, z + BoundsHalfDepth ) );

	/// <summary>Every category and every mask: a query that filters on the managed side.</summary>
	public static b2QueryFilter Everything => new() { categoryBits = ulong.MaxValue, maskBits = ulong.MaxValue };

	/// <summary>An id as one word, for dictionaries. Box2D's ids are small plain structs.</summary>
	public static unsafe ulong Key( b2ShapeId id ) { ulong k = 0; *(b2ShapeId*)&k = id; return k; }
	public static unsafe ulong Key( b2BodyId id ) { ulong k = 0; *(b2BodyId*)&k = id; return k; }
	public static unsafe ulong Key( b2JointId id ) { ulong k = 0; *(b2JointId*)&k = id; return k; }
}
