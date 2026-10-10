namespace Rinten.UI;

//Tessellated vector vertex; carries across/along distances so the shader computes 1px AA coverage
internal struct VectorVertex
{
	public Vector2 Position;

	/// <summary>How far across the line the vertex is from its middle; the edge is at <see cref="Half"/>.</summary>
	public float Across;

	/// <summary>Half the line's width - how far across the edge lies. A fill's is half a fringe.</summary>
	public float Half;

	/// <summary>How far in from a cut end the vertex is: negative past the end, positive inside it.</summary>
	public float End;
}

//A path's outline after flattening: points in pixels, and which of them are corners of the
//path rather than steps along one of its curves. A stroke's joins are only drawn at corners.
internal sealed class VectorContour
{
	public readonly List<Vector2> Points = new();
	public readonly List<bool> Corners = new();
	public bool Closed;

	public void Add( Vector2 p, bool corner )
	{
		//A point on top of the last one says nothing, except that it may be a corner.
		if ( Points.Count > 0 )
		{
			var d = p - Points[^1];

			if ( d.x * d.x + d.y * d.y < 1e-6f )
			{
				if ( corner ) Corners[^1] = true;
				return;
			}
		}

		Points.Add( p );
		Corners.Add( corner );
	}
}

//How a line is drawn: its width, its ends, its corners, its dashes.
internal struct VectorStrokeStyle
{
	/// <summary>The whole width, in pixels.</summary>
	public float Width;
	public StrokeLinecap Cap;
	public StrokeLinejoin Join;
	public float MiterLimit;

	/// <summary>Dash and gap lengths in pixels, repeating; null or empty for a solid line.</summary>
	public float[] Dashes;
	public float DashOffset;
}

//CPU tessellation of vector strokes and fills with a ~1px AA fringe, based on nanovg's stroker
internal static class VectorTessellator
{
	//-- flattening -------------------------------------------------------------------------

	/// <summary>A path through a transform into contours of pixels, curves cut into lines no further than <paramref name="tolerance"/> pixels from them.</summary>
	public static void Flatten( VectorPath path, in VectorTransform toPixels, float tolerance, List<VectorContour> into )
	{
		tolerance = MathF.Max( tolerance, 0.01f );

		VectorContour contour = null;
		var point = 0;

		for ( var v = 0; v < path.Verbs.Count; v++ )
		{
			switch ( path.Verbs[v] )
			{
				case VectorPath.Verb.Move:
				{
					contour = new VectorContour();
					into.Add( contour );
					contour.Add( toPixels.Apply( path.Points[point++] ), true );
					break;
				}

				case VectorPath.Verb.Line:
				{
					contour?.Add( toPixels.Apply( path.Points[point] ), true );
					point++;
					break;
				}

				case VectorPath.Verb.Cubic:
				{
					if ( contour is null || contour.Points.Count == 0 ) { point += 3; break; }

					var p0 = contour.Points[^1];
					var p1 = toPixels.Apply( path.Points[point] );
					var p2 = toPixels.Apply( path.Points[point + 1] );
					var p3 = toPixels.Apply( path.Points[point + 2] );
					point += 3;

					//Wang's bound: how many lines keep every point of the curve within the tolerance.
					var ddx = MathF.Max( MathF.Abs( p0.x - 2 * p1.x + p2.x ), MathF.Abs( p1.x - 2 * p2.x + p3.x ) );
					var ddy = MathF.Max( MathF.Abs( p0.y - 2 * p1.y + p2.y ), MathF.Abs( p1.y - 2 * p2.y + p3.y ) );
					var dd = MathF.Sqrt( ddx * ddx + ddy * ddy );
					var steps = Math.Clamp( (int)MathF.Ceiling( MathF.Sqrt( 0.75f * dd / tolerance ) ), 1, 512 );

					for ( var i = 1; i < steps; i++ )
					{
						var t = i / (float)steps;
						var u = 1 - t;
						var q = p0 * ( u * u * u ) + p1 * ( 3 * u * u * t ) + p2 * ( 3 * u * t * t ) + p3 * ( t * t * t );
						contour.Add( q, false );
					}

					contour.Add( p3, true );
					break;
				}

				case VectorPath.Verb.Close:
				{
					if ( contour is not null ) contour.Closed = true;
					contour = null;
					break;
				}
			}
		}

		//A closed contour ends where it began; the closing length is implied.
		foreach ( var c in into )
		{
			if ( !c.Closed || c.Points.Count < 2 ) continue;

			var d = c.Points[^1] - c.Points[0];

			if ( d.x * d.x + d.y * d.y < 1e-6f )
			{
				c.Points.RemoveAt( c.Points.Count - 1 );
				c.Corners.RemoveAt( c.Corners.Count - 1 );
				c.Corners[0] = true;
			}
		}
	}

	//-- the working points -----------------------------------------------------------------

	[Flags]
	private enum PointFlags : byte
	{
		Corner = 1,
		Left = 2,
		Bevel = 4,
		InnerBevel = 8,
	}

	//A point of the contour being expanded, with nanovg's working: the way to the next, and the extrusion at it.
	private struct Pt
	{
		public float X, Y;
		public float Dx, Dy;
		public float Len;
		public float Dmx, Dmy;
		public PointFlags Flags;
	}

