namespace Rinten;

//Where DebugShapes puts what it builds: a line at a time, a triangle at a time. A struct handed in by reference, so the calls are made on the struct
//and inlined rather than through an interface. The gizmos' sink also marks every line as something the pointer can land on; the overlay's only draws.
internal interface IDebugShapeSink
{
	void Line( in Vector3 a, in Vector3 b );
	void Triangle( in Vector3 a, in Vector3 b, in Vector3 c );
}

//The shapes Gizmo.Draw draws, and the drawer builds for each view. Everything here is in
//whatever space the caller's transform says; nothing here knows about colour, depth or lifetime.
internal static class DebugShapes
{
	//-- lines -------------------------------------------------------------------

	/// <summary>The twelve edges of a box given by its eight corners: the bottom four, then the top four.</summary>
	public static void LineBox<T>( ref T sink, ReadOnlySpan<Vector3> corners ) where T : struct, IDebugShapeSink
	{
		//bottom
		sink.Line( corners[0], corners[1] );
		sink.Line( corners[1], corners[2] );
		sink.Line( corners[2], corners[3] );
		sink.Line( corners[3], corners[0] );

		//middle
		sink.Line( corners[0], corners[4] );
		sink.Line( corners[1], corners[5] );
		sink.Line( corners[2], corners[6] );
		sink.Line( corners[3], corners[7] );

		//top
		sink.Line( corners[4], corners[5] );
		sink.Line( corners[5], corners[6] );
		sink.Line( corners[6], corners[7] );
		sink.Line( corners[7], corners[4] );
	}

	public static void LineBox<T>( ref T sink, in BBox box ) where T : struct, IDebugShapeSink
	{
		LineBox( ref sink, stackalloc Vector3[8]
		{
			new Vector3( box.Mins.x, box.Mins.y, box.Mins.z ),
			new Vector3( box.Maxs.x, box.Mins.y, box.Mins.z ),
			new Vector3( box.Maxs.x, box.Maxs.y, box.Mins.z ),
			new Vector3( box.Mins.x, box.Maxs.y, box.Mins.z ),

			new Vector3( box.Mins.x, box.Mins.y, box.Maxs.z ),
			new Vector3( box.Maxs.x, box.Mins.y, box.Maxs.z ),
			new Vector3( box.Maxs.x, box.Maxs.y, box.Maxs.z ),
			new Vector3( box.Mins.x, box.Maxs.y, box.Maxs.z )
		} );
	}

	public static void LineFrustum<T>( ref T sink, in Frustum frustum ) where T : struct, IDebugShapeSink
	{
		LineBox( ref sink, stackalloc Vector3[8]
		{
			frustum.GetCorner( 0 ) ?? Vector3.Zero,
			frustum.GetCorner( 1 ) ?? Vector3.Zero,
			frustum.GetCorner( 2 ) ?? Vector3.Zero,
			frustum.GetCorner( 3 ) ?? Vector3.Zero,

			frustum.GetCorner( 4 ) ?? Vector3.Zero,
			frustum.GetCorner( 5 ) ?? Vector3.Zero,
			frustum.GetCorner( 6 ) ?? Vector3.Zero,
			frustum.GetCorner( 7 ) ?? Vector3.Zero
		} );
	}

	public static void LineTriangle<T>( ref T sink, in Vector3 a, in Vector3 b, in Vector3 c ) where T : struct, IDebugShapeSink
	{
		sink.Line( a, b );
		sink.Line( b, c );
		sink.Line( c, a );
	}

	/// <summary>An arc round <paramref name="center"/>: the point at angle t is <c>center + sinAxis·sin t + cosAxis·cos t</c>, both axes already the radius long, from <paramref name="startDegrees"/> through <paramref name="sweepDegrees"/> in <paramref name="sections"/> lines.</summary>
	public static void Arc<T>( ref T sink, in Vector3 center, in Vector3 sinAxis, in Vector3 cosAxis,
		float startDegrees, float sweepDegrees, int sections ) where T : struct, IDebugShapeSink
	{
		if ( sections < 1 ) return;

		var start = startDegrees.DegreeToRadian();
		var sweep = sweepDegrees.DegreeToRadian();

		var last = center + ( sinAxis * MathF.Sin( start ) ) + ( cosAxis * MathF.Cos( start ) );

		for ( var i = 0; i < sections; i++ )
		{
			var f = start + ( ( i + 1.0f ) / sections ) * sweep;
			var next = center + ( sinAxis * MathF.Sin( f ) ) + ( cosAxis * MathF.Cos( f ) );

			sink.Line( last, next );
			last = next;
		}
	}

