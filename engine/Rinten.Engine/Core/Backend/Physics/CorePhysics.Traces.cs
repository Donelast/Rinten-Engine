using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rinten.Native.Box3D;
using static Rinten.Native.Box3D.Box3D;
using static Rinten.Native.Box3D.Box3DMath;

namespace Rinten;

//Casts and overlaps; all hits are collected, filtered and sorted before trimming to the request
internal static unsafe partial class CorePhysics
{
	/// <summary>One hit of a cast, in world space.</summary>
	[StructLayout( LayoutKind.Sequential )]
	public struct TraceHit
	{
		public ulong Shape;
		public float PointX, PointY, PointZ;
		public float NormalX, NormalY, NormalZ;
		public float Fraction;
		public int Triangle;

		/// <summary>Started inside the shape, not merely touching; callers unstick themselves when this is set</summary>
		public int StartedSolid;

		public int Padding;
	}

	//Collects every hit of a cast in reused per-thread native memory, since Box3D calls it
	private struct TraceCollector
	{
		public TraceHit* Hits;
		public int Count;
		public int Capacity;
	}

	//Owns the per-thread hit buffer via [ThreadStatic], so it is freed when the thread ends
	private sealed class TraceScratch
	{
		public TraceHit* Hits;
		public int Capacity;

		~TraceScratch()
		{
			if ( Hits != null ) NativeMemory.Free( Hits );
		}
	}

	[ThreadStatic] private static TraceScratch traceScratch;

	private static void GrowTrace( TraceCollector* collector )
	{
		int grown = collector->Capacity == 0 ? 64 : collector->Capacity * 2;

		collector->Hits = (TraceHit*)NativeMemory.Realloc( collector->Hits, (nuint)grown * (nuint)sizeof( TraceHit ) );
		collector->Capacity = grown;
	}

	private static TraceCollector BorrowCollector()
	{
		var scratch = traceScratch ??= new TraceScratch();

		return new TraceCollector { Hits = scratch.Hits, Count = 0, Capacity = scratch.Capacity };
	}

