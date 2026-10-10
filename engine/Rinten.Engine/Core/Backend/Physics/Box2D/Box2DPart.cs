using Rinten.Native.Box2D;
using static Rinten.Native.Box2D.Box2D;

namespace Rinten;

//One piece of Box2D geometry in a body's frame, kept on the managed side so a shape can be rebuilt - Box2D
//fixes whether a shape is a sensor when it is made, and a trigger switched on later means making it again.
internal unsafe struct Box2DPart
{
	public b2ShapeType Type;
	public b2Circle Circle;
	public b2Capsule Capsule;
	public b2Polygon Polygon;
	public b2Segment Segment;

	public static Box2DPart Of( b2Circle circle ) => new() { Type = b2ShapeType.b2_circleShape, Circle = circle };
	public static Box2DPart Of( b2Capsule capsule ) => new() { Type = b2ShapeType.b2_capsuleShape, Capsule = capsule };
	public static Box2DPart Of( b2Polygon polygon ) => new() { Type = b2ShapeType.b2_polygonShape, Polygon = polygon };
	public static Box2DPart Of( b2Segment segment ) => new() { Type = b2ShapeType.b2_segmentShape, Segment = segment };

	/// <summary>A convex polygon through Box2D's own hull, which welds near points and checks what it is given - or, when the points are too thin to be a polygon, the segment across them.</summary>
	public static bool TryPolygon( ReadOnlySpan<Vector2> points, out Box2DPart part )
	{
		part = default;
		if ( points.Length < 2 ) return false;

		if ( points.Length >= 3 && points.Length <= Box2DConvexDecomposition.MaxPolygonVertices )
		{
			var buffer = stackalloc b2Vec2[points.Length];
			for ( var i = 0; i < points.Length; i++ ) buffer[i] = Box2DPlane.Vec( points[i] );

			var hull = b2ComputeHull( buffer, points.Length );
			if ( hull.count >= 3 )
			{
				part = Of( b2MakePolygon( &hull, 0.0f ) );
				return true;
			}
		}

		return TrySegment( points, out part );
	}

	/// <summary>The longest span across some points, as a segment. Nothing when they are all one point.</summary>
	public static bool TrySegment( ReadOnlySpan<Vector2> points, out Box2DPart part )
	{
		part = default;

		var best = 0.0f;
		var a = Vector2.Zero;
		var b = Vector2.Zero;

		for ( var i = 0; i < points.Length; i++ )
		{
			for ( var j = i + 1; j < points.Length; j++ )
			{
				var d = (points[i] - points[j]).LengthSquared;
				if ( d <= best ) continue;

				best = d;
				a = points[i];
				b = points[j];
			}
		}

		//Box2D asserts on a segment shorter than its linear slop.
		if ( best < 0.01f * 0.01f ) return false;

		part = Of( new b2Segment { point1 = Box2DPlane.Vec( a ), point2 = Box2DPlane.Vec( b ) } );
		return true;
	}

	public readonly b2ShapeId Create( b2BodyId body, in b2ShapeDef def )
	{
		var d = def;

		switch ( Type )
		{
			case b2ShapeType.b2_circleShape: { var g = Circle; return b2CreateCircleShape( body, &d, &g ); }
			case b2ShapeType.b2_capsuleShape: { var g = Capsule; return b2CreateCapsuleShape( body, &d, &g ); }
			case b2ShapeType.b2_polygonShape: { var g = Polygon; return b2CreatePolygonShape( body, &d, &g ); }
			case b2ShapeType.b2_segmentShape: { var g = Segment; return b2CreateSegmentShape( body, &d, &g ); }
			default: return default;
		}
	}

	/// <summary>The outline as a point cloud, placed by <paramref name="xf"/>, for casting.</summary>
	public readonly bool Proxy( in b2Transform xf, out b2ShapeProxy proxy )
	{
		proxy = default;

		switch ( Type )
		{
			case b2ShapeType.b2_circleShape:
				proxy.points[0] = Box2DPlane.Apply( xf, Circle.center );
				proxy.count = 1;
				proxy.radius = Circle.radius;
				return true;

			case b2ShapeType.b2_capsuleShape:
				proxy.points[0] = Box2DPlane.Apply( xf, Capsule.center1 );
				proxy.points[1] = Box2DPlane.Apply( xf, Capsule.center2 );
				proxy.count = 2;
				proxy.radius = Capsule.radius;
				return true;

			case b2ShapeType.b2_polygonShape:
				for ( var i = 0; i < Polygon.count; i++ )
					proxy.points[i] = Box2DPlane.Apply( xf, Polygon.vertices[i] );
				proxy.count = Polygon.count;
				proxy.radius = Polygon.radius;
				return true;

			case b2ShapeType.b2_segmentShape:
				proxy.points[0] = Box2DPlane.Apply( xf, Segment.point1 );
				proxy.points[1] = Box2DPlane.Apply( xf, Segment.point2 );
				proxy.count = 2;
				return true;
		}

		return false;
	}

	/// <summary>A closed outline in the body's frame, for drawing, buoyancy and bounds. Curves are cut into straight pieces.</summary>
	public readonly int Outline( Span<Vector2> into )
	{
		const int circleSegments = 16;
		const int capSegments = 8;

		switch ( Type )
		{
			case b2ShapeType.b2_circleShape:
			{
				var c = Box2DPlane.Flat( Circle.center );
				for ( var i = 0; i < circleSegments; i++ )
				{
					var angle = MathF.Tau * i / circleSegments;
					into[i] = c + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * Circle.radius;
				}
				return circleSegments;
			}

			case b2ShapeType.b2_capsuleShape:
			{
				var c1 = Box2DPlane.Flat( Capsule.center1 );
				var c2 = Box2DPlane.Flat( Capsule.center2 );
				var axis = c2 - c1;
				var baseAngle = axis.LengthSquared > 1e-12f ? MathF.Atan2( axis.y, axis.x ) : 0.0f;
				var count = 0;

				for ( var i = 0; i <= capSegments; i++ )
				{
					var angle = baseAngle - MathF.PI * 0.5f + MathF.PI * i / capSegments;
					into[count++] = c2 + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * Capsule.radius;
				}

				for ( var i = 0; i <= capSegments; i++ )
				{
					var angle = baseAngle + MathF.PI * 0.5f + MathF.PI * i / capSegments;
					into[count++] = c1 + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * Capsule.radius;
				}

				return count;
			}

			case b2ShapeType.b2_polygonShape:
				for ( var i = 0; i < Polygon.count; i++ )
					into[i] = Box2DPlane.Flat( Polygon.vertices[i] );
				return Polygon.count;

			case b2ShapeType.b2_segmentShape:
				into[0] = Box2DPlane.Flat( Segment.point1 );
				into[1] = Box2DPlane.Flat( Segment.point2 );
				return 2;
		}

		return 0;
	}

	/// <summary>Most points <see cref="Outline"/> writes.</summary>
	public const int MaxOutline = 18;

	/// <summary>Read a live shape's geometry back, for a rebuild.</summary>
	public static Box2DPart Read( b2ShapeId id )
	{
		return b2Shape_GetType( id ) switch
		{
			b2ShapeType.b2_circleShape => Of( b2Shape_GetCircle( id ) ),
			b2ShapeType.b2_capsuleShape => Of( b2Shape_GetCapsule( id ) ),
			b2ShapeType.b2_polygonShape => Of( b2Shape_GetPolygon( id ) ),
			b2ShapeType.b2_segmentShape => Of( b2Shape_GetSegment( id ) ),
			_ => default,
		};
	}
}