	[ThreadStatic] private static Pt[] pts;
	[ThreadStatic] private static List<(Vector2 P, float U, float V)> strip;

	//The contour's points with their directions and lengths worked out, or nought where there are not two of them.
	private static int Prepare( List<Vector2> points, List<bool> corners, bool closed, bool allCorners )
	{
		var count = points.Count;
		if ( pts is null || pts.Length < count ) pts = new Pt[Math.Max( count, 64 )];

		for ( var i = 0; i < count; i++ )
		{
			pts[i] = new Pt
			{
				X = points[i].x,
				Y = points[i].y,
				Flags = allCorners || corners is null || corners[i] ? PointFlags.Corner : 0,
			};
		}

		if ( count < 2 ) return count;

		//An open contour's ends are always corners.
		if ( !closed )
		{
			pts[0].Flags |= PointFlags.Corner;
			pts[count - 1].Flags |= PointFlags.Corner;
		}

		for ( var i = 0; i < count; i++ )
		{
			//Each point's way to the next; an open contour's last point keeps the one before its.
			var next = i + 1 < count ? i + 1 : ( closed ? 0 : -1 );

			if ( next < 0 )
			{
				pts[i].Dx = pts[i - 1].Dx;
				pts[i].Dy = pts[i - 1].Dy;
				pts[i].Len = 0;
				continue;
			}

			var dx = pts[next].X - pts[i].X;
			var dy = pts[next].Y - pts[i].Y;
			var len = MathF.Sqrt( dx * dx + dy * dy );

			if ( len > 1e-6f ) { dx /= len; dy /= len; }

			pts[i].Dx = dx;
			pts[i].Dy = dy;
			pts[i].Len = len;
		}

		return count;
	}

	//nanovg's calculateJoins: the extrusion at every point, which way it turns, and whether it must be bevelled.
	private static void CalculateJoins( int count, bool closed, float w, StrokeLinejoin join, float miterLimit )
	{
		var iw = w > 0 ? 1.0f / w : 0.0f;

		for ( var i = 0; i < count; i++ )
		{
			ref var p1 = ref pts[i];

			//The point before: the last, for a loop; for an open contour's first point, itself.
			var i0 = i > 0 ? i - 1 : ( closed ? count - 1 : 0 );
			ref var p0 = ref pts[i0];

			var dlx0 = p0.Dy;
			var dly0 = -p0.Dx;
			var dlx1 = p1.Dy;
			var dly1 = -p1.Dx;

			p1.Dmx = ( dlx0 + dlx1 ) * 0.5f;
			p1.Dmy = ( dly0 + dly1 ) * 0.5f;

			var dmr2 = p1.Dmx * p1.Dmx + p1.Dmy * p1.Dmy;

			if ( dmr2 > 0.000001f )
			{
				var scale = MathF.Min( 1.0f / dmr2, 600.0f );
				p1.Dmx *= scale;
				p1.Dmy *= scale;
			}

			p1.Flags &= PointFlags.Corner;

			var cross = p1.Dx * p0.Dy - p0.Dx * p1.Dy;
			if ( cross > 0.0f ) p1.Flags |= PointFlags.Left;

			//Too short either side to reach the inside corner: bevel the inside instead.
			var limit = MathF.Max( 1.01f, MathF.Min( p0.Len, p1.Len ) * iw );
			if ( dmr2 * limit * limit < 1.0f ) p1.Flags |= PointFlags.InnerBevel;

			if ( ( p1.Flags & PointFlags.Corner ) != 0 )
			{
				if ( dmr2 * miterLimit * miterLimit < 1.0f || join == StrokeLinejoin.Bevel || join == StrokeLinejoin.Round )
					p1.Flags |= PointFlags.Bevel;
			}
		}
	}

	private static void Put( float x, float y, float u, float v ) => strip.Add( (new Vector2( x, y ), u, v) );

	private static void ChooseBevel( bool bevel, in Pt p0, in Pt p1, float w, out float x0, out float y0, out float x1, out float y1 )
	{
		if ( bevel )
		{
			x0 = p1.X + p0.Dy * w;
			y0 = p1.Y - p0.Dx * w;
			x1 = p1.X + p1.Dy * w;
			y1 = p1.Y - p1.Dx * w;
		}
		else
		{
			x0 = p1.X + p1.Dmx * w;
			y0 = p1.Y + p1.Dmy * w;
			x1 = x0;
			y1 = y0;
		}
	}

