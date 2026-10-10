using System.Globalization;

namespace Rinten.UI;

/// <summary>A vector path: runs of straight lines and cubic curves, each run either left open or closed.</summary>
public sealed class VectorPath
{
	internal enum Verb : byte { Move, Line, Cubic, Close }

	internal readonly List<Verb> Verbs = new();

	//One point for a move or a line, three for a cubic - its two controls and its end - and none for a close.
	internal readonly List<Vector2> Points = new();

	private Vector2 current;
	private Vector2 start;

	/// <summary>Whether there is anything in it.</summary>
	public bool IsEmpty => Verbs.Count == 0;

	/// <summary>Where the pen is.</summary>
	public Vector2 Current => current;

	/// <summary>Empties the path, so one can be kept and filled again rather than made anew.</summary>
	public VectorPath Clear()
	{
		Verbs.Clear();
		Points.Clear();
		current = start = Vector2.Zero;
		return this;
	}

	public VectorPath MoveTo( Vector2 p )
	{
		Verbs.Add( Verb.Move );
		Points.Add( p );
		current = start = p;
		return this;
	}

	//A drawing command straight after a close, or on an empty path, starts where the last run started.
	private void EnsureStarted()
	{
		if ( Verbs.Count == 0 || Verbs[^1] == Verb.Close ) MoveTo( Verbs.Count == 0 ? current : start );
	}

	public VectorPath LineTo( Vector2 p )
	{
		EnsureStarted();
		Verbs.Add( Verb.Line );
		Points.Add( p );
		current = p;
		return this;
	}

	public VectorPath CubicTo( Vector2 c1, Vector2 c2, Vector2 p )
	{
		EnsureStarted();
		Verbs.Add( Verb.Cubic );
		Points.Add( c1 );
		Points.Add( c2 );
		Points.Add( p );
		current = p;
		return this;
	}

	/// <summary>A quadratic curve, raised to the cubic that is the same curve.</summary>
	public VectorPath QuadTo( Vector2 c, Vector2 p )
	{
		var from = current;
		return CubicTo( from + ( c - from ) * ( 2.0f / 3.0f ), p + ( c - p ) * ( 2.0f / 3.0f ), p );
	}

