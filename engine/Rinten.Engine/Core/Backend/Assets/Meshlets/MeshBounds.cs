namespace Rinten.Meshlets;

//Cluster bounding sphere and normal cone, ported from meshoptimizer's meshletutils.cpp
internal static class MeshBounds
{
	/// <summary>What a cluster is bounded by. Cone fields are zero for a cluster with no usable cone.</summary>
	public struct Cluster
	{
		public Vector3 Center;
		public float Radius;

		public Vector3 ConeApex;
		public Vector3 ConeAxis;
		public float ConeCutoff;
	}

	/// <summary>Centre and radius, the way the two sphere routines hand them back.</summary>
	public struct Sphere
	{
		public Vector3 Center;
		public float Radius;
	}

	private static readonly Vector3[] axes =
	{
		new( 1, 0, 0 ),
		new( 0, 1, 0 ),
		new( 0, 0, 1 ),
		new( 0.57735026f, 0.57735026f, 0.57735026f ),
		new( -0.57735026f, 0.57735026f, 0.57735026f ),
		new( 0.57735026f, -0.57735026f, 0.57735026f ),
		new( 0.57735026f, 0.57735026f, -0.57735026f ),
	};

	//A sphere around points, or around spheres when radii are given; the indices select which points, or all of them when empty.
	//The axis count is how many of the seven to try for the seed pair - three is enough for normals, which never spread far.
	private static Sphere BoundingSphere( ReadOnlySpan<Vector3> points, ReadOnlySpan<float> radii, int axisCount, ReadOnlySpan<uint> indices )
	{
		var count = indices.IsEmpty ? points.Length : indices.Length;

		Span<uint> pmin = stackalloc uint[7];
		Span<uint> pmax = stackalloc uint[7];
		Span<float> tmin = stackalloc float[7];
		Span<float> tmax = stackalloc float[7];

		for ( var axis = 0; axis < axisCount; ++axis )
		{
			pmin[axis] = pmax[axis] = 0;
			tmin[axis] = float.MaxValue;
			tmax[axis] = -float.MaxValue;
		}

		for ( var i = 0; i < count; ++i )
		{
			var v = indices.IsEmpty ? (uint)i : indices[i];
			ref readonly var p = ref points[(int)v];
			var r = radii.IsEmpty ? 0.0f : radii[(int)v];

			for ( var axis = 0; axis < axisCount; ++axis )
			{
				ref readonly var ax = ref axes[axis];

				var tp = ( ax.x * p.x ) + ( ax.y * p.y ) + ( ax.z * p.z );
				var tpmin = tp - r;
				var tpmax = tp + r;

				pmin[axis] = tpmin < tmin[axis] ? v : pmin[axis];
				pmax[axis] = tpmax > tmax[axis] ? v : pmax[axis];
				tmin[axis] = tpmin < tmin[axis] ? tpmin : tmin[axis];
				tmax[axis] = tpmax > tmax[axis] ? tpmax : tmax[axis];
			}
		}

		//The pair furthest apart is the seed diameter.
		var paxis = 0;
		var paxisdr = 0.0f;

		for ( var axis = 0; axis < axisCount; ++axis )
		{
			ref readonly var p1 = ref points[(int)pmin[axis]];
			ref readonly var p2 = ref points[(int)pmax[axis]];
			var r1 = radii.IsEmpty ? 0.0f : radii[(int)pmin[axis]];
			var r2 = radii.IsEmpty ? 0.0f : radii[(int)pmax[axis]];

			var d2 = ( ( p2.x - p1.x ) * ( p2.x - p1.x ) ) + ( ( p2.y - p1.y ) * ( p2.y - p1.y ) ) + ( ( p2.z - p1.z ) * ( p2.z - p1.z ) );
			var dr = MathF.Sqrt( d2 ) + r1 + r2;

			if ( dr > paxisdr )
			{
				paxisdr = dr;
				paxis = axis;
			}
		}

		{
			ref readonly var p1 = ref points[(int)pmin[paxis]];
			ref readonly var p2 = ref points[(int)pmax[paxis]];
			var r1 = radii.IsEmpty ? 0.0f : radii[(int)pmin[paxis]];
			var r2 = radii.IsEmpty ? 0.0f : radii[(int)pmax[paxis]];

			var paxisd = MathF.Sqrt( ( ( p2.x - p1.x ) * ( p2.x - p1.x ) ) + ( ( p2.y - p1.y ) * ( p2.y - p1.y ) ) + ( ( p2.z - p1.z ) * ( p2.z - p1.z ) ) );
			var paxisk = paxisd > 0 ? ( paxisd + r2 - r1 ) / ( 2 * paxisd ) : 0.0f;

			var cx = p1.x + ( ( p2.x - p1.x ) * paxisk );
			var cy = p1.y + ( ( p2.y - p1.y ) * paxisk );
			var cz = p1.z + ( ( p2.z - p1.z ) * paxisk );
			var radius = paxisdr / 2;

			//Grow towards whatever still sticks out, one point at a time.
			for ( var i = 0; i < count; ++i )
			{
				var v = indices.IsEmpty ? (uint)i : indices[i];
				ref readonly var p = ref points[(int)v];
				var r = radii.IsEmpty ? 0.0f : radii[(int)v];

				var d2 = ( ( p.x - cx ) * ( p.x - cx ) ) + ( ( p.y - cy ) * ( p.y - cy ) ) + ( ( p.z - cz ) * ( p.z - cz ) );
				var d = MathF.Sqrt( d2 );

				if ( d + r > radius )
				{
					var k = d > 0 ? ( d + r - radius ) / ( 2 * d ) : 0.0f;

					cx += k * ( p.x - cx );
					cy += k * ( p.y - cy );
					cz += k * ( p.z - cz );
					radius = ( radius + d + r ) / 2;
				}
			}

			return new Sphere { Center = new Vector3( cx, cy, cz ), Radius = radius };
		}
	}