	private static void BevelJoin( in Pt p0, in Pt p1, float lw, float rw, float lu, float ru )
	{
		var dlx0 = p0.Dy;
		var dly0 = -p0.Dx;
		var dlx1 = p1.Dy;
		var dly1 = -p1.Dx;

		var inner = ( p1.Flags & PointFlags.InnerBevel ) != 0;
		var bevel = ( p1.Flags & PointFlags.Bevel ) != 0;

		if ( ( p1.Flags & PointFlags.Left ) != 0 )
		{
			ChooseBevel( inner, p0, p1, lw, out var lx0, out var ly0, out var lx1, out var ly1 );

			Put( lx0, ly0, lu, 1 );
			Put( p1.X - dlx0 * rw, p1.Y - dly0 * rw, ru, 1 );

			if ( bevel )
			{
				Put( lx0, ly0, lu, 1 );
				Put( p1.X - dlx0 * rw, p1.Y - dly0 * rw, ru, 1 );
				Put( lx1, ly1, lu, 1 );
				Put( p1.X - dlx1 * rw, p1.Y - dly1 * rw, ru, 1 );
			}
			else
			{
				var rx0 = p1.X - p1.Dmx * rw;
				var ry0 = p1.Y - p1.Dmy * rw;

				Put( p1.X, p1.Y, 0.5f, 1 );
				Put( p1.X - dlx0 * rw, p1.Y - dly0 * rw, ru, 1 );
				Put( rx0, ry0, ru, 1 );
				Put( rx0, ry0, ru, 1 );
				Put( p1.X, p1.Y, 0.5f, 1 );
				Put( p1.X - dlx1 * rw, p1.Y - dly1 * rw, ru, 1 );
			}

			Put( lx1, ly1, lu, 1 );
			Put( p1.X - dlx1 * rw, p1.Y - dly1 * rw, ru, 1 );
		}
		else
		{
			ChooseBevel( inner, p0, p1, -rw, out var rx0, out var ry0, out var rx1, out var ry1 );

			Put( p1.X + dlx0 * lw, p1.Y + dly0 * lw, lu, 1 );
			Put( rx0, ry0, ru, 1 );

			if ( bevel )
			{
				Put( p1.X + dlx0 * lw, p1.Y + dly0 * lw, lu, 1 );
				Put( rx0, ry0, ru, 1 );
				Put( p1.X + dlx1 * lw, p1.Y + dly1 * lw, lu, 1 );
				Put( rx1, ry1, ru, 1 );
			}
			else
			{
				var lx0 = p1.X + p1.Dmx * lw;
				var ly0 = p1.Y + p1.Dmy * lw;

				Put( p1.X + dlx0 * lw, p1.Y + dly0 * lw, lu, 1 );
				Put( p1.X, p1.Y, 0.5f, 1 );
				Put( lx0, ly0, lu, 1 );
				Put( lx0, ly0, lu, 1 );
				Put( p1.X + dlx1 * lw, p1.Y + dly1 * lw, lu, 1 );
				Put( p1.X, p1.Y, 0.5f, 1 );
			}

			Put( p1.X + dlx1 * lw, p1.Y + dly1 * lw, lu, 1 );
			Put( rx1, ry1, ru, 1 );
		}
	}

	private static void RoundJoin( in Pt p0, in Pt p1, float lw, float rw, float lu, float ru, int ncap )
	{
		var dlx0 = p0.Dy;
		var dly0 = -p0.Dx;
		var dlx1 = p1.Dy;
		var dly1 = -p1.Dx;

		var inner = ( p1.Flags & PointFlags.InnerBevel ) != 0;

		if ( ( p1.Flags & PointFlags.Left ) != 0 )
		{
			ChooseBevel( inner, p0, p1, lw, out var lx0, out var ly0, out var lx1, out var ly1 );

			var a0 = MathF.Atan2( -dly0, -dlx0 );
			var a1 = MathF.Atan2( -dly1, -dlx1 );
			if ( a1 > a0 ) a1 -= MathF.PI * 2;

			Put( lx0, ly0, lu, 1 );
			Put( p1.X - dlx0 * rw, p1.Y - dly0 * rw, ru, 1 );

			var n = Math.Clamp( (int)MathF.Ceiling( ( a0 - a1 ) / MathF.PI * ncap ), 2, ncap );

			for ( var i = 0; i < n; i++ )
			{
				var a = a0 + ( i / (float)( n - 1 ) ) * ( a1 - a0 );

				Put( p1.X, p1.Y, 0.5f, 1 );
				Put( p1.X + MathF.Cos( a ) * rw, p1.Y + MathF.Sin( a ) * rw, ru, 1 );
			}

			Put( lx1, ly1, lu, 1 );
			Put( p1.X - dlx1 * rw, p1.Y - dly1 * rw, ru, 1 );
		}
		else
		{
			ChooseBevel( inner, p0, p1, -rw, out var rx0, out var ry0, out var rx1, out var ry1 );

			var a0 = MathF.Atan2( dly0, dlx0 );
			var a1 = MathF.Atan2( dly1, dlx1 );
			if ( a1 < a0 ) a1 += MathF.PI * 2;

			Put( p1.X + dlx0 * rw, p1.Y + dly0 * rw, lu, 1 );
			Put( rx0, ry0, ru, 1 );

			var n = Math.Clamp( (int)MathF.Ceiling( ( a1 - a0 ) / MathF.PI * ncap ), 2, ncap );

			for ( var i = 0; i < n; i++ )
			{
				var a = a0 + ( i / (float)( n - 1 ) ) * ( a1 - a0 );

				Put( p1.X + MathF.Cos( a ) * lw, p1.Y + MathF.Sin( a ) * lw, lu, 1 );
				Put( p1.X, p1.Y, 0.5f, 1 );
			}

			Put( p1.X + dlx1 * rw, p1.Y + dly1 * rw, lu, 1 );
			Put( rx1, ry1, ru, 1 );
		}
	}