	/// <summary>An elliptical arc from the pen to <paramref name="p"/>, as SVG's <c>A</c> command describes one: the radii, the ellipse's turn in degrees, and which of the four arcs through the two points.</summary>
	public VectorPath ArcTo( float rx, float ry, float rotation, bool largeArc, bool sweep, Vector2 p )
	{
		var from = current;

		//The same point: no arc at all. No radius: a straight line.
		if ( from == p ) return this;

		rx = MathF.Abs( rx );
		ry = MathF.Abs( ry );

		if ( rx < 1e-6f || ry < 1e-6f ) return LineTo( p );

		//SVG 1.1, F.6.5: from the ends of the arc to its centre and angles.
		var phi = rotation * MathF.PI / 180.0f;
		var cos = MathF.Cos( phi );
		var sin = MathF.Sin( phi );

		var half = ( from - p ) * 0.5f;
		var x1 = cos * half.x + sin * half.y;
		var y1 = -sin * half.x + cos * half.y;

		//Radii too small to reach are grown until they just do.
		var lambda = ( x1 * x1 ) / ( rx * rx ) + ( y1 * y1 ) / ( ry * ry );

		if ( lambda > 1.0f )
		{
			var grow = MathF.Sqrt( lambda );
			rx *= grow;
			ry *= grow;
		}

		var num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
		var den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
		var root = den <= 0.0f ? 0.0f : MathF.Sqrt( MathF.Max( 0.0f, num / den ) );
		if ( largeArc == sweep ) root = -root;

		var cx1 = root * rx * y1 / ry;
		var cy1 = -root * ry * x1 / rx;

		var mid = ( from + p ) * 0.5f;
		var centre = new Vector2( cos * cx1 - sin * cy1 + mid.x, sin * cx1 + cos * cy1 + mid.y );

		static float Angle( float ux, float uy, float vx, float vy )
		{
			var a = MathF.Atan2( ux * vy - uy * vx, ux * vx + uy * vy );
			return a;
		}

		var theta = Angle( 1, 0, ( x1 - cx1 ) / rx, ( y1 - cy1 ) / ry );
		var delta = Angle( ( x1 - cx1 ) / rx, ( y1 - cy1 ) / ry, ( -x1 - cx1 ) / rx, ( -y1 - cy1 ) / ry );

		if ( !sweep && delta > 0 ) delta -= MathF.PI * 2.0f;
		else if ( sweep && delta < 0 ) delta += MathF.PI * 2.0f;

		//Quarter turns at most, each one cubic.
		var pieces = Math.Max( 1, (int)MathF.Ceiling( MathF.Abs( delta ) / ( MathF.PI * 0.5f ) - 0.001f ) );
		var step = delta / pieces;
		var k = 4.0f / 3.0f * MathF.Tan( step * 0.25f );

		Vector2 On( float t ) => new(
			centre.x + rx * MathF.Cos( t ) * cos - ry * MathF.Sin( t ) * sin,
			centre.y + rx * MathF.Cos( t ) * sin + ry * MathF.Sin( t ) * cos );

		Vector2 Tangent( float t ) => new(
			-rx * MathF.Sin( t ) * cos - ry * MathF.Cos( t ) * sin,
			-rx * MathF.Sin( t ) * sin + ry * MathF.Cos( t ) * cos );

		var a0 = theta;

		for ( var i = 0; i < pieces; i++ )
		{
			var a1 = a0 + step;
			var p0 = On( a0 );
			var p3 = i == pieces - 1 ? p : On( a1 );

			CubicTo( p0 + Tangent( a0 ) * k, p3 - Tangent( a1 ) * k, p3 );
			a0 = a1;
		}

		return this;
	}

	public VectorPath Close()
	{
		if ( Verbs.Count == 0 || Verbs[^1] == Verb.Close ) return this;

		Verbs.Add( Verb.Close );
		current = start;
		return this;
	}

	//-- shapes -----------------------------------------------------------------------------

	/// <summary>A rectangle, its corners rounded by <paramref name="rx"/> across and <paramref name="ry"/> down, as <c>&lt;rect&gt;</c> draws one.</summary>
	public static VectorPath Rectangle( float x, float y, float width, float height, float rx = 0, float ry = 0 )
		=> new VectorPath().AddRectangle( x, y, width, height, rx, ry );

	/// <summary><see cref="Rectangle"/>, as a run of its own added to this path.</summary>
	public VectorPath AddRectangle( float x, float y, float width, float height, float rx = 0, float ry = 0 )
	{
		var path = this;
		if ( width <= 0 || height <= 0 ) return path;

		rx = MathF.Min( MathF.Max( rx, 0 ), width * 0.5f );
		ry = MathF.Min( MathF.Max( ry, 0 ), height * 0.5f );

		if ( rx <= 0 || ry <= 0 )
		{
			return path.MoveTo( new( x, y ) ).LineTo( new( x + width, y ) ).LineTo( new( x + width, y + height ) ).LineTo( new( x, y + height ) ).Close();
		}

		path.MoveTo( new( x + rx, y ) );
		path.LineTo( new( x + width - rx, y ) );
		path.ArcTo( rx, ry, 0, false, true, new( x + width, y + ry ) );
		path.LineTo( new( x + width, y + height - ry ) );
		path.ArcTo( rx, ry, 0, false, true, new( x + width - rx, y + height ) );
		path.LineTo( new( x + rx, y + height ) );
		path.ArcTo( rx, ry, 0, false, true, new( x, y + height - ry ) );
		path.LineTo( new( x, y + ry ) );
		path.ArcTo( rx, ry, 0, false, true, new( x + rx, y ) );
		return path.Close();
	}

	/// <summary>An ellipse about a centre, or a circle where the radii are equal.</summary>
	public static VectorPath Ellipse( Vector2 centre, float rx, float ry ) => new VectorPath().AddEllipse( centre, rx, ry );

