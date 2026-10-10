namespace Rinten;

//Outlines in the plane for Box2D, whose polygons are convex and eight corners at most. Like
//ConvexDecomposition2D: a monotone-chain hull, and a fan cut of any hull with more corners than a polygon takes.
internal static class Box2DConvexDecomposition
{
	public const int MaxPolygonVertices = 8;

	/// <summary>Triangles thinner than this (square metres) are edge-on and dropped.</summary>
	public const float AreaEpsilon = 1e-5f;

	const float ConvexEpsilon = 1e-6f;

	//Points closer than this are one point (metres).
	const float WeldDistanceSquared = 1e-8f;

	/// <summary>The convex hull, counter-clockwise, with collinear points dropped.</summary>
	public static List<Vector2> ConvexHull( ReadOnlySpan<Vector2> points )
	{
		var unique = new List<Vector2>( points.Length );
		foreach ( var p in points )
		{
			if ( float.IsFinite( p.x ) && float.IsFinite( p.y ) )
				unique.Add( p );
		}

		unique.Sort( static ( a, b ) =>
		{
			int cmp = a.x.CompareTo( b.x );
			return cmp != 0 ? cmp : a.y.CompareTo( b.y );
		} );

		for ( var i = unique.Count - 1; i > 0; i-- )
		{
			if ( (unique[i] - unique[i - 1]).LengthSquared < WeldDistanceSquared )
				unique.RemoveAt( i );
		}

		if ( unique.Count < 3 )
			return unique;

		var hull = new Vector2[unique.Count * 2];
		int k = 0;

		for ( int i = 0; i < unique.Count; i++ )
		{
			while ( k >= 2 && Cross( hull[k - 2], hull[k - 1], unique[i] ) <= 0 )
				k--;
			hull[k++] = unique[i];
		}

		int lower = k + 1;
		for ( int i = unique.Count - 2; i >= 0; i-- )
		{
			while ( k >= lower && Cross( hull[k - 2], hull[k - 1], unique[i] ) <= 0 )
				k--;
			hull[k++] = unique[i];
		}

		var result = new List<Vector2>( k - 1 );
		for ( int i = 0; i < k - 1; i++ )
			result.Add( hull[i] );

		return result;
	}

	/// <summary>A convex outline as pieces Box2D can hold: itself when it is small enough, otherwise fans about its centroid, each as large as fits.</summary>
	public static List<Vector2[]> Split( List<Vector2> hull )
	{
		var results = new List<Vector2[]>();

		int n = hull.Count;
		if ( n < 3 )
			return results;

		if ( n <= MaxPolygonVertices )
		{
			results.Add( hull.ToArray() );
			return results;
		}

		var centroid = Vector2.Zero;
		foreach ( var p in hull )
			centroid += p;
		centroid /= n;

		Span<Vector2> candidate = stackalloc Vector2[MaxPolygonVertices];

		int startV = 0;
		while ( startV < n )
		{
			int endV = startV + 1;

			while ( endV < n )
			{
				int tryEnd = endV + 1;
				int polyVerts = (tryEnd - startV + 1) + 1;
				if ( polyVerts > MaxPolygonVertices )
					break;

				candidate[0] = centroid;
				for ( int j = 0; j <= tryEnd - startV; j++ )
					candidate[j + 1] = hull[(startV + j) % n];

				if ( !IsConvex( candidate[..polyVerts] ) )
					break;

				endV = tryEnd;
			}

			int count = endV - startV + 1;
			var poly = new Vector2[count + 1];
			poly[0] = centroid;
			for ( int j = 0; j < count; j++ )
				poly[j + 1] = hull[(startV + j) % n];

			results.Add( poly );

			startV = endV;
		}

		return results;
	}

	/// <summary>Twice the signed area of the triangle o, a, b.</summary>
	public static float Cross( Vector2 o, Vector2 a, Vector2 b )
		=> (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

	static bool IsConvex( ReadOnlySpan<Vector2> poly )
	{
		if ( poly.Length < 3 )
			return false;

		bool positive = false;
		bool negative = false;

		for ( int i = 0; i < poly.Length; i++ )
		{
			float turn = Cross( poly[i], poly[(i + 1) % poly.Length], poly[(i + 2) % poly.Length] );
			if ( turn > ConvexEpsilon )
				positive = true;
			else if ( turn < -ConvexEpsilon )
				negative = true;

			if ( positive && negative )
				return false;
		}

		return true;
	}
}