	private static void ButtCapStart( in Pt p, float dx, float dy, float w, float d, float aa )
	{
		var px = p.X - dx * d;
		var py = p.Y - dy * d;
		var dlx = dy;
		var dly = -dx;

		Put( px + dlx * w - dx * aa, py + dly * w - dy * aa, 0, 0 );
		Put( px - dlx * w - dx * aa, py - dly * w - dy * aa, 1, 0 );
		Put( px + dlx * w, py + dly * w, 0, 1 );
		Put( px - dlx * w, py - dly * w, 1, 1 );
	}

	private static void ButtCapEnd( in Pt p, float dx, float dy, float w, float d, float aa )
	{
		var px = p.X + dx * d;
		var py = p.Y + dy * d;
		var dlx = dy;
		var dly = -dx;

		Put( px + dlx * w, py + dly * w, 0, 1 );
		Put( px - dlx * w, py - dly * w, 1, 1 );
		Put( px + dlx * w + dx * aa, py + dly * w + dy * aa, 0, 0 );
		Put( px - dlx * w + dx * aa, py - dly * w + dy * aa, 1, 0 );
	}

	private static void RoundCapStart( in Pt p, float dx, float dy, float w, int ncap )
	{
		var dlx = dy;
		var dly = -dx;

		for ( var i = 0; i < ncap; i++ )
		{
			var a = i / (float)( ncap - 1 ) * MathF.PI;
			var ax = MathF.Cos( a ) * w;
			var ay = MathF.Sin( a ) * w;

			Put( p.X - dlx * ax - dx * ay, p.Y - dly * ax - dy * ay, 0, 1 );
			Put( p.X, p.Y, 0.5f, 1 );
		}

		Put( p.X + dlx * w, p.Y + dly * w, 0, 1 );
		Put( p.X - dlx * w, p.Y - dly * w, 1, 1 );
	}

	private static void RoundCapEnd( in Pt p, float dx, float dy, float w, int ncap )
	{
		var dlx = dy;
		var dly = -dx;

		Put( p.X + dlx * w, p.Y + dly * w, 0, 1 );
		Put( p.X - dlx * w, p.Y - dly * w, 1, 1 );

		for ( var i = 0; i < ncap; i++ )
		{
			var a = i / (float)( ncap - 1 ) * MathF.PI;
			var ax = MathF.Cos( a ) * w;
			var ay = MathF.Sin( a ) * w;

			Put( p.X, p.Y, 0.5f, 1 );
			Put( p.X - dlx * ax + dx * ay, p.Y - dly * ax + dy * ay, 0, 1 );
		}
	}

	//Segments per half turn that keep a circle of this radius within the tolerance.
	private static int CurveDivs( float r, float arc, float tolerance )
	{
		var da = MathF.Acos( r / ( r + tolerance ) ) * 2.0f;
		return Math.Clamp( (int)MathF.Ceiling( arc / MathF.Max( da, 0.001f ) ), 2, 64 );
	}

	//The strip so far into triangles. across is how far the strip's own edges are from its middle, half where the drawn edge is, and aa the fringe.
	private static void EmitStrip( List<VectorVertex> into, float across, float half, float aa )
	{
		VectorVertex V( int i )
		{
			var (p, u, v) = strip[i];
			return new VectorVertex { Position = p, Across = ( u * 2.0f - 1.0f ) * across, Half = half, End = ( v - 0.5f ) * aa };
		}

		for ( var i = 0; i + 2 < strip.Count; i++ )
		{
			var a = strip[i].P;
			var b = strip[i + 1].P;
			var c = strip[i + 2].P;

			//The strip's own joints: two points in one place make a triangle of no area.
			var area = ( b.x - a.x ) * ( c.y - a.y ) - ( c.x - a.x ) * ( b.y - a.y );
			if ( MathF.Abs( area ) < 1e-7f ) continue;

			into.Add( V( i ) );
			into.Add( V( i + 1 ) );
			into.Add( V( i + 2 ) );
		}

		strip.Clear();
	}

	//-- strokes ----------------------------------------------------------------------------