	/// <summary><see cref="Ellipse"/>, as a run of its own added to this path.</summary>
	public VectorPath AddEllipse( Vector2 centre, float rx, float ry )
	{
		var path = this;
		if ( rx <= 0 || ry <= 0 ) return path;

		path.MoveTo( new( centre.x + rx, centre.y ) );
		path.ArcTo( rx, ry, 0, false, true, new( centre.x, centre.y + ry ) );
		path.ArcTo( rx, ry, 0, false, true, new( centre.x - rx, centre.y ) );
		path.ArcTo( rx, ry, 0, false, true, new( centre.x, centre.y - ry ) );
		path.ArcTo( rx, ry, 0, false, true, new( centre.x + rx, centre.y ) );
		return path.Close();
	}

	/// <summary>Straight lines through the points, closed back to the first where asked.</summary>
	public static VectorPath Polyline( ReadOnlySpan<Vector2> points, bool closed = false )
	{
		var path = new VectorPath();
		if ( points.Length == 0 ) return path;

		path.MoveTo( points[0] );
		for ( var i = 1; i < points.Length; i++ ) path.LineTo( points[i] );

		return closed ? path.Close() : path;
	}

	//-- reading ----------------------------------------------------------------------------

	/// <summary>Path data, as <c>&lt;path d&gt;</c> writes it: M, L, H, V, C, S, Q, T, A and Z in either case, a command repeated by giving it more numbers, numbers run together wherever the next sign or point says where one ends. What comes before a mistake is kept, as a browser keeps it.</summary>
	public static VectorPath Parse( string data )
	{
		var path = new VectorPath();
		if ( string.IsNullOrWhiteSpace( data ) ) return path;

		var at = 0;
		var command = '\0';
		var lastControl = Vector2.Zero;
		var lastKind = '\0';

		Span<float> n = stackalloc float[7];

		while ( true )
		{
			SkipSeparators( data, ref at );
			if ( at >= data.Length ) break;

			var c = data[at];

			if ( char.IsLetter( c ) && c != 'e' && c != 'E' )
			{
				command = c;
				at++;

				if ( command is 'z' or 'Z' )
				{
					path.Close();
					lastKind = 'Z';
					continue;
				}
			}
			else if ( command == '\0' || command is 'z' or 'Z' )
			{
				//Numbers with no command to take them.
				break;
			}

			var relative = char.IsLower( command );
			var origin = relative ? path.current : Vector2.Zero;
			var upper = char.ToUpperInvariant( command );

			var needed = upper switch
			{
				'M' or 'L' or 'T' => 2,
				'H' or 'V' => 1,
				'C' => 6,
				'S' or 'Q' => 4,
				'A' => 7,
				_ => -1,
			};

			if ( needed < 0 ) break;

			var read = true;

			for ( var i = 0; i < needed && read; i++ )
			{
				SkipSeparators( data, ref at );

				//An arc's two flags are one digit each and need nothing between them.
				if ( upper == 'A' && i is 3 or 4 )
				{
					if ( at < data.Length && data[at] is '0' or '1' )
					{
						n[i] = data[at] - '0';
						at++;
					}
					else read = false;

					continue;
				}

				read = TryReadNumber( data, ref at, out n[i] );
			}

			if ( !read ) break;

			switch ( upper )
			{
				case 'M':
					path.MoveTo( origin + new Vector2( n[0], n[1] ) );

					//Numbers after a move are lines, in the same case as the move.
					command = relative ? 'l' : 'L';
					break;

				case 'L':
					path.LineTo( origin + new Vector2( n[0], n[1] ) );
					break;

				case 'H':
					path.LineTo( new Vector2( relative ? path.current.x + n[0] : n[0], path.current.y ) );
					break;

				case 'V':
					path.LineTo( new Vector2( path.current.x, relative ? path.current.y + n[0] : n[0] ) );
					break;

				case 'C':
				{
					var c2 = origin + new Vector2( n[2], n[3] );
					path.CubicTo( origin + new Vector2( n[0], n[1] ), c2, origin + new Vector2( n[4], n[5] ) );
					lastControl = c2;
					break;
				}

				case 'S':
				{
					var c1 = lastKind is 'C' or 'S' ? path.current * 2.0f - lastControl : path.current;
					var c2 = origin + new Vector2( n[0], n[1] );
					path.CubicTo( c1, c2, origin + new Vector2( n[2], n[3] ) );
					lastControl = c2;
					break;
				}

				case 'Q':
				{
					var q = origin + new Vector2( n[0], n[1] );
					path.QuadTo( q, origin + new Vector2( n[2], n[3] ) );
					lastControl = q;
					break;
				}

				case 'T':
				{
					var q = lastKind is 'Q' or 'T' ? path.current * 2.0f - lastControl : path.current;
					path.QuadTo( q, origin + new Vector2( n[0], n[1] ) );
					lastControl = q;
					break;
				}

				case 'A':
					path.ArcTo( n[0], n[1], n[2], n[3] != 0, n[4] != 0, origin + new Vector2( n[5], n[6] ) );
					break;
			}

			lastKind = upper;
		}

		return path;
	}

