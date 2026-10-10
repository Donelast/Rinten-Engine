namespace Rinten;

//Polygon triangulation by ear clipping, handling concave faces
internal static class CoreTriangulator
{
	//Cuts a face into triangles, three indices at a time into the points given. Returns how many indices were written.
	internal static int Polygon( ReadOnlySpan<Vector3> points, Span<int> indices )
	{
		if ( points.Length < 3 ) return 0;

		var wanted = (points.Length - 2) * 3;
		if ( indices.Length < wanted ) return 0;

		//Three points are already a triangle, whatever shape they are in.
		if ( points.Length == 3 )
		{
			indices[0] = 0;
			indices[1] = 1;
			indices[2] = 2;
			return 3;
		}

		var normal = Normal( points );

		//A ring with no area - every point in a line - has no triangles in it that would draw as anything.
		if ( normal.LengthSquared < 1e-12f ) return 0;

		//Flattened onto the plane it lies on, so the whole thing is a two-dimensional problem from here.
		var flat = new Vector2[points.Length];
		Flatten( points, normal, flat );

		var remaining = new int[points.Length];
		for ( var i = 0; i < remaining.Length; i++ ) remaining[i] = i;

		var count = remaining.Length;
		var written = 0;

		//The ring may be given either way round; ear clipping wants to know which, and the sign of the area says.
		var clockwise = SignedArea( flat, remaining, count ) < 0;

		//Every pass takes off at least one corner unless the ring is degenerate, so this cannot run away - the guard is for rings that are.
		var guard = count * count;

		while ( count > 3 && guard-- > 0 )
		{
			var cut = false;

			for ( var i = 0; i < count; i++ )
			{
				var previous = remaining[(i + count - 1) % count];
				var current = remaining[i];
				var next = remaining[(i + 1) % count];

				if ( !IsEar( flat, remaining, count, previous, current, next, clockwise ) ) continue;

				indices[written++] = previous;
				indices[written++] = current;
				indices[written++] = next;

				//Corner gone; the ring closes over it.
				for ( var j = i; j < count - 1; j++ ) remaining[j] = remaining[j + 1];
				count--;

				cut = true;
				break;
			}

			//Nothing could be cut - the ring crosses itself, or every corner has a point inside it. Fan the
			//rest rather than giving up on the face: a wrong triangle is easier to see than a missing surface.
			if ( !cut ) break;
		}

		for ( var i = 1; i < count - 1; i++ )
		{
			indices[written++] = remaining[0];
			indices[written++] = remaining[i];
			indices[written++] = remaining[i + 1];
		}

		return written;
	}

	//Which way the face points, by Newell's method - which works on a ring that is not quite flat, where three points picked out of it do not.
	private static Vector3 Normal( ReadOnlySpan<Vector3> points )
	{
		var normal = Vector3.Zero;

		for ( var i = 0; i < points.Length; i++ )
		{
			var current = points[i];
			var next = points[(i + 1) % points.Length];

			normal.x += (current.y - next.y) * (current.z + next.z);
			normal.y += (current.z - next.z) * (current.x + next.x);
			normal.z += (current.x - next.x) * (current.y + next.y);
		}

		return normal;
	}

	//The ring in two dimensions, on the plane it lies on.
	private static void Flatten( ReadOnlySpan<Vector3> points, Vector3 normal, Span<Vector2> flat )
	{
		//Pick a helper axis not parallel to the normal, using y since up is Y
		var up = MathF.Abs( normal.y ) < 0.9f * normal.Length ? Vector3.Up : Vector3.Forward;

		var across = Vector3.Cross( up, normal ).Normal;
		var down = Vector3.Cross( normal, across ).Normal;

		for ( var i = 0; i < points.Length; i++ )
			flat[i] = new Vector2( Vector3.Dot( points[i], across ), Vector3.Dot( points[i], down ) );
	}

	private static float SignedArea( ReadOnlySpan<Vector2> flat, ReadOnlySpan<int> ring, int count )
	{
		var area = 0.0f;

		for ( var i = 0; i < count; i++ )
		{
			var current = flat[ring[i]];
			var next = flat[ring[(i + 1) % count]];

			area += (current.x * next.y) - (next.x * current.y);
		}

		return area * 0.5f;
	}

	//Whether this corner can be cut off: it sticks out rather than in, and no other corner of the ring is inside the triangle it would leave.
	private static bool IsEar( ReadOnlySpan<Vector2> flat, ReadOnlySpan<int> ring, int count,
		int previous, int current, int next, bool clockwise )
	{
		var a = flat[previous];
		var b = flat[current];
		var c = flat[next];

		var cross = ((b.x - a.x) * (c.y - a.y)) - ((b.y - a.y) * (c.x - a.x));

		//Pointing into the face rather than out of it, or flat.
		if ( clockwise ? cross > -1e-9f : cross < 1e-9f ) return false;

		for ( var i = 0; i < count; i++ )
		{
			var index = ring[i];
			if ( index == previous || index == current || index == next ) continue;

			if ( Inside( a, b, c, flat[index] ) ) return false;
		}

		return true;
	}

	//Whether a point is inside a triangle, edges counting as in.
	private static bool Inside( Vector2 a, Vector2 b, Vector2 c, Vector2 point )
	{
		var d1 = Side( point, a, b );
		var d2 = Side( point, b, c );
		var d3 = Side( point, c, a );

		var negative = d1 < 0 || d2 < 0 || d3 < 0;
		var positive = d1 > 0 || d2 > 0 || d3 > 0;

		return !(negative && positive);
	}

	private static float Side( Vector2 point, Vector2 a, Vector2 b )
		=> ((point.x - b.x) * (a.y - b.y)) - ((a.x - b.x) * (point.y - b.y));
}