	/// <summary>Every contour stroked into triangles. <paramref name="aa"/> is how many of these pixels one pixel of the screen is. Answers what the colour's alpha must be multiplied by: a line thinner than a pixel is drawn a pixel wide and fainter, as nanovg draws one.</summary>
	public static float Stroke( List<VectorContour> contours, in VectorStrokeStyle style, float aa, List<VectorVertex> into )
	{
		strip ??= new();
		strip.Clear();

		var half = style.Width * 0.5f;
		if ( !( half > 0.0f ) || contours.Count == 0 ) return 0.0f;

		var alpha = 1.0f;

		if ( half * 2.0f < aa )
		{
			alpha = Math.Clamp( half * 2.0f / aa, 0.0f, 1.0f );
			alpha *= alpha;
			half = aa * 0.5f;
		}

		var dashed = Dash( contours, style.Dashes, style.DashOffset );

		//The strip's edges lie half a fringe past the drawn edge.
		var w = half + aa * 0.5f;
		var ncap = CurveDivs( w, MathF.PI, aa * 0.25f );
		var miter = MathF.Max( style.MiterLimit, 1.0f );

		foreach ( var contour in dashed )
		{
			var closed = contour.Closed && contour.Points.Count > 2;
			var count = Prepare( contour.Points, contour.Corners, closed, false );

			if ( count == 0 ) continue;

			if ( count == 1 )
			{
				//A run of no length: SVG gives it a dot where its ends are round or square.
				Dot( pts[0], w, style.Cap, ncap, aa );
				EmitStrip( into, w, half, aa );
				continue;
			}

			CalculateJoins( count, closed, w, style.Join, miter );

			int s, e;
			int i0, i1;

			if ( closed )
			{
				i0 = count - 1;
				i1 = 0;
				s = 0;
				e = count;
			}
			else
			{
				i0 = 0;
				i1 = 1;
				s = 1;
				e = count - 1;

				var dx = pts[1].X - pts[0].X;
				var dy = pts[1].Y - pts[0].Y;
				Normalise( ref dx, ref dy );

				switch ( style.Cap )
				{
					case StrokeLinecap.Butt: ButtCapStart( pts[0], dx, dy, w, -aa * 0.5f, aa ); break;
					case StrokeLinecap.Square: ButtCapStart( pts[0], dx, dy, w, w - aa, aa ); break;
					default: RoundCapStart( pts[0], dx, dy, w, ncap ); break;
				}
			}

			var first = strip.Count;

			for ( var j = s; j < e; j++ )
			{
				ref readonly var p0 = ref pts[i0];
				ref readonly var p1 = ref pts[i1];

				if ( ( p1.Flags & ( PointFlags.Bevel | PointFlags.InnerBevel ) ) != 0 )
				{
					if ( style.Join == StrokeLinejoin.Round ) RoundJoin( p0, p1, w, w, 0, 1, ncap );
					else BevelJoin( p0, p1, w, w, 0, 1 );
				}
				else
				{
					Put( p1.X + p1.Dmx * w, p1.Y + p1.Dmy * w, 0, 1 );
					Put( p1.X - p1.Dmx * w, p1.Y - p1.Dmy * w, 1, 1 );
				}

				i0 = i1;
				i1++;
			}

			if ( closed )
			{
				//Round to where it began.
				Put( strip[first].P.x, strip[first].P.y, 0, 1 );
				Put( strip[first + 1].P.x, strip[first + 1].P.y, 1, 1 );
			}
			else
			{
				var dx = pts[i1].X - pts[i0].X;
				var dy = pts[i1].Y - pts[i0].Y;
				Normalise( ref dx, ref dy );

				switch ( style.Cap )
				{
					case StrokeLinecap.Butt: ButtCapEnd( pts[i1], dx, dy, w, -aa * 0.5f, aa ); break;
					case StrokeLinecap.Square: ButtCapEnd( pts[i1], dx, dy, w, w - aa, aa ); break;
					default: RoundCapEnd( pts[i1], dx, dy, w, ncap ); break;
				}
			}

			EmitStrip( into, w, half, aa );
		}

		return alpha;
	}

	//A zero-length run's dot: a disc for round ends, a square for square ones, nothing for butt.
	private static void Dot( in Pt p, float w, StrokeLinecap cap, int ncap, float aa )
	{
		if ( cap == StrokeLinecap.Round )
		{
			var n = ncap * 2;

			for ( var i = 0; i <= n; i++ )
			{
				var a = i / (float)n * MathF.PI * 2.0f;
				Put( p.X + MathF.Cos( a ) * w, p.Y + MathF.Sin( a ) * w, 0, 1 );
				Put( p.X, p.Y, 0.5f, 1 );
			}
		}
		else if ( cap == StrokeLinecap.Square )
		{
			var h = w - aa * 0.5f;
			ButtCapStart( p, 1, 0, w, h - aa * 0.5f, aa );
			ButtCapEnd( p, 1, 0, w, h - aa * 0.5f, aa );
		}
	}

	private static void Normalise( ref float x, ref float y )
	{
		var d = MathF.Sqrt( x * x + y * y );
		if ( d <= 1e-6f ) return;

		x /= d;
		y /= d;
	}

	//The contours cut into dashes, as open runs of their own; the contours themselves where there
	//is no pattern to cut them by. A dash keeps the corners it runs through and ends in caps.
	private static List<VectorContour> Dash( List<VectorContour> contours, float[] dashes, float offset )
	{
		if ( dashes is null || dashes.Length == 0 ) return contours;

		//An odd list is repeated to make it even, as SVG says; nothing to draw is no pattern at all.
		var pattern = dashes.Length % 2 == 0 ? dashes : [.. dashes, .. dashes];
		var total = 0.0f;

		foreach ( var d in pattern )
		{
			if ( d < 0 || !float.IsFinite( d ) ) return contours;
			total += d;
		}

		if ( total <= 1e-4f ) return contours;

		var result = new List<VectorContour>();

		foreach ( var contour in contours )
		{
			var n = contour.Points.Count;
			if ( n < 2 ) continue;

			//Where in the pattern the contour starts.
			var phase = offset % total;
			if ( phase < 0 ) phase += total;

			var index = 0;
			while ( phase >= pattern[index] )
			{
				phase -= pattern[index];
				index = ( index + 1 ) % pattern.Length;
			}

			var left = pattern[index] - phase;
			var on = index % 2 == 0;

			VectorContour dash = null;

			if ( on )
			{
				dash = new VectorContour();
				dash.Add( contour.Points[0], true );
			}

			var segments = contour.Closed ? n : n - 1;

			for ( var i = 0; i < segments; i++ )
			{
				var a = contour.Points[i];
				var b = contour.Points[( i + 1 ) % n];
				var bCorner = contour.Corners[( i + 1 ) % n];
				var length = ( b - a ).Length;
				var along = 0.0f;

				while ( length - along > left )
				{
					along += left;
					var at = a + ( b - a ) * ( along / length );

					if ( on )
					{
						//A dash of no length is kept: with round or square ends it is a dot.
						dash.Add( at, true );
						result.Add( dash );
						dash = null;
					}
					else
					{
						dash = new VectorContour();
						dash.Add( at, true );
					}

					on = !on;
					index = ( index + 1 ) % pattern.Length;
					left = pattern[index];
				}

				left -= length - along;
				dash?.Add( b, bCorner );
			}

			if ( dash is not null && dash.Points.Count > 1 ) result.Add( dash );
		}

		return result;
	}