	/// <summary>A circle facing <paramref name="forward"/>, starting from <paramref name="up"/> and turning towards <c>forward × up</c>.</summary>
	public static void Circle<T>( ref T sink, in Vector3 center, in Vector3 forward, in Vector3 up, float radius,
		float startDegrees = 0.0f, float sweepDegrees = 360.0f, int sections = 16 ) where T : struct, IDebugShapeSink
	{
		var right = Vector3.Cross( forward, up ).Normal;

		Arc( ref sink, center, right * radius, up * radius, startDegrees, sweepDegrees, sections );
	}

	/// <summary>A cylinder, or a cone where one radius is nought: a ring at each end and four lines down the side.</summary>
	public static void LineCylinder<T>( ref T sink, in Vector3 a, in Vector3 b, float radiusA, float radiusB, int segments ) where T : struct, IDebugShapeSink
	{
		segments = Math.Max( segments, 3 );
		radiusA = MathF.Abs( radiusA );
		radiusB = MathF.Abs( radiusB );

		var along = b - a;
		var rotation = Rotation.LookAt( along.IsNearZeroLength ? Vector3.Up : along.Normal );

		var left = rotation.Left;
		var up = rotation.Up;

		var step = MathF.Tau / segments;

		//Four lines down the side whatever the segment count - one per quarter.
		var every = segments / Math.Min( 4, segments );

		var lastA = a + ( up * radiusA );
		var lastB = b + ( up * radiusB );

		for ( var i = 1; i <= segments; i++ )
		{
			var offset = ( left * MathF.Sin( step * i ) ) + ( up * MathF.Cos( step * i ) );

			var nextA = a + ( offset * radiusA );
			var nextB = b + ( offset * radiusB );

			if ( radiusA > 0.0f ) sink.Line( lastA, nextA );
			if ( radiusB > 0.0f ) sink.Line( lastB, nextB );

			if ( ( i - 1 ) % every == 0 ) sink.Line( lastA, lastB );

			lastA = nextA;
			lastB = nextB;
		}
	}

	/// <summary>A cylinder with a half sphere on each end, drawn as rings and arcs.</summary>
	public static void LineCapsule<T>( ref T sink, in Capsule capsule, int segments = 12 ) where T : struct, IDebugShapeSink
	{
		var along = capsule.CenterB - capsule.CenterA;

		//No length: a ball, as its three rings.
		if ( along.IsNearZeroLength )
		{
			RingOf( ref sink, capsule.CenterA, Rotation.Identity, capsule.Radius );
			RingOf( ref sink, capsule.CenterA, new Angles( 0, 90, 0 ), capsule.Radius );
			RingOf( ref sink, capsule.CenterA, new Angles( 90, 0, 0 ), capsule.Radius );
			return;
		}

		var rotation = Rotation.LookAt( along );

		LineCylinder( ref sink, capsule.CenterA, capsule.CenterB, capsule.Radius, capsule.Radius, segments );

		//Two arcs over each end, square to each other.
		Circle( ref sink, capsule.CenterA, rotation.Left, rotation.Forward, capsule.Radius, 90, 180, segments );
		Circle( ref sink, capsule.CenterB, rotation.Left, rotation.Forward, capsule.Radius, 270, 180, segments );
		Circle( ref sink, capsule.CenterA, rotation.Up, rotation.Forward, capsule.Radius, 90, 180, segments );
		Circle( ref sink, capsule.CenterB, rotation.Up, rotation.Forward, capsule.Radius, 270, 180, segments );
	}

