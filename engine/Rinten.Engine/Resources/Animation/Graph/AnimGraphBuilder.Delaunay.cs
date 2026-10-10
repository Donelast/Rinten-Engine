namespace Rinten;

public sealed partial class AnimGraphBuilder
{
	/// <summary>Delaunay triangulation + hull of a 2D blend space at compile time; collinear points yield hull only</summary>
	public static bool TriangulateBlendSpace( IReadOnlyList<Vector2> points,
		out List<int> triangles, out List<int> hull )
	{
		triangles = new List<int>();
		hull = new List<int>();

		if ( points is null || points.Count == 0 ) return false;

		hull = ConvexHull( points );

		//Fewer than three points is a line, which has a hull and no triangles - a blend space that is one is blended along rather than across.
		if ( points.Count < 3 ) return false;

		//Everything happens inside one big triangle that contains all the points; it is removed at the end, along with anything still touching it.
		var bounds = Bounds( points );

		var size = MathF.Max( bounds.Maxs.x - bounds.Mins.x, bounds.Maxs.y - bounds.Mins.y );

		//Every point in the same place: no space to triangulate.
		if ( size <= 0.0f ) return false;

		var middle = ( bounds.Mins + bounds.Maxs ) * 0.5f;

		var working = new List<Vector2>( points )
		{
			new( middle.x - ( 20.0f * size ), middle.y - size ),
			new( middle.x + ( 20.0f * size ), middle.y - size ),
			new( middle.x, middle.y + ( 20.0f * size ) ),
		};

		var count = points.Count;
		var mesh = new List<(int A, int B, int C)> { (count, count + 1, count + 2) };

		for ( var point = 0; point < count; point++ )
		{
			var removed = new List<(int A, int B, int C)>();

			for ( var i = mesh.Count - 1; i >= 0; i-- )
			{
				if ( !InCircumcircle( working, mesh[i], working[point] ) ) continue;

				removed.Add( mesh[i] );
				mesh.RemoveAt( i );
			}

			//The hole the removed triangles left is bounded by the edges only one of them had; the shared ones are inside it.
			var edges = new List<(int A, int B)>();

			foreach ( var (a, b, c) in removed )
			{
				AddEdge( edges, a, b );
				AddEdge( edges, b, c );
				AddEdge( edges, c, a );
			}

			foreach ( var (a, b) in edges ) mesh.Add( (a, b, point) );
		}

		foreach ( var (a, b, c) in mesh )
		{
			if ( a >= count || b >= count || c >= count ) continue;

			triangles.Add( a );
			triangles.Add( b );
			triangles.Add( c );
		}

		return triangles.Count > 0;
	}

	//An edge, kept only if it is not already there - and dropped along with the one it matched
	//if it is, because an edge two triangles shared is inside the hole rather than around it.
	private static void AddEdge( List<(int A, int B)> edges, int a, int b )
	{
		for ( var i = 0; i < edges.Count; i++ )
		{
			var edge = edges[i];
			if ( ( edge.A != a || edge.B != b ) && ( edge.A != b || edge.B != a ) ) continue;

			edges.RemoveAt( i );
			return;
		}

		edges.Add( (a, b) );
	}

	private static bool InCircumcircle( List<Vector2> points, (int A, int B, int C) triangle, Vector2 point )
	{
		var a = points[triangle.A];
		var b = points[triangle.B];
		var c = points[triangle.C];

		//The usual determinant, with the winding taken out of it so the test does not depend on which way round the triangle was built.
		var ax = a.x - point.x;
		var ay = a.y - point.y;
		var bx = b.x - point.x;
		var by = b.y - point.y;
		var cx = c.x - point.x;
		var cy = c.y - point.y;

		var determinant =
			( ( ( ax * ax ) + ( ay * ay ) ) * ( ( bx * cy ) - ( cx * by ) ) ) -
			( ( ( bx * bx ) + ( by * by ) ) * ( ( ax * cy ) - ( cx * ay ) ) ) +
			( ( ( cx * cx ) + ( cy * cy ) ) * ( ( ax * by ) - ( bx * ay ) ) );

		var winding =
			( ( b.x - a.x ) * ( c.y - a.y ) ) -
			( ( c.x - a.x ) * ( b.y - a.y ) );

		return winding < 0.0f ? determinant < 0.0f : determinant > 0.0f;
	}

	//The outline around the points, in order. A value outside the blend space is pulled onto this rather than left to fall through the triangles.
	private static List<int> ConvexHull( IReadOnlyList<Vector2> points )
	{
		var order = Enumerable.Range( 0, points.Count ).ToList();
		if ( points.Count < 3 ) return order;

		order.Sort( ( left, right ) =>
		{
			var a = points[left];
			var b = points[right];

			return a.x != b.x ? a.x.CompareTo( b.x ) : a.y.CompareTo( b.y );
		} );

		var lower = new List<int>();
		foreach ( var index in order )
		{
			while ( lower.Count >= 2 && Cross( points, lower[^2], lower[^1], index ) <= 0.0f ) lower.RemoveAt( lower.Count - 1 );
			lower.Add( index );
		}

		var upper = new List<int>();
		for ( var i = order.Count - 1; i >= 0; i-- )
		{
			var index = order[i];

			while ( upper.Count >= 2 && Cross( points, upper[^2], upper[^1], index ) <= 0.0f ) upper.RemoveAt( upper.Count - 1 );
			upper.Add( index );
		}

		//Both lists end where the other begins.
		lower.RemoveAt( lower.Count - 1 );
		upper.RemoveAt( upper.Count - 1 );

		lower.AddRange( upper );

		return lower;
	}

	private static float Cross( IReadOnlyList<Vector2> points, int origin, int a, int b )
	{
		var o = points[origin];

		return ( ( points[a].x - o.x ) * ( points[b].y - o.y ) ) -
			( ( points[a].y - o.y ) * ( points[b].x - o.x ) );
	}

	private static BBox Bounds( IReadOnlyList<Vector2> points )
	{
		var mins = new Vector3( points[0].x, points[0].y, 0.0f );
		var maxs = mins;

		foreach ( var point in points )
		{
			mins = new Vector3( MathF.Min( mins.x, point.x ), MathF.Min( mins.y, point.y ), 0.0f );
			maxs = new Vector3( MathF.Max( maxs.x, point.x ), MathF.Max( maxs.y, point.y ), 0.0f );
		}

		return new BBox( mins, maxs );
	}
}