	/// <summary>The bounds of one meshlet as the clusterizer laid it out: its vertex list into <paramref name="positions"/>, its triangles as bytes into that list.</summary>
	public static Cluster OfMeshlet( ReadOnlySpan<uint> meshletVertices, ReadOnlySpan<byte> meshletTriangles, ReadOnlySpan<Vector3> positions )
	{
		var triangleCount = meshletTriangles.Length / 3;

		//Plane per triangle, with .w completing the equation; degenerate ones are left out, they are invisible anyway.
		Span<float> normals = stackalloc float[triangleCount * 4];
		var triangles = 0;

		var cornerCount = 0;

		for ( var i = 0; i < triangleCount * 3; i += 3 )
		{
			byte ta = meshletTriangles[i], tb = meshletTriangles[i + 1], tc = meshletTriangles[i + 2];

			cornerCount = ta >= cornerCount ? ta + 1 : cornerCount;
			cornerCount = tb >= cornerCount ? tb + 1 : cornerCount;
			cornerCount = tc >= cornerCount ? tc + 1 : cornerCount;

			ref readonly var p0 = ref positions[(int)meshletVertices[ta]];
			ref readonly var p1 = ref positions[(int)meshletVertices[tb]];
			ref readonly var p2 = ref positions[(int)meshletVertices[tc]];

			float p10x = p1.x - p0.x, p10y = p1.y - p0.y, p10z = p1.z - p0.z;
			float p20x = p2.x - p0.x, p20y = p2.y - p0.y, p20z = p2.z - p0.z;

			var nx = ( p10y * p20z ) - ( p10z * p20y );
			var ny = ( p10z * p20x ) - ( p10x * p20z );
			var nz = ( p10x * p20y ) - ( p10y * p20x );

			var area = MathF.Sqrt( ( nx * nx ) + ( ny * ny ) + ( nz * nz ) );
			if ( area == 0.0f ) continue;

			nx /= area;
			ny /= area;
			nz /= area;

			normals[( triangles * 4 ) + 0] = nx;
			normals[( triangles * 4 ) + 1] = ny;
			normals[( triangles * 4 ) + 2] = nz;
			normals[( triangles * 4 ) + 3] = -( ( nx * p0.x ) + ( ny * p0.y ) + ( nz * p0.z ) );
			triangles++;
		}

		var bounds = new Cluster();

		if ( triangles == 0 ) return bounds;

		//The vertex sphere, whose centre the cone apex is pushed back from.
		var psphere = BoundingSphere( positions, ReadOnlySpan<float>.Empty, 7, meshletVertices[..cornerCount] );
		var center = psphere.Center;

		//The normals as points: the centre of their sphere is the best cone axis.
		Span<Vector3> normalPoints = stackalloc Vector3[triangles];
		for ( var i = 0; i < triangles; ++i )
			normalPoints[i] = new Vector3( normals[( i * 4 ) + 0], normals[( i * 4 ) + 1], normals[( i * 4 ) + 2] );

		var nsphere = BoundingSphere( normalPoints, ReadOnlySpan<float>.Empty, 3, ReadOnlySpan<uint>.Empty );

		var axis = nsphere.Center;
		var axisLength = MathF.Sqrt( ( axis.x * axis.x ) + ( axis.y * axis.y ) + ( axis.z * axis.z ) );
		var invAxisLength = axisLength == 0.0f ? 0.0f : 1.0f / axisLength;

		axis = new Vector3( axis.x * invAxisLength, axis.y * invAxisLength, axis.z * invAxisLength );

		//cos(angle / 2) of the tightest cone.
		var mindp = 1.0f;

		for ( var i = 0; i < triangles; ++i )
		{
			var dp = ( normals[( i * 4 ) + 0] * axis.x ) + ( normals[( i * 4 ) + 1] * axis.y ) + ( normals[( i * 4 ) + 2] * axis.z );
			mindp = dp < mindp ? dp : mindp;
		}

		bounds.Center = center;
		bounds.Radius = psphere.Radius;

		//Wider than a hemisphere, or close enough to it that the apex below goes unstable: no cone, the test always accepts.
		if ( mindp <= 0.1f )
		{
			bounds.ConeCutoff = 1;
			return bounds;
		}

		var maxt = 0.0f;

		//The point along centre - t * axis that is behind every triangle's plane.
		for ( var i = 0; i < triangles; ++i )
		{
			var dc = ( center.x * normals[( i * 4 ) + 0] ) + ( center.y * normals[( i * 4 ) + 1] ) + ( center.z * normals[( i * 4 ) + 2] ) + normals[( i * 4 ) + 3];
			var dn = ( axis.x * normals[( i * 4 ) + 0] ) + ( axis.y * normals[( i * 4 ) + 1] ) + ( axis.z * normals[( i * 4 ) + 2] );

			var t = dc / dn;
			maxt = t > maxt ? t : maxt;
		}

		bounds.ConeApex = new Vector3( center.x - ( axis.x * maxt ), center.y - ( axis.y * maxt ), center.z - ( axis.z * maxt ) );
		bounds.ConeAxis = axis;

		//The cone of normals opened by ninety degrees each side and inverted: -cos(a + 90) = sin(a).
		bounds.ConeCutoff = MathF.Sqrt( 1 - ( mindp * mindp ) );

		return bounds;
	}
}