	/// <summary>A sphere as four rings: its own three, and one square to the eye, which is its outline from wherever it is seen.</summary>
	public static void SphereRings<T>( ref T sink, in Vector3 center, float radius, Rotation rotation, in Vector3 eye ) where T : struct, IDebugShapeSink
	{
		var facing = Rotation.LookAt( center - eye );

		RingOf( ref sink, center, facing, radius );
		RingOf( ref sink, center, rotation, radius );
		RingOf( ref sink, center, rotation * new Angles( 0, 90, 0 ), radius );
		RingOf( ref sink, center, rotation * new Angles( 90, 0, 0 ), radius );
	}

	//How many segments a sphere's ring is drawn with.
	private const int RingSegments = 16;

	//The unit ring, worked out once for the process - a sphere's four rings are then two multiplies and two
	//adds a point, which on a page, where all of it runs in the interpreter, was the difference that mattered.
	private static readonly (float Sin, float Cos)[] UnitRing = BuildRing();

	private static (float Sin, float Cos)[] BuildRing()
	{
		var ring = new (float, float)[RingSegments + 1];

		for ( var s = 0; s <= RingSegments; s++ )
		{
			var t = ( s % RingSegments ) / (float)RingSegments * MathF.Tau;
			ring[s] = (MathF.Sin( t ), MathF.Cos( t ));
		}

		return ring;
	}

	private static void RingOf<T>( ref T sink, in Vector3 center, Rotation rotation, float radius ) where T : struct, IDebugShapeSink
	{
		var right = rotation * ( Vector3.Right * radius );
		var up = rotation * ( Vector3.Up * radius );

		var last = center + ( right * UnitRing[0].Sin ) + ( up * UnitRing[0].Cos );

		for ( var s = 1; s <= RingSegments; s++ )
		{
			var next = center + ( right * UnitRing[s].Sin ) + ( up * UnitRing[s].Cos );

			sink.Line( last, next );
			last = next;
		}
	}

	/// <summary>A patch of grid lying in one of the three axis planes, <paramref name="cells"/> cells each way from the centre - minor lines, then the major ones every <paramref name="majorEvery"/>. Two passes so the caller can colour them apart.</summary>
	public static void GridLines<T>( ref T sink, in Vector3 center, Gizmo.GridAxis axis, Vector2 spacing, int cells, int majorEvery, bool major ) where T : struct, IDebugShapeSink
	{
		var (u, v) = axis switch
		{
			Gizmo.GridAxis.XY => (Vector3.Right, Vector3.Up),
			Gizmo.GridAxis.YZ => (Vector3.Up, Vector3.Backward),
			_ => (Vector3.Right, Vector3.Backward),
		};

		var reachU = spacing.x * cells;
		var reachV = spacing.y * cells;

		for ( var i = -cells; i <= cells; i++ )
		{
			if ( ( i % majorEvery == 0 ) != major ) continue;

			var atU = u * ( spacing.x * i );
			var atV = v * ( spacing.y * i );

			sink.Line( center + atU - ( v * reachV ), center + atU + ( v * reachV ) );
			sink.Line( center + atV - ( u * reachU ), center + atV + ( u * reachU ) );
		}
	}

	//-- solids ------------------------------------------------------------------

	public static void SolidBox<T>( ref T sink, in BBox box ) where T : struct, IDebugShapeSink
	{
		var mx = box.Maxs;
		var mn = box.Mins;

		Quad( ref sink, new Vector3( mx.x, mn.y, mn.z ), new Vector3( mx.x, mx.y, mn.z ), new Vector3( mx.x, mx.y, mx.z ), new Vector3( mx.x, mn.y, mx.z ) );
		Quad( ref sink, new Vector3( mn.x, mn.y, mn.z ), new Vector3( mn.x, mn.y, mx.z ), new Vector3( mn.x, mx.y, mx.z ), new Vector3( mn.x, mx.y, mn.z ) );
		Quad( ref sink, new Vector3( mn.x, mx.y, mn.z ), new Vector3( mn.x, mx.y, mx.z ), new Vector3( mx.x, mx.y, mx.z ), new Vector3( mx.x, mx.y, mn.z ) );
		Quad( ref sink, new Vector3( mn.x, mn.y, mn.z ), new Vector3( mx.x, mn.y, mn.z ), new Vector3( mx.x, mn.y, mx.z ), new Vector3( mn.x, mn.y, mx.z ) );
		Quad( ref sink, new Vector3( mn.x, mn.y, mn.z ), new Vector3( mn.x, mx.y, mn.z ), new Vector3( mx.x, mx.y, mn.z ), new Vector3( mx.x, mn.y, mn.z ) );
		Quad( ref sink, new Vector3( mn.x, mn.y, mx.z ), new Vector3( mx.x, mn.y, mx.z ), new Vector3( mx.x, mx.y, mx.z ), new Vector3( mn.x, mx.y, mx.z ) );
	}