	/// <summary>The points of <c>&lt;polyline points&gt;</c> and <c>&lt;polygon points&gt;</c>; an odd number out at the end is dropped.</summary>
	public static List<Vector2> ParsePoints( string data )
	{
		var points = new List<Vector2>();
		if ( string.IsNullOrWhiteSpace( data ) ) return points;

		var at = 0;

		while ( true )
		{
			SkipSeparators( data, ref at );
			if ( !TryReadNumber( data, ref at, out var x ) ) break;

			SkipSeparators( data, ref at );
			if ( !TryReadNumber( data, ref at, out var y ) ) break;

			points.Add( new Vector2( x, y ) );
		}

		return points;
	}

	/// <summary>A list of numbers, by spaces or commas: a view box, a dash array.</summary>
	public static List<float> ParseNumbers( string data )
	{
		var numbers = new List<float>();
		if ( string.IsNullOrWhiteSpace( data ) ) return numbers;

		var at = 0;

		while ( true )
		{
			SkipSeparators( data, ref at );
			if ( !TryReadNumber( data, ref at, out var f ) ) break;

			numbers.Add( f );

			//A unit after a length is taken as pixels, which is what a user unit is.
			while ( at < data.Length && ( char.IsLetter( data[at] ) || data[at] == '%' ) ) at++;
		}

		return numbers;
	}

	private static void SkipSeparators( string data, ref int at )
	{
		while ( at < data.Length && ( char.IsWhiteSpace( data[at] ) || data[at] == ',' ) ) at++;
	}

	//One number as SVG writes it: a sign, digits, a point, more digits, an exponent - ended by anything that cannot continue it.
	internal static bool TryReadNumber( string data, ref int at, out float value )
	{
		value = 0;
		var begin = at;
		var i = at;

		if ( i < data.Length && data[i] is '+' or '-' ) i++;

		var digits = 0;
		while ( i < data.Length && char.IsAsciiDigit( data[i] ) ) { i++; digits++; }

		if ( i < data.Length && data[i] == '.' )
		{
			i++;
			while ( i < data.Length && char.IsAsciiDigit( data[i] ) ) { i++; digits++; }
		}

		if ( digits == 0 ) return false;

		if ( i < data.Length && data[i] is 'e' or 'E' )
		{
			var e = i + 1;
			if ( e < data.Length && data[e] is '+' or '-' ) e++;

			if ( e < data.Length && char.IsAsciiDigit( data[e] ) )
			{
				while ( e < data.Length && char.IsAsciiDigit( data[e] ) ) e++;
				i = e;
			}
		}

		if ( !float.TryParse( data.AsSpan( begin, i - begin ), NumberStyles.Float, CultureInfo.InvariantCulture, out value ) ) return false;
		if ( !float.IsFinite( value ) ) return false;

		at = i;
		return true;
	}
}
