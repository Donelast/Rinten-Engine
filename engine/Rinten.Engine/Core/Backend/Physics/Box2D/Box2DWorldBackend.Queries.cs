using Rinten.Native.Box2D;
using static Rinten.Native.Box2D.Box2D;

namespace Rinten;

//Traces and overlap queries in the plane. A trace's shape is flattened the way a body's shapes are - a sphere is a circle, a box its outline
//seen side-on - and swept along the trace's XY; depth plays no part in what is hit, and the result carries the depth the trace was made at.
internal sealed unsafe partial class Box2DWorldBackend
{
	//A cast's shape as a point cloud, relative to where the cast starts.
	private static bool BuildProxy( in PhysicsTrace.Request.Shape shape, out b2ShapeProxy proxy )
	{
		proxy = default;
		var rotation = shape.StartRot;

		switch ( shape.Type )
		{
			case PhysicsTrace.Request.ShapeType.Sphere:
			{
				var radius = shape.Radius.x;

				//A zero-radius sphere is how the builder spells "ray".
				if ( radius <= 0.0f ) return false;

				proxy.points[0] = Box2DPlane.Vec( 0, 0 );
				proxy.count = 1;
				proxy.radius = radius;
				return true;
			}

			case PhysicsTrace.Request.ShapeType.Capsule:
			{
				proxy.points[0] = Box2DPlane.Vec( rotation * shape.Mins );
				proxy.points[1] = Box2DPlane.Vec( rotation * shape.Maxs );
				proxy.count = 2;
				proxy.radius = shape.Radius.x;
				return true;
			}

			case PhysicsTrace.Request.ShapeType.Box:
			{
				Span<Vector2> corners = stackalloc Vector2[8];
				for ( var i = 0; i < 8; i++ )
				{
					var corner = new Vector3(
						(i & 1) == 0 ? shape.Mins.x : shape.Maxs.x,
						(i & 2) == 0 ? shape.Mins.y : shape.Maxs.y,
						(i & 4) == 0 ? shape.Mins.z : shape.Maxs.z );

					corners[i] = Box2DPlane.Flat( rotation * corner );
				}

				return OutlineProxy( corners, 0.0f, out proxy );
			}

			case PhysicsTrace.Request.ShapeType.Cylinder:
			{
				const int segments = 12;
				Span<Vector2> rims = stackalloc Vector2[segments * 2];

				for ( var i = 0; i < segments; i++ )
				{
					var angle = MathF.Tau * i / segments;
					var cos = MathF.Cos( angle );
					var sin = MathF.Sin( angle );

					rims[i] = Box2DPlane.Flat( rotation * (shape.Mins + new Vector3( cos * shape.Radius.x, sin * shape.Radius.x, 0 )) );
					rims[segments + i] = Box2DPlane.Flat( rotation * (shape.Maxs + new Vector3( cos * shape.Radius.y, sin * shape.Radius.y, 0 )) );
				}

				return OutlineProxy( rims, 0.0f, out proxy );
			}
		}

		return false;
	}

	//The convex outline of some points as a proxy. Box2D takes eight at most; an outline with more is swept as its bounding box, which contains it.
	private static bool OutlineProxy( ReadOnlySpan<Vector2> points, float radius, out b2ShapeProxy proxy )
	{
		proxy = default;

		var hull = Box2DConvexDecomposition.ConvexHull( points );
		if ( hull.Count == 0 ) return false;

		if ( hull.Count <= 8 )
		{
			for ( var i = 0; i < hull.Count; i++ ) proxy.points[i] = Box2DPlane.Vec( hull[i] );
			proxy.count = hull.Count;
			proxy.radius = radius;
			return true;
		}

		var min = hull[0];
		var max = hull[0];
		foreach ( var p in hull )
		{
			min = new Vector2( MathF.Min( min.x, p.x ), MathF.Min( min.y, p.y ) );
			max = new Vector2( MathF.Max( max.x, p.x ), MathF.Max( max.y, p.y ) );
		}

		proxy.points[0] = Box2DPlane.Vec( min.x, min.y );
		proxy.points[1] = Box2DPlane.Vec( max.x, min.y );
		proxy.points[2] = Box2DPlane.Vec( max.x, max.y );
		proxy.points[3] = Box2DPlane.Vec( min.x, max.y );
		proxy.count = 4;
		proxy.radius = radius;
		return true;
	}

	//-- traces ---------------------------------------------------------------

	public PhysicsTraceResult Trace( in PhysicsTrace.Request request, Func<PhysicsShape, bool> filter, PhysicsBody sweptBody )
	{
		var hits = Cast( request, filter, sweptBody, all: false );
		if ( hits.Count == 0 ) return Miss( request );

		var best = 0;
		for ( var i = 1; i < hits.Count; i++ )
		{
			if ( hits[i].Fraction < hits[best].Fraction ) best = i;
		}

		return Build( request, hits[best] );
	}