	//Two triangles, a b c and c d a.
	private static void Quad<T>( ref T sink, in Vector3 a, in Vector3 b, in Vector3 c, in Vector3 d ) where T : struct, IDebugShapeSink
	{
		sink.Triangle( a, b, c );
		sink.Triangle( c, d, a );
	}

	/// <summary>A cone from a round base to a point, closed underneath.</summary>
	public static void SolidCone<T>( ref T sink, in Vector3 @base, in Vector3 extent, float radius, int segments = 10 ) where T : struct, IDebugShapeSink
	{
		segments = Math.Max( segments, 3 );

		var tip = @base + extent;
		var rotation = Rotation.LookAt( extent );

		var left = rotation.Left * radius;
		var up = rotation.Up * radius;
		var step = MathF.Tau / segments;

		var first = @base + up;
		var last = first;

		for ( var i = 1; i <= segments; i++ )
		{
			var next = i == segments ? first : @base + ( left * MathF.Sin( step * i ) ) + ( up * MathF.Cos( step * i ) );

			sink.Triangle( last, tip, next );

			if ( i > 1 && i < segments ) sink.Triangle( first, last, next );

			last = next;
		}
	}

	/// <summary>A disc, or part of one, in the plane of right and up.</summary>
	public static void SolidCircle<T>( ref T sink, in Vector3 center, float radius, float startDegrees, float sweepDegrees, int sections ) where T : struct, IDebugShapeSink
		=> SolidRing( ref sink, center, 0.0f, radius, startDegrees, sweepDegrees, sections );

	/// <summary>A flat ring, or part of one, in the plane of right and up.</summary>
	public static void SolidRing<T>( ref T sink, in Vector3 center, float innerRadius, float outerRadius, float startDegrees, float sweepDegrees, int sections ) where T : struct, IDebugShapeSink
	{
		if ( sections < 1 ) return;

		var start = startDegrees.DegreeToRadian();
		var sweep = sweepDegrees.DegreeToRadian();

		Vector3 At( float f ) => ( Vector3.Right * MathF.Sin( f ) ) + ( Vector3.Up * MathF.Cos( f ) );

		var last = At( start );

		for ( var i = 1; i <= sections; i++ )
		{
			var next = At( start + ( i / (float)sections ) * sweep );

			if ( innerRadius > 0.0f )
			{
				Quad( ref sink, center + last * outerRadius, center + next * outerRadius, center + next * innerRadius, center + last * innerRadius );
			}
			else
			{
				sink.Triangle( center + last * outerRadius, center + next * outerRadius, center );
			}

			last = next;
		}
	}

	/// <summary>A sphere of latitude bands, y up.</summary>
	public static void SolidSphere<T>( ref T sink, in Vector3 center, float radius, int hSegments = 8, int vSegments = 8 ) where T : struct, IDebugShapeSink
	{
		hSegments = Math.Max( hSegments, 3 );
		vSegments = Math.Max( vSegments, 2 );

		var middle = center;

		Vector3 At( int x, int y )
		{
			var vAngle = ( y / (float)vSegments ) * MathF.PI;
			var hAngle = ( x / (float)hSegments ) * MathF.Tau;

			return middle + new Vector3(
				radius * MathF.Sin( vAngle ) * MathF.Cos( hAngle ),
				radius * MathF.Cos( vAngle ),
				radius * MathF.Sin( vAngle ) * MathF.Sin( hAngle ) );
		}

		for ( var y = 0; y < vSegments; y++ )
		{
			for ( var x = 0; x < hSegments; x++ )
			{
				var p0 = At( x, y );
				var p1 = At( x + 1, y );
				var p2 = At( x, y + 1 );
				var p3 = At( x + 1, y + 1 );

				sink.Triangle( p0, p2, p1 );
				sink.Triangle( p1, p2, p3 );
			}
		}
	}