	//-- fills ------------------------------------------------------------------------------

	[ThreadStatic] private static List<Vector2> ring;

	/// <summary>The inside of every contour, by the rule given, into triangles with a fringe round the outside. Contours that do not cross each other are exact under either rule, holes and islands in holes included; a contour that crosses itself is cut as if it did not.</summary>
	public static void Fill( List<VectorContour> contours, FillRule rule, float aa, List<VectorVertex> into )
	{
		strip ??= new();
		strip.Clear();
		ring ??= new();

		var rings = new List<(List<Vector2> Points, List<bool> Corners, float Area)>();

		foreach ( var c in contours )
		{
			if ( c.Points.Count < 3 ) continue;

			var area = SignedArea( c.Points );
			if ( MathF.Abs( area ) < 1e-6f ) continue;

			rings.Add( (c.Points, c.Corners, area) );
		}

		if ( rings.Count == 0 ) return;

		//Which rings are edges of the filled region, and on which side of them it lies.
		var role = new int[rings.Count]; //0 nothing, 1 outer edge, 2 hole edge

		for ( var r = 0; r < rings.Count; r++ )
		{
			var probe = rings[r].Points[0];
			var own = MathF.Sign( rings[r].Area );
			var outside = 0;
			var crossings = 0;

			for ( var q = 0; q < rings.Count; q++ )
			{
				if ( q == r ) continue;

				var wn = Winding( rings[q].Points, probe );
				outside += wn;
				if ( wn != 0 ) crossings++;
			}

			bool filledOut, filledIn;

			if ( rule == FillRule.EvenOdd )
			{
				filledOut = ( crossings & 1 ) != 0;
				filledIn = !filledOut;
			}
			else
			{
				filledOut = outside != 0;
				filledIn = outside + (int)own != 0;
			}

			role[r] = filledIn == filledOut ? 0 : filledIn ? 1 : 2;
		}

		//Each hole belongs to the smallest outer edge around it.
		var holes = new List<int>[rings.Count];

		for ( var h = 0; h < rings.Count; h++ )
		{
			if ( role[h] != 2 ) continue;

			var best = -1;

			for ( var o = 0; o < rings.Count; o++ )
			{
				if ( role[o] != 1 || Winding( rings[o].Points, rings[h].Points[0] ) == 0 ) continue;
				if ( best < 0 || MathF.Abs( rings[o].Area ) < MathF.Abs( rings[best].Area ) ) best = o;
			}

			if ( best < 0 ) continue;

			( holes[best] ??= new() ).Add( h );
		}

		var woff = aa * 0.5f;

		for ( var o = 0; o < rings.Count; o++ )
		{
			if ( role[o] != 1 ) continue;

			//The outer edge with the filled side on its left - which, with y running down, is a
			//negative area - and each hole the other way about, so that inward is always left.
			var outer = Inset( rings[o].Points, rings[o].Corners, rings[o].Area > 0, woff, aa, into );

			var holeRings = new List<List<Vector2>>();

			if ( holes[o] is { } hs )
			{
				foreach ( var h in hs )
					holeRings.Add( Inset( rings[h].Points, rings[h].Corners, rings[h].Area < 0, woff, aa, into ) );
			}

			Triangulate( outer, holeRings, aa, into );
		}
	}

