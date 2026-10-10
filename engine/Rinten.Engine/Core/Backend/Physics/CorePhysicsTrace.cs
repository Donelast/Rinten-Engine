using System.Runtime.InteropServices;

namespace Rinten;

//Traces as inflated point clouds (one native entry); tag rules and filters applied here afterwards
internal static unsafe partial class CorePhysicsTrace
{
	//How many hits one trace can report. Box3D caps a cast's point cloud at 64; this is the other end - how much of the world one ray may pass through.
	private const int MaxHits = 64;

	//Segments per cap when a cylinder is approximated by a point cloud.
	private const int CylinderSegments = 12;

	//The most points any swept shape becomes - a cylinder's two rims, which is the largest of them.
	private const int MostTracePoints = CylinderSegments * 2;


	//What the traces actually did since the last read. A trace that is subtly wrong looks like nothing from the outside - the game just behaves
	//oddly - so these count what is being swept and how often it hits. A few seconds of the real game says more than any synthetic test.
	internal static class Stats
	{
		//A slot a thread each - see Rinten.Utility.SlotCounter. A trace is made from wherever the work is, and a particle that collides traces
		//once a step on the pool: ten counters declared next to each other put every core on the same cache line for every trace in the scene.
		public static readonly Rinten.Utility.SlotCounter Rays = new();
		public static readonly Rinten.Utility.SlotCounter Spheres = new();
		public static readonly Rinten.Utility.SlotCounter Boxes = new();
		public static readonly Rinten.Utility.SlotCounter Capsules = new();
		public static readonly Rinten.Utility.SlotCounter Cylinders = new();
		public static readonly Rinten.Utility.SlotCounter BodySweeps = new();
		public static readonly Rinten.Utility.SlotCounter Hits = new();
		public static readonly Rinten.Utility.SlotCounter Misses = new();
		public static readonly Rinten.Utility.SlotCounter StartedSolid = new();
		public static readonly Rinten.Utility.SlotCounter FilteredOut = new();

		/// <summary>How far the hits got, in tenths. A wheel ray that keeps reporting a hit at nothing but arm's length looks the same as a healthy one in a count of hits, and completely different here.</summary>
		public static readonly Rinten.Utility.SlotCounter[] Fractions =
			System.Linq.Enumerable.Range( 0, 11 ).Select( _ => new Rinten.Utility.SlotCounter() ).ToArray();

		public static void Record( float fraction )
		{
			var bucket = (int)(fraction * 10.0f);
			Fractions[bucket.Clamp( 0, 10 )].Add();
		}

		public static string TakeSummary()
		{
			var total = Rays.Total + Spheres.Total + Boxes.Total + Capsules.Total + Cylinders.Total + BodySweeps.Total;

			var summary = $"traces {total}: {Rays.Total} ray, {Spheres.Total} sphere, {Boxes.Total} box, " +
				$"{Capsules.Total} capsule, {Cylinders.Total} cylinder, {BodySweeps.Total} body sweep | " +
				$"{Hits.Total} hit, {Misses.Total} miss, {StartedSolid.Total} started solid, {FilteredOut.Total} filtered";

			summary += $"\n  hit distance: " + string.Join( " ", Fractions.Select( ( n, i ) => $"{i / 10.0f:0.0}:{n.Total}" ) );

			foreach ( var counter in new[] { Rays, Spheres, Boxes, Capsules, Cylinders, BodySweeps, Hits, Misses, StartedSolid, FilteredOut } )
				counter.Clear();

			foreach ( var counter in Fractions ) counter.Clear();

			return summary;
		}
	}

	//Results are built into a per-thread buffer. A trace result holds managed references so it cannot live on the stack, and
	//traces run every frame per moving thing - allocating one array per trace would be the most expensive part of tracing.
	[ThreadStatic]
	private static PhysicsTraceResult[] buffer;

	private static PhysicsTraceResult[] Buffer => buffer ??= new PhysicsTraceResult[MaxHits];

	/// <summary>Run a trace and return the first hit that passes the request's filters, or a miss that ends where the trace was aimed.</summary>
	public static PhysicsTraceResult Run( CorePhysicsWorldBackend world, in PhysicsTrace.Request request,
		Func<PhysicsShape, bool> filter, PhysicsBody sweptBody = null )
	{
		var results = Buffer;
		var count = Collect( world, request, filter, sweptBody, results, stopAtFirst: true );

		return count > 0 ? results[0] : Miss( request );
	}