	/// <summary>A closed cylinder from <paramref name="start"/> to <paramref name="end"/>.</summary>
	public static void SolidCylinder<T>( ref T sink, in Vector3 start, in Vector3 end, float radius, int hSegments = 32 ) where T : struct, IDebugShapeSink
	{
		hSegments = Math.Max( hSegments, 3 );

		var (right, up) = Across( ( end - start ).Normal );

		Vector3 Around( int i )
		{
			var angle = i / (float)hSegments * MathF.Tau;
			return ( ( right * MathF.Cos( angle ) ) + ( up * MathF.Sin( angle ) ) ) * radius;
		}

		for ( var i = 0; i < hSegments; i++ )
		{
			var a = Around( i );
			var b = Around( i + 1 );

			sink.Triangle( start + a, start + b, end + a );
			sink.Triangle( end + a, start + b, end + b );

			sink.Triangle( end, end + b, end + a );
			sink.Triangle( start, start + a, start + b );
		}
	}

	/// <summary>A cylinder with a half sphere on each end.</summary>
	public static void SolidCapsule<T>( ref T sink, in Vector3 start, in Vector3 end, float radius, int hSegments, int vSegments ) where T : struct, IDebugShapeSink
	{
		hSegments = Math.Max( hSegments, 3 );
		var half = Math.Max( vSegments / 2, 1 );

		var axis = ( end - start ).Normal;
		var (right, forward) = Across( axis );

		Vector3 Round( int x ) => ( right * MathF.Cos( x / (float)hSegments * MathF.Tau ) ) + ( forward * MathF.Sin( x / (float)hSegments * MathF.Tau ) );

		//A point on the cap at `end` of the axis, `y` bands down from its pole.
		Vector3 Cap( in Vector3 at, float outward, int x, int y )
		{
			var angle = y / (float)half * MathF.PI * 0.5f;
			return at + ( axis * ( outward * radius * MathF.Cos( angle ) ) ) + ( Round( x ) * ( radius * MathF.Sin( angle ) ) );
		}

		for ( var y = 0; y < half; y++ )
		{
			for ( var x = 0; x < hSegments; x++ )
			{
				//The cap below the start, pole first.
				var a0 = Cap( start, -1.0f, x, y );
				var a1 = Cap( start, -1.0f, x + 1, y );
				var a2 = Cap( start, -1.0f, x, y + 1 );
				var a3 = Cap( start, -1.0f, x + 1, y + 1 );

				sink.Triangle( a0, a2, a1 );
				sink.Triangle( a1, a2, a3 );

				//And the one over the end.
				var b0 = Cap( end, 1.0f, x, y );
				var b1 = Cap( end, 1.0f, x + 1, y );
				var b2 = Cap( end, 1.0f, x, y + 1 );
				var b3 = Cap( end, 1.0f, x + 1, y + 1 );

				sink.Triangle( b0, b2, b1 );
				sink.Triangle( b1, b2, b3 );
			}
		}

		//The side between them.
		for ( var x = 0; x < hSegments; x++ )
		{
			var a = Round( x ) * radius;
			var b = Round( x + 1 ) * radius;

			sink.Triangle( start + a, end + a, start + b );
			sink.Triangle( start + b, end + a, end + b );
		}
	}

	//Two directions square to axis and to each other.
	private static (Vector3 Right, Vector3 Up) Across( in Vector3 axis )
	{
		var up = MathF.Abs( Vector3.Dot( axis, Vector3.Up ) ) > 0.99f ? Vector3.Forward : Vector3.Up;
		var right = Vector3.Cross( axis, up ).Normal;

		return (right, Vector3.Cross( right, axis ).Normal);
	}
}
