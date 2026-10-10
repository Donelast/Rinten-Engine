using Rinten.Native.Box2D;
using static Rinten.Native.Box2D.Box2D;

namespace Rinten;

//How deep two pieces of geometry overlap, from Box2D's own manifold functions - the same
//ones its solver uses, so a penetration asked for here is the one the step would push apart.
internal static unsafe class Box2DCollide
{
	/// <summary>The deepest separation between <paramref name="a"/> at <paramref name="xfA"/> and <paramref name="b"/> at <paramref name="xfB"/> (negative when they overlap), and the world normal pointing from a to b. False when they are too far apart for a manifold, or are a pair Box2D does not collide.</summary>
	public static bool Separation( in Box2DPart a, in b2Transform xfA, in Box2DPart b, in b2Transform xfB,
		out Vector2 normal, out float separation )
	{
		normal = default;
		separation = 0.0f;

		if ( Collide( a, b, Relative( xfA, xfB ), out var manifold ) )
		{
			return Deepest( manifold, xfA, false, out normal, out separation );
		}

		if ( Collide( b, a, Relative( xfB, xfA ), out manifold ) )
		{
			return Deepest( manifold, xfB, true, out normal, out separation );
		}

		return false;
	}

	//B's transform in A's frame.
	private static b2Transform Relative( in b2Transform a, in b2Transform b )
	{
		var dx = b.p.x - a.p.x;
		var dy = b.p.y - a.p.y;

		return new b2Transform
		{
			p = Box2DPlane.Vec( a.q.c * dx + a.q.s * dy, -a.q.s * dx + a.q.c * dy ),
			q = new b2Rot { c = a.q.c * b.q.c + a.q.s * b.q.s, s = a.q.c * b.q.s - a.q.s * b.q.c },
		};
	}

	private static bool Deepest( in b2LocalManifold manifold, in b2Transform frame, bool flip, out Vector2 normal, out float separation )
	{
		normal = default;
		separation = 0.0f;

		if ( manifold.pointCount <= 0 ) return false;

		separation = manifold.points[0].separation;
		for ( var i = 1; i < manifold.pointCount; i++ )
			separation = MathF.Min( separation, manifold.points[i].separation );

		var n = manifold.normal;
		normal = new Vector2( frame.q.c * n.x - frame.q.s * n.y, frame.q.s * n.x + frame.q.c * n.y );
		if ( flip ) normal = -normal;

		return true;
	}

	private static bool Collide( in Box2DPart a, in Box2DPart b, b2Transform xf, out b2LocalManifold manifold )
	{
		manifold = default;

		var ca = a.Circle; var cb = b.Circle;
		var ka = a.Capsule; var kb = b.Capsule;
		var pa = a.Polygon; var pb = b.Polygon;
		var sa = a.Segment;

		switch ( (a.Type, b.Type) )
		{
			case (b2ShapeType.b2_circleShape, b2ShapeType.b2_circleShape): manifold = b2CollideCircles( &ca, &cb, xf ); return true;
			case (b2ShapeType.b2_capsuleShape, b2ShapeType.b2_circleShape): manifold = b2CollideCapsuleAndCircle( &ka, &cb, xf ); return true;
			case (b2ShapeType.b2_segmentShape, b2ShapeType.b2_circleShape): manifold = b2CollideSegmentAndCircle( &sa, &cb, xf ); return true;
			case (b2ShapeType.b2_polygonShape, b2ShapeType.b2_circleShape): manifold = b2CollidePolygonAndCircle( &pa, &cb, xf ); return true;
			case (b2ShapeType.b2_capsuleShape, b2ShapeType.b2_capsuleShape): manifold = b2CollideCapsules( &ka, &kb, xf ); return true;
			case (b2ShapeType.b2_segmentShape, b2ShapeType.b2_capsuleShape): manifold = b2CollideSegmentAndCapsule( &sa, &kb, xf ); return true;
			case (b2ShapeType.b2_polygonShape, b2ShapeType.b2_capsuleShape): manifold = b2CollidePolygonAndCapsule( &pa, &kb, xf ); return true;
			case (b2ShapeType.b2_polygonShape, b2ShapeType.b2_polygonShape): manifold = b2CollidePolygons( &pa, &pb, xf ); return true;
			case (b2ShapeType.b2_segmentShape, b2ShapeType.b2_polygonShape): manifold = b2CollideSegmentAndPolygon( &sa, &pb, xf ); return true;
			default: return false;
		}
	}
}