	/// <summary>Run a trace and return every hit that passes, nearest first.</summary>
	public static PhysicsTraceResult[] RunAll( CorePhysicsWorldBackend world, in PhysicsTrace.Request request,
		Func<PhysicsShape, bool> filter, PhysicsBody sweptBody = null )
	{
		var results = Buffer;
		var count = Collect( world, request, filter, sweptBody, results, stopAtFirst: false );

		if ( count == 0 ) return Array.Empty<PhysicsTraceResult>();

		var copy = new PhysicsTraceResult[count];
		Array.Copy( results, copy, count );
		return copy;
	}

	private static int Collect( CorePhysicsWorldBackend world, in PhysicsTrace.Request request,
		Func<PhysicsShape, bool> filter, PhysicsBody sweptBody, PhysicsTraceResult[] results, bool stopAtFirst )
	{
		//Not asked whether the world is valid: the two calls below ask through CorePhysics.ResolveWorld and answer nothing where it is not,
		//so the check here was a second trip into the native library for every trace - and a particle that collides traces once a step.
		if ( world is null ) return 0;

		var start = request.StartPos;
		var end = request.EndPos;

		Span<CorePhysics.TraceHit> hits = stackalloc CorePhysics.TraceHit[MaxHits];
		int hitCount;

		if ( sweptBody?.Backend is CorePhysicsBodyBackend body )
		{
			Stats.BodySweeps.Add();

			//Trace.Body and Sweep move the body's own shapes, not the request's StartShape - which for those is
			//left at its default, a sphere of no radius. Sweeping that would be a bare ray through the world.
			hitCount = CorePhysics.WorldTraceBody( world.Handle, body.Handle, start, end,
				request.StartShape.StartRot, hits );
		}
		else
		{
			//On the stack: a trace's point cloud is read by the call below and never kept, and Trace is what a particle that collides does every
			//step of its life - measured at 70 MB over ten seconds on the test ground's FX scene, all of it arrays that die in this method.
			Span<float> points = stackalloc float[MostTracePoints * 3];

			var pointCount = BuildPointCloud( request.StartShape, points, out var radius );

			switch ( request.StartShape.Type )
			{
				case PhysicsTrace.Request.ShapeType.Sphere:
					if ( pointCount == 0 ) Stats.Rays.Add(); else Stats.Spheres.Add();
					break;
				case PhysicsTrace.Request.ShapeType.Box: Stats.Boxes.Add(); break;
				case PhysicsTrace.Request.ShapeType.Capsule: Stats.Capsules.Add(); break;
				case PhysicsTrace.Request.ShapeType.Cylinder: Stats.Cylinders.Add(); break;
			}

			hitCount = CorePhysics.WorldTrace( world.Handle, start, end,
				points[..( pointCount * 3 )], radius, hits );
		}

		var written = 0;

		for ( var i = 0; i < hitCount; i++ )
		{
			var shape = CoreShapeRegistry.Get( hits[i].Shape );
			if ( shape is null ) continue;

			if ( shape.Backend is CorePhysicsShapeBackend core && core.IgnoresTraces ) { Stats.FilteredOut.Add(); continue; }

			if ( !PassesTags( request, shape ) ) { Stats.FilteredOut.Add(); continue; }
			if ( !PassesRules( request, world.CollisionRules, shape ) ) { Stats.FilteredOut.Add(); continue; }
			if ( request.TriggerFilter == 0 && shape.IsTrigger ) { Stats.FilteredOut.Add(); continue; }
			if ( filter is not null && !filter( shape ) ) { Stats.FilteredOut.Add(); continue; }

			if ( hits[i].StartedSolid != 0 ) Stats.StartedSolid.Add();
			Stats.Record( hits[i].Fraction );

			results[written++] = Build( request, hits[i], shape );

			if ( stopAtFirst || written >= results.Length ) break;
		}

		if ( written > 0 ) Stats.Hits.Add();
		else Stats.Misses.Add();

		return written;
	}

	private static PhysicsTraceResult Build( in PhysicsTrace.Request request, in CorePhysics.TraceHit hit, PhysicsShape shape )
	{
		var start = request.StartPos;
		var aimedAt = request.EndPos;

		var hitPosition = new Vector3( hit.PointX, hit.PointY, hit.PointZ );
		var normal = new Vector3( hit.NormalX, hit.NormalY, hit.NormalZ );

		var tags = new TagBuffer16();
		if ( shape.Backend is CorePhysicsShapeBackend core )
		{
			foreach ( var tag in core.Tags ) tags.AddUnique( tag );
		}

		return new PhysicsTraceResult
		{
			Hit = hit.Fraction < 1.0f,

			//Only a genuine overlap at the start, not a cast that could not move because it was already
			//resting on something - the controllers treat this as "unstick me" and would never move otherwise.
			StartedSolid = hit.StartedSolid != 0,
			StartPosition = start,

			//Where the swept shape came to rest, not where its surface touched.
			EndPosition = Vector3.Lerp( start, aimedAt, hit.Fraction ),
			HitPosition = hitPosition,
			Normal = normal,
			Fraction = hit.Fraction,
			Direction = Vector3.Direction( start, aimedAt ),
			Triangle = hit.Triangle,
			_rawTags = tags,

			Body = shape.Body?.SelfOrParent,
			Shape = shape,
			Surface = shape.Surface,

			StartShape = request.StartShape,
		};
	}