	public PhysicsTraceResult[] TraceAll( in PhysicsTrace.Request request, Func<PhysicsShape, bool> filter, PhysicsBody sweptBody )
	{
		var hits = Cast( request, filter, sweptBody, all: true );
		if ( hits.Count == 0 ) return Array.Empty<PhysicsTraceResult>();

		hits.Sort( static ( a, b ) => a.Fraction.CompareTo( b.Fraction ) );

		//A shape made of several Box2D polygons can be passed through more than once; it is one thing in the way, at the nearest of them.
		var seen = new HashSet<PhysicsShape>();
		var results = new List<PhysicsTraceResult>( hits.Count );

		foreach ( var hit in hits )
		{
			var shape = Box2DRegistry.Shape( hit.Shape );
			if ( shape is null || !seen.Add( shape ) ) continue;

			results.Add( Build( request, hit ) );
		}

		return results.ToArray();
	}

	private List<CastHit> Cast( PhysicsTrace.Request request, Func<PhysicsShape, bool> filter, PhysicsBody sweptBody, bool all )
	{
		var state = castState ??= new CastState();
		state.Hits.Clear();
		state.All = all;

		if ( !IsValid ) return state.Hits;

		var swept = sweptBody?.Backend as Box2DBodyBackend;
		var rules = CollisionRules;

		state.Accept = id =>
		{
			var shape = Box2DRegistry.Shape( id );
			if ( shape is null ) return false;
			if ( swept is not null && shape.Body == sweptBody ) return false;

			return Passes( request, rules, shape, filter );
		};

		try
		{
			var origin = Box2DPlane.Vec( request.StartPos );
			var translation = Box2DPlane.Vec( request.EndPos - request.StartPos );

			if ( swept is not null && swept.IsValid )
			{
				//The body's own shapes, placed where the sweep starts and turned the way it asks
				//- not the request's StartShape, which for a body sweep is a sphere of no radius.
				var xf = Box2DPlane.Xf( Vector2.Zero, Box2DPlane.AngleOf( request.StartShape.StartRot ) );

				foreach ( var shape in swept.ShapeList )
				{
					if ( shape?.Backend is not Box2DShapeBackend s ) continue;

					foreach ( var part in s.Parts )
					{
						if ( !part.Proxy( xf, out var proxy ) ) continue;
						b2World_CastShape( world, origin, &proxy, translation, Box2DPlane.Everything, CastEntry(), null );
					}
				}
			}
			else if ( BuildProxy( request.StartShape, out var proxy ) )
			{
				b2World_CastShape( world, origin, &proxy, translation, Box2DPlane.Everything, CastEntry(), null );
			}
			else if ( translation.x * translation.x + translation.y * translation.y > 1e-12f )
			{
				b2World_CastRay( world, origin, translation, Box2DPlane.Everything, CastEntry(), null );
			}
		}
		finally
		{
			state.Accept = null;
		}

		return state.Hits;
	}

	private static bool Passes( in PhysicsTrace.Request request, CoreCollisionRules rules, PhysicsShape shape, Func<PhysicsShape, bool> filter )
	{
		if ( shape.Backend is Box2DShapeBackend s && s.IgnoresTraces ) return false;
		if ( request.TriggerFilter == 0 && shape.IsTrigger ) return false;
		if ( !PassesTags( request, shape ) ) return false;
		if ( !PassesRules( request, rules, shape ) ) return false;
		if ( filter is not null && !filter( shape ) ) return false;
		return true;
	}

	//The collision rules the trace took, resolved against the shape's tags by the table this world filters contacts with - as CorePhysicsTrace does.
	private static bool PassesRules( in PhysicsTrace.Request request, CoreCollisionRules rules, PhysicsShape shape )
	{
		if ( request.RulesLimit == 0 || rules is null ) return true;
		if ( shape.Backend is not Box2DShapeBackend s ) return true;

		return rules.Resolve( request.RulesMask, s.TagMask ) <= request.RulesLimit;
	}