	//A ring of the fill: its fringe emitted, and the ring pulled in by half a fringe answered,
	//for the inside to be cut from. reverse turns it so the filled side is on its left.
	private static List<Vector2> Inset( List<Vector2> points, List<bool> corners, bool reverse, float woff, float aa, List<VectorVertex> into )
	{
		var ordered = new List<Vector2>( points );
		var orderedCorners = new List<bool>( corners );

		if ( reverse )
		{
			ordered.Reverse();
			orderedCorners.Reverse();
		}

		var count = Prepare( ordered, orderedCorners, true, true );
		CalculateJoins( count, true, aa, StrokeLinejoin.Miter, 2.4f );

		var inset = new List<Vector2>( count + 8 );

		for ( int j = 0, i0 = count - 1; j < count; i0 = j, j++ )
		{
			ref readonly var p0 = ref pts[i0];
			ref readonly var p1 = ref pts[j];

			if ( ( p1.Flags & PointFlags.Bevel ) != 0 )
			{
				if ( ( p1.Flags & PointFlags.Left ) != 0 )
				{
					inset.Add( new( p1.X + p1.Dmx * woff, p1.Y + p1.Dmy * woff ) );
				}
				else
				{
					inset.Add( new( p1.X + p0.Dy * woff, p1.Y - p0.Dx * woff ) );
					inset.Add( new( p1.X + p1.Dy * woff, p1.Y - p1.Dx * woff ) );
				}
			}
			else
			{
				inset.Add( new( p1.X + p1.Dmx * woff, p1.Y + p1.Dmy * woff ) );
			}
		}

		//The fringe: from the inset ring, where the fill is whole, out to half a fringe past the edge.
		var lw = woff;
		var rw = aa - woff;
		var first = strip.Count;

		for ( int j = 0, i0 = count - 1; j < count; i0 = j, j++ )
		{
			ref readonly var p0 = ref pts[i0];
			ref readonly var p1 = ref pts[j];

			if ( ( p1.Flags & ( PointFlags.Bevel | PointFlags.InnerBevel ) ) != 0 )
			{
				BevelJoin( p0, p1, lw, rw, 0.5f, 1 );
			}
			else
			{
				Put( p1.X + p1.Dmx * lw, p1.Y + p1.Dmy * lw, 0.5f, 1 );
				Put( p1.X - p1.Dmx * rw, p1.Y - p1.Dmy * rw, 1, 1 );
			}
		}

		Put( strip[first].P.x, strip[first].P.y, 0.5f, 1 );
		Put( strip[first + 1].P.x, strip[first + 1].P.y, 1, 1 );

		EmitStrip( into, aa, woff, aa );
		return inset;
	}

	//Twice the area a ring encloses, signed by the way round it goes.
	private static float SignedArea( List<Vector2> points )
	{
		var a = 0.0f;

		for ( int i = 0, j = points.Count - 1; i < points.Count; j = i++ )
			a += points[j].x * points[i].y - points[i].x * points[j].y;

		return a * 0.5f;
	}

	//How many times a ring winds round a point, signed.
	internal static int Winding( List<Vector2> points, Vector2 p )
	{
		var wn = 0;

		for ( int i = 0, j = points.Count - 1; i < points.Count; j = i++ )
		{
			var a = points[j];
			var b = points[i];
			var side = ( b.x - a.x ) * ( p.y - a.y ) - ( p.x - a.x ) * ( b.y - a.y );

			if ( a.y <= p.y )
			{
				if ( b.y > p.y && side > 0 ) wn++;
			}
			else if ( b.y <= p.y && side < 0 )
			{
				wn--;
			}
		}

		return wn;
	}

	//A ring and its holes into triangles, by ear clipping: each hole joined to the ring
	//by a seam to a point it can see, and ears cut from the one polygon that makes.
	private static void Triangulate( List<Vector2> outer, List<List<Vector2>> holes, float aa, List<VectorVertex> into )
	{
		var polygon = new List<Vector2>( outer );

		//Holes rightmost first, as each seam is found by looking right.
		holes.Sort( ( a, b ) => MaxX( b ).CompareTo( MaxX( a ) ) );

		foreach ( var hole in holes )
		{
			if ( hole.Count < 3 ) continue;
			Bridge( polygon, hole );
		}

		var n = polygon.Count;
		if ( n < 3 ) return;

		var orientation = MathF.Sign( SignedArea( polygon ) );
		if ( orientation == 0 ) return;

		var prev = new int[n];
		var next = new int[n];

		for ( var i = 0; i < n; i++ )
		{
			prev[i] = ( i + n - 1 ) % n;
			next[i] = ( i + 1 ) % n;
		}

		var half = aa * 0.5f;

		void Emit( int a, int b, int c )
		{
			into.Add( new VectorVertex { Position = polygon[a], Across = 0, Half = half, End = half } );
			into.Add( new VectorVertex { Position = polygon[b], Across = 0, Half = half, End = half } );
			into.Add( new VectorVertex { Position = polygon[c], Across = 0, Half = half, End = half } );
		}

		var remaining = n;
		var at = 0;
		var stall = 0;

		while ( remaining > 3 )
		{
			var a = prev[at];
			var b = at;
			var c = next[at];

			if ( IsEar( polygon, prev, next, a, b, c, orientation ) || stall > remaining )
			{
				var pa = polygon[a];
				var pb = polygon[b];
				var pc = polygon[c];

				if ( MathF.Abs( ( pb.x - pa.x ) * ( pc.y - pa.y ) - ( pc.x - pa.x ) * ( pb.y - pa.y ) ) > 1e-9f )
					Emit( a, b, c );

				next[a] = c;
				prev[c] = a;
				remaining--;
				stall = 0;
				at = c;
				continue;
			}

			at = next[at];
			stall++;
		}

		Emit( prev[at], at, next[at] );
	}

	private static float MaxX( List<Vector2> points )
	{
		var m = float.MinValue;
		foreach ( var p in points ) m = MathF.Max( m, p.x );
		return m;
	}

	private static float Cross( Vector2 a, Vector2 b, Vector2 c ) => ( b.x - a.x ) * ( c.y - b.y ) - ( b.y - a.y ) * ( c.x - b.x );