	private static PhysicsTraceResult Miss( in PhysicsTrace.Request request )
	{
		return new PhysicsTraceResult
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
	}

	//The collision rules the trace took - see PhysicsTraceBuilder.WithCollisionRules
	//- resolved against the shape's tags by the table the world filters contacts with.
	private static bool PassesRules( in PhysicsTrace.Request request, CoreCollisionRules rules, PhysicsShape shape )
	{
		if ( request.RulesLimit == 0 || rules is null ) return true;
		if ( shape.Backend is not CorePhysicsShapeBackend core ) return true;

		return rules.Resolve( request.RulesMask, core.TagMask ) <= request.RulesLimit;
	}

	//The require / any / exclude tag rules, in that order of cheapness.
	private static bool PassesTags( in PhysicsTrace.Request request, PhysicsShape shape )
	{
		if ( shape.Backend is not CorePhysicsShapeBackend core ) return true;

		fixed ( PhysicsTrace.Request* r = &request )
		{
			for ( var i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
			{
				var tag = r->TagExclude[i];
				if ( tag == 0 ) break;
				if ( core.Tags.Contains( tag ) ) return false;
			}

			for ( var i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
			{
				var tag = r->TagRequire[i];
				if ( tag == 0 ) break;
				if ( !core.Tags.Contains( tag ) ) return false;
			}

			var anyWanted = false;
			for ( var i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
			{
				var tag = r->TagAny[i];
				if ( tag == 0 ) break;

				anyWanted = true;
				if ( core.Tags.Contains( tag ) ) return true;
			}

			return !anyWanted;
		}
	}

	//Writes the swept shape's points into the span (MostTracePoints) and returns the count, 0 for a ray
	private static int BuildPointCloud( in PhysicsTrace.Request.Shape shape, Span<float> points, out float radius )
	{
		var rotation = shape.StartRot;

		switch ( shape.Type )
		{
			case PhysicsTrace.Request.ShapeType.Sphere:
				radius = shape.Radius.x;

				//A zero-radius sphere is how the builder spells "ray".
				if ( radius <= 0.0f ) return 0;

				points[0] = 0.0f;
				points[1] = 0.0f;
				points[2] = 0.0f;
				return 1;

			case PhysicsTrace.Request.ShapeType.Box:
			{
				radius = 0.0f;

				var min = shape.Mins;
				var max = shape.Maxs;

				for ( var i = 0; i < 8; i++ )
				{
					var corner = new Vector3(
						(i & 1) == 0 ? min.x : max.x,
						(i & 2) == 0 ? min.y : max.y,
						(i & 4) == 0 ? min.z : max.z );

					Write( points, i, rotation * corner );
				}

				return 8;
			}

			case PhysicsTrace.Request.ShapeType.Capsule:
			{
				radius = shape.Radius.x;

				Write( points, 0, rotation * shape.Mins );
				Write( points, 1, rotation * shape.Maxs );
				return 2;
			}

			case PhysicsTrace.Request.ShapeType.Cylinder:
			{
				//Box3D has no cylinder primitive, so both rims become points and the convex hull of them
				//is the cylinder. Radius stays zero - inflating would round the rim into a capsule.
				radius = 0.0f;

				var bottom = shape.Radius.x;
				var top = shape.Radius.y;

				for ( var i = 0; i < CylinderSegments; i++ )
				{
					var angle = MathF.Tau * i / CylinderSegments;
					var cos = MathF.Cos( angle );
					var sin = MathF.Sin( angle );

					Write( points, i, rotation * (shape.Mins + new Vector3( cos * bottom, sin * bottom, 0 )) );
					Write( points, CylinderSegments + i, rotation * (shape.Maxs + new Vector3( cos * top, sin * top, 0 )) );
				}

				return CylinderSegments * 2;
			}

			default:
				radius = 0.0f;
				return 0;
		}
	}

	private static void Write( Span<float> points, int index, Vector3 value )
	{
		points[index * 3 + 0] = value.x;
		points[index * 3 + 1] = value.y;
		points[index * 3 + 2] = value.z;
	}
}