	private static void ReturnCollector( in TraceCollector collector )
	{
		var scratch = traceScratch;
		if ( scratch is null ) return;

		scratch.Hits = collector.Hits;
		scratch.Capacity = collector.Capacity;
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static float OnTraceHit( b3ShapeId shapeId, b3Vec3 point, b3Vec3 normal, float fraction,
		ulong userMaterialId, int triangleIndex, int childIndex, void* context )
		=> CollectTraceHit( shapeId, point, normal, fraction, userMaterialId, triangleIndex, childIndex, context );

	//What both entries do once the arguments are in hand.
	private static float CollectTraceHit( b3ShapeId shapeId, b3Vec3 point, b3Vec3 normal, float fraction,
		ulong userMaterialId, int triangleIndex, int childIndex, void* context )
	{
		var collector = (TraceCollector*)context;

		if ( collector->Count == collector->Capacity ) GrowTrace( collector );

		ref TraceHit hit = ref collector->Hits[collector->Count++];

		hit.Shape = Pack( shapeId );
		hit.PointX = point.x; hit.PointY = point.y; hit.PointZ = point.z;

		//A normal is a direction: scaling it by the unit conversion would only denormalise it.
		hit.NormalX = normal.x; hit.NormalY = normal.y; hit.NormalZ = normal.z;

		hit.Fraction = fraction;
		hit.Triangle = triangleIndex;
		hit.StartedSolid = 0;
		hit.Padding = 0;

		//Keep going: the caller filters, so clipping the ray here would hide hits it might have wanted.
		return 1.0f;
	}

	//Collects the shapes an overlap query finds, into a fixed buffer.
	private struct OverlapCollector
	{
		public ulong* Shapes;
		public int Capacity;
		public int Count;
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static B3Bool OnOverlap( b3ShapeId shapeId, void* context ) => CollectOverlap( shapeId, context );

	//What both entries do once the arguments are in hand.
	private static B3Bool CollectOverlap( b3ShapeId shapeId, void* context )
	{
		var collector = (OverlapCollector*)context;

		collector->Shapes[collector->Count++] = Pack( shapeId );

		//Stop once the caller's buffer is full rather than overrun it.
		return collector->Count < collector->Capacity;
	}

	//Drops hits the sweep only rests against (not moving into), then sorts nearest first
	private static int DropRestingContacts( TraceHit* hits, int count, b3Vec3 direction )
	{
		int kept = 0;

		for ( int i = 0; i < count; ++i )
		{
			ref TraceHit hit = ref hits[i];

			bool drop = false;

			if ( hit.Fraction <= 0.0f )
			{
				float into = direction.x * hit.NormalX
					+ direction.y * hit.NormalY
					+ direction.z * hit.NormalZ;

				//No normal means deeply buried; treat it as a glancing touch so the caller does not wedge
				if ( into == 0.0f && hit.NormalX == 0.0f && hit.NormalY == 0.0f && hit.NormalZ == 0.0f )
					drop = true;
				else
					drop = into > -0.01f; //moving along the surface, or away from it
			}

			if ( drop ) continue;
			if ( kept != i ) hits[kept] = hit;
			++kept;
		}

		return kept;
	}

	//Nearest first, so a caller that wants one hit can stop at the first.
	private static void SortByFraction( TraceHit* hits, int count )
	{
		//Insertion sort: a trace that hits more than a handful of things is rare, and
		//this is stable, allocates nothing and beats a general sort at these sizes.
		for ( int i = 1; i < count; ++i )
		{
			TraceHit hit = hits[i];
			int j = i - 1;

			while ( j >= 0 && hits[j].Fraction > hit.Fraction )
			{
				hits[j + 1] = hits[j];
				--j;
			}

			hits[j + 1] = hit;
		}
	}

	/// <summary>Sweeps a point cloud through the world. An empty cloud is a ray.</summary>
	public static int WorldTrace( ulong world, Vector3 start, Vector3 end,
		ReadOnlySpan<float> points, float radius, Span<TraceHit> outHits )
	{
		if ( outHits.IsEmpty ) return 0;
		if ( !ResolveWorld( world, out var id ) ) return 0;

		b3Vec3 origin = ToBox3D( start );
		b3Vec3 translation = ToBox3D( end - start );

		b3QueryFilter filter = b3DefaultQueryFilter();

		int pointCount = points.Length / 3;
		TraceCollector collector = BorrowCollector();

		if ( pointCount <= 0 )
		{
			b3World_CastRay( id, origin, translation, filter, TraceHitEntry(), &collector );
		}
		else
		{
			b3Vec3* cloud = stackalloc b3Vec3[pointCount];
			for ( int i = 0; i < pointCount; ++i )
				cloud[i] = Vec3( points[i * 3], points[i * 3 + 1], points[i * 3 + 2] );

			b3ShapeProxy proxy = new() { points = cloud, count = pointCount, radius = radius };

			b3World_CastShape( id, origin, &proxy, translation, filter, TraceHitEntry(), &collector );
		}

		float length = b3Length( translation );

		if ( length > 0.0f )
		{
			b3Vec3 direction = Vec3( translation.x / length, translation.y / length, translation.z / length );
			collector.Count = DropRestingContacts( collector.Hits, collector.Count, direction );
		}

		SortByFraction( collector.Hits, collector.Count );

		//Zero fraction is either inside or resting; the overlap test to tell them apart runs only then
		bool anyAtZero = collector.Count > 0 && collector.Hits[0].Fraction <= 0.0f;

		if ( anyAtZero )
		{
			if ( pointCount <= 0 )
			{
				for ( int i = 0; i < collector.Count; ++i )
				{
					if ( collector.Hits[i].Fraction <= 0.0f ) collector.Hits[i].StartedSolid = 1;
				}
			}
			else
			{
				MarkStartedSolid( id, origin, points, pointCount, radius, filter, ref collector );
			}
		}

		int count = Math.Min( collector.Count, outHits.Length );

		for ( int i = 0; i < count; ++i ) outHits[i] = collector.Hits[i];

		ReturnCollector( collector );
		return count;
	}

	//Marks zero-fraction hits the shape truly began inside; shrinks it first to ignore resting contact
	private static void MarkStartedSolid( b3WorldId id, b3Vec3 origin,
		ReadOnlySpan<float> points, int pointCount, float radius, b3QueryFilter filter,
		ref TraceCollector collector )
	{
		b3Vec3* cloud = stackalloc b3Vec3[pointCount];
		b3Vec3 centroid = default;

		for ( int i = 0; i < pointCount; ++i )
		{
			cloud[i] = Vec3( points[i * 3], points[i * 3 + 1], points[i * 3 + 2] );

			centroid.x += cloud[i].x;
			centroid.y += cloud[i].y;
			centroid.z += cloud[i].z;
		}

		float inverse = 1.0f / pointCount;
		centroid.x *= inverse;
		centroid.y *= inverse;
		centroid.z *= inverse;

		float shrunkRadius = radius - B3_SPECULATIVE_DISTANCE;

		if ( shrunkRadius < 0.0f )
		{
			//No radius left to give: take the rest out of the point cloud by pulling it in towards its own middle.
			float remaining = -shrunkRadius;
			shrunkRadius = 0.0f;

			for ( int i = 0; i < pointCount; ++i )
			{
				b3Vec3 toCentre = Vec3( centroid.x - cloud[i].x, centroid.y - cloud[i].y, centroid.z - cloud[i].z );
				float length = b3Length( toCentre );

				if ( length <= remaining ) { cloud[i] = centroid; continue; }

				float scale = remaining / length;
				cloud[i].x += toCentre.x * scale;
				cloud[i].y += toCentre.y * scale;
				cloud[i].z += toCentre.z * scale;
			}
		}

		b3ShapeProxy proxy = new() { points = cloud, count = pointCount, radius = shrunkRadius };

		//Everything genuinely overlapping the shape where it started. One slot per hit
		//is always enough: a shape the cast never touched cannot be one we are marking.
		ulong* inside = stackalloc ulong[collector.Count];
		OverlapCollector overlapping = new() { Shapes = inside, Capacity = collector.Count, Count = 0 };

		b3World_OverlapShape( id, origin, &proxy, filter, OverlapEntry(), &overlapping );

		for ( int i = 0; i < collector.Count; ++i )
		{
			if ( collector.Hits[i].Fraction > 0.0f ) continue;

			for ( int j = 0; j < overlapping.Count; ++j )
			{
				if ( inside[j] != collector.Hits[i].Shape ) continue;

				collector.Hits[i].StartedSolid = 1;
				break;
			}
		}
	}

	/// <summary>Sweeps a body's own shapes, from a pose it may not be in.</summary>
	public static int WorldTraceBody( ulong world, ulong body, Vector3 start, Vector3 end,
		in Rotation? rotation, Span<TraceHit> outHits )
	{
		if ( outHits.IsEmpty ) return 0;
		if ( !ResolveWorld( world, out var worldId ) ) return 0;
		if ( !ResolveBody( body, out var bodyId ) ) return 0;

		int shapeCount = b3Body_GetShapeCount( bodyId );
		if ( shapeCount <= 0 ) return 0;

		Span<b3ShapeId> shapes = shapeCount <= StackShapes ? stackalloc b3ShapeId[shapeCount] : new b3ShapeId[shapeCount];
		int written = GetShapes( bodyId, shapes );

		b3Vec3 origin = ToBox3D( start );
		b3Vec3 translation = ToBox3D( end - start );

		//The shapes are described in the body's own frame, so the pose being swept from has to be applied to them.
		b3Quat pose = rotation is { } given ? ToBox3D( given ) : b3Quat_identity;

		b3QueryFilter filter = b3DefaultQueryFilter();

		TraceCollector collector = BorrowCollector();
		b3Vec3* cloud = stackalloc b3Vec3[B3Constants.B3_MAX_SHAPE_CAST_POINTS];

		for ( int i = 0; i < written; ++i )
		{
			b3ShapeId shape = shapes[i];

			int cloudCount = 0;
			float shapeRadius = 0.0f;

			switch ( b3Shape_GetType( shape ) )
			{
				case b3ShapeType.b3_sphereShape:
				{
					b3Sphere sphere = b3Shape_GetSphere( shape );
					cloud[cloudCount++] = sphere.center;
					shapeRadius = sphere.radius;
					break;
				}

				case b3ShapeType.b3_capsuleShape:
				{
					b3Capsule capsule = b3Shape_GetCapsule( shape );
					cloud[cloudCount++] = capsule.center1;
					cloud[cloudCount++] = capsule.center2;
					shapeRadius = capsule.radius;
					break;
				}

				case b3ShapeType.b3_hullShape:
				{
					b3HullData* hull = b3Shape_GetHull( shape );
					if ( hull == null ) continue;

					b3Vec3* hullPoints = b3GetHullPoints( hull );
					int usable = Math.Min( hull->vertexCount, B3Constants.B3_MAX_SHAPE_CAST_POINTS );

					for ( int p = 0; p < usable; ++p ) cloud[cloudCount++] = hullPoints[p];
					break;
				}

				default:
					//A mesh or a height field has no inside to sweep.
					continue;
			}

			if ( cloudCount == 0 ) continue;

			//Into the pose being swept from, and relative to the start, which is the frame b3World_CastShape works in.
			for ( int p = 0; p < cloudCount; ++p ) cloud[p] = b3RotateVector( pose, cloud[p] );

			b3ShapeProxy proxy = new() { points = cloud, count = cloudCount, radius = shapeRadius };

			b3World_CastShape( worldId, origin, &proxy, translation, filter, TraceHitEntry(), &collector );
		}

		//Never report the body sweeping itself, and drop surfaces the sweep is only resting against rather than moving into - see the note in the shape trace.
		float length = b3Length( translation );
		b3Vec3 direction = length > 0.0f
			? Vec3( translation.x / length, translation.y / length, translation.z / length )
			: default;

		int kept = 0;

		for ( int i = 0; i < collector.Count; ++i )
		{
			ref TraceHit hit = ref collector.Hits[i];

			if ( B3_ID_EQUALS( b3Shape_GetBody( UnpackShape( hit.Shape ) ), bodyId ) ) continue;

			if ( hit.Fraction <= 0.0f && length > 0.0f )
			{
				float into = direction.x * hit.NormalX
					+ direction.y * hit.NormalY
					+ direction.z * hit.NormalZ;

				if ( into > -0.01f ) continue;
			}

			if ( kept != i ) collector.Hits[kept] = hit;
			++kept;
		}

		collector.Count = kept;

		SortByFraction( collector.Hits, collector.Count );

		int count = Math.Min( collector.Count, outHits.Length );

		for ( int i = 0; i < count; ++i ) outHits[i] = collector.Hits[i];

		ReturnCollector( collector );
		return count;
	}

	/// <summary>Every shape a point cloud, inflated by a radius, is inside.</summary>
	public static int WorldOverlap( ulong world, Vector3 origin,
		ReadOnlySpan<float> points, float radius, Span<ulong> outShapes )
	{
		if ( outShapes.IsEmpty ) return 0;

		int pointCount = points.Length / 3;
		if ( pointCount <= 0 ) return 0;

		if ( !ResolveWorld( world, out var id ) ) return 0;

		b3Vec3* cloud = stackalloc b3Vec3[pointCount];
		for ( int i = 0; i < pointCount; ++i )
			cloud[i] = Vec3( points[i * 3], points[i * 3 + 1], points[i * 3 + 2] );

		b3ShapeProxy proxy = new() { points = cloud, count = pointCount, radius = radius };

		fixed ( ulong* shapes = outShapes )
		{
			OverlapCollector collector = new() { Shapes = shapes, Capacity = outShapes.Length, Count = 0 };

			b3World_OverlapShape( id, ToBox3D( origin ), &proxy, b3DefaultQueryFilter(), OverlapEntry(), &collector );

			return collector.Count;
		}
	}
}