	private static bool IsEar( List<Vector2> polygon, int[] prev, int[] next, int a, int b, int c, int orientation )
	{
		var pa = polygon[a];
		var pb = polygon[b];
		var pc = polygon[c];

		//Convex, the way the polygon goes round.
		if ( Cross( pa, pb, pc ) * orientation <= 0 ) return false;

		//And nothing else of the polygon inside it. A seam's two copies of a point are not inside anything.
		for ( var p = next[c]; p != a; p = next[p] )
		{
			var q = polygon[p];
			if ( q == pa || q == pb || q == pc ) continue;

			if ( Inside( pa, pb, pc, q, orientation ) ) return false;
		}

		return true;
	}

	private static bool Inside( Vector2 a, Vector2 b, Vector2 c, Vector2 p, int orientation )
	{
		var d1 = ( ( b.x - a.x ) * ( p.y - a.y ) - ( b.y - a.y ) * ( p.x - a.x ) ) * orientation;
		var d2 = ( ( c.x - b.x ) * ( p.y - b.y ) - ( c.y - b.y ) * ( p.x - b.x ) ) * orientation;
		var d3 = ( ( a.x - c.x ) * ( p.y - c.y ) - ( a.y - c.y ) * ( p.x - c.x ) ) * orientation;

		return d1 >= 0 && d2 >= 0 && d3 >= 0;
	}

	//A hole joined into the polygon by a seam from its rightmost point to a point of the polygon it can see.
	private static void Bridge( List<Vector2> polygon, List<Vector2> hole )
	{
		var m = 0;
		for ( var i = 1; i < hole.Count; i++ ) if ( hole[i].x > hole[m].x ) m = i;

		var pm = hole[m];

		//The nearest edge a ray to the right meets, and where.
		var bestX = float.MaxValue;
		var edge = -1;

		for ( int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++ )
		{
			var a = polygon[j];
			var b = polygon[i];

			if ( ( a.y > pm.y ) == ( b.y > pm.y ) ) continue;

			var x = a.x + ( pm.y - a.y ) / ( b.y - a.y ) * ( b.x - a.x );

			if ( x >= pm.x && x < bestX )
			{
				bestX = x;
				edge = a.x > b.x ? j : i;
			}
		}

		if ( edge < 0 ) return;

		//A point of the polygon inside the triangle from the hole to the hit would block the seam; the one nearest the ray's direction is visible instead.
		var target = edge;
		var hit = new Vector2( bestX, pm.y );
		var pe = polygon[edge];
		var bestAngle = float.MaxValue;

		for ( var i = 0; i < polygon.Count; i++ )
		{
			if ( i == edge ) continue;

			var q = polygon[i];
			if ( q.x < pm.x ) continue;

			var inTriangle = Inside( pm, hit, pe, q, 1 ) || Inside( pm, hit, pe, q, -1 );
			if ( !inTriangle ) continue;

			var angle = MathF.Abs( MathF.Atan2( q.y - pm.y, q.x - pm.x ) );

			if ( angle < bestAngle )
			{
				bestAngle = angle;
				target = i;
			}
		}

		var seam = new List<Vector2>( polygon.Count + hole.Count + 2 );

		for ( var i = 0; i <= target; i++ ) seam.Add( polygon[i] );
		for ( var i = 0; i <= hole.Count; i++ ) seam.Add( hole[( m + i ) % hole.Count] );
		seam.Add( polygon[target] );
		for ( var i = target + 1; i < polygon.Count; i++ ) seam.Add( polygon[i] );

		polygon.Clear();
		polygon.AddRange( seam );
	}

	//-- hit testing ------------------------------------------------------------------------

	/// <summary>Whether a point is inside the contours, closed or not, by the rule given.</summary>
	public static bool InsideFill( List<VectorContour> contours, FillRule rule, Vector2 p )
	{
		var wn = 0;
		var crossings = 0;

		foreach ( var c in contours )
		{
			if ( c.Points.Count < 3 ) continue;

			var w = Winding( c.Points, p );
			wn += w;
			crossings += Math.Abs( w );
		}

		return rule == FillRule.EvenOdd ? ( crossings & 1 ) != 0 : wn != 0;
	}

	/// <summary>Whether a point is within a distance of the line through the contours.</summary>
	public static bool NearStroke( List<VectorContour> contours, float distance, Vector2 p )
	{
		var limit = distance * distance;

		foreach ( var c in contours )
		{
			var n = c.Points.Count;
			if ( n == 0 ) continue;

			if ( n == 1 )
			{
				if ( ( p - c.Points[0] ).LengthSquared <= limit ) return true;
				continue;
			}

			var segments = c.Closed ? n : n - 1;

			for ( var i = 0; i < segments; i++ )
			{
				var a = c.Points[i];
				var b = c.Points[( i + 1 ) % n];
				var ab = b - a;
				var len2 = ab.x * ab.x + ab.y * ab.y;
				var t = len2 > 0 ? Math.Clamp( ( ( p.x - a.x ) * ab.x + ( p.y - a.y ) * ab.y ) / len2, 0.0f, 1.0f ) : 0.0f;
				var d = p - ( a + ab * t );

				if ( d.x * d.x + d.y * d.y <= limit ) return true;
			}
		}

		return false;
	}
}