	//The require / any / exclude tag rules, as CorePhysicsTrace applies them.
	private static bool PassesTags( in PhysicsTrace.Request request, PhysicsShape shape )
	{
		if ( shape.Backend is not Box2DShapeBackend s ) return true;

		fixed ( PhysicsTrace.Request* r = &request )
		{
			for ( var i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
			{
				var tag = r->TagExclude[i];
				if ( tag == 0 ) break;
				if ( s.Tags.Contains( tag ) ) return false;
			}

			for ( var i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
			{
				var tag = r->TagRequire[i];
				if ( tag == 0 ) break;
				if ( !s.Tags.Contains( tag ) ) return false;
			}

			var anyWanted = false;
			for ( var i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
			{
				var tag = r->TagAny[i];
				if ( tag == 0 ) break;

				anyWanted = true;
				if ( s.Tags.Contains( tag ) ) return true;
			}

			return !anyWanted;
		}
	}

	private static PhysicsTraceResult Build( in PhysicsTrace.Request request, in CastHit hit )
	{
		var shape = Box2DRegistry.Shape( hit.Shape );
		var start = request.StartPos;
		var end = request.EndPos;
		var at = Vector3.Lerp( start, end, hit.Fraction );
		var direction = Vector3.Direction( start, end );

		//A cast that begins inside something reports no normal. Pointing back along the trace is the one answer that cannot read as sliding.
		var startedSolid = hit.Fraction <= 0.0f && hit.Normal.LengthSquared < 1e-12f;
		var normal = startedSolid
			? new Vector3( -direction.x, -direction.y, 0 ).Normal
			: new Vector3( hit.Normal.x, hit.Normal.y, 0 );

		var tags = new TagBuffer16();
		if ( shape?.Backend is Box2DShapeBackend s )
		{
			foreach ( var tag in s.Tags ) tags.AddUnique( tag );
		}

		return new PhysicsTraceResult
		{
			Hit = true,
			StartedSolid = startedSolid,
			StartPosition = start,
			EndPosition = at,
			HitPosition = new Vector3( hit.Point.x, hit.Point.y, at.z ),
			Normal = normal,
			Fraction = hit.Fraction,
			Direction = direction,
			Triangle = -1,
			_rawTags = tags,

			Body = shape?.Body?.SelfOrParent,
			Shape = shape,
			Surface = shape?.Surface,

			StartShape = request.StartShape,
		};
	}

	private static PhysicsTraceResult Miss( in PhysicsTrace.Request request ) => new()
	{
		Hit = false,
		StartPosition = request.StartPos,
		EndPosition = request.EndPos,
		HitPosition = request.EndPos,
		Normal = Vector3.Zero,
		Fraction = 1.0f,
		Direction = Vector3.Direction( request.StartPos, request.EndPos ),
		StartShape = request.StartShape,
	};

	//-- overlaps -------------------------------------------------------------

	public int OverlapSphere( Vector3 center, float radius, List<PhysicsShape> results )
	{
		b2ShapeProxy proxy = default;
		proxy.points[0] = Box2DPlane.Vec( 0, 0 );
		proxy.count = 1;
		proxy.radius = radius;

		return Overlap( Box2DPlane.Vec( center ), proxy, results );
	}

	public int OverlapBox( BBox box, List<PhysicsShape> results )
	{
		var center = box.Center;
		var half = box.Size * 0.5f;

		b2ShapeProxy proxy = default;
		proxy.points[0] = Box2DPlane.Vec( -half.x, -half.y );
		proxy.points[1] = Box2DPlane.Vec( half.x, -half.y );
		proxy.points[2] = Box2DPlane.Vec( half.x, half.y );
		proxy.points[3] = Box2DPlane.Vec( -half.x, half.y );
		proxy.count = 4;

		return Overlap( Box2DPlane.Vec( center ), proxy, results );
	}

	public int OverlapConvex( ReadOnlySpan<Vector3> points, List<PhysicsShape> results )
	{
		if ( points.Length == 0 ) return 0;

		var origin = Vector3.Zero;
		foreach ( var p in points ) origin += p;
		origin /= points.Length;

		Span<Vector2> flat = points.Length <= 64 ? stackalloc Vector2[points.Length] : new Vector2[points.Length];
		for ( var i = 0; i < points.Length; i++ ) flat[i] = Box2DPlane.Flat( points[i] - origin );

		if ( !OutlineProxy( flat, 0.0f, out var proxy ) ) return 0;

		return Overlap( Box2DPlane.Vec( origin ), proxy, results );
	}

	[ThreadStatic] private static HashSet<PhysicsShape> overlapSeen;

	private int Overlap( b2Vec2 origin, b2ShapeProxy proxy, List<PhysicsShape> results )
	{
		if ( results is null || !IsValid ) return 0;

		var hits = overlapHits ??= new List<b2ShapeId>();
		hits.Clear();

		b2World_OverlapShape( world, origin, &proxy, Box2DPlane.Everything, OverlapEntry(), null );

		//One managed shape can be several Box2D polygons, each reported.
		var seen = overlapSeen ??= new HashSet<PhysicsShape>();
		seen.Clear();

		var added = 0;

		foreach ( var id in hits )
		{
			var shape = Box2DRegistry.Shape( id );
			if ( shape is null || !seen.Add( shape ) ) continue;

			results.Add( shape );
			added++;
		}

		hits.Clear();
		seen.Clear();
		return added;
	}
}
