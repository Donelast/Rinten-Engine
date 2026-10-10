using System.Globalization;

namespace Rinten.UI;

/// <summary>2D affine transform in SVG matrix(a b c d e f) form, for shapes' own and view box transforms</summary>
public readonly record struct VectorTransform( float A, float B, float C, float D, float E, float F )
{
	/// <summary>Leaves every point where it is.</summary>
	public static readonly VectorTransform Identity = new( 1, 0, 0, 1, 0, 0 );

	/// <summary>Whether this leaves every point where it is.</summary>
	public bool IsIdentity => this == Identity;

	/// <summary>Moves every point by the same amount.</summary>
	public static VectorTransform Translation( float x, float y ) => new( 1, 0, 0, 1, x, y );

	/// <summary>Stretches about the origin.</summary>
	public static VectorTransform Scaling( float x, float y ) => new( x, 0, 0, y, 0, 0 );

	/// <summary>Turns about the origin, by degrees, clockwise on a screen whose y runs down.</summary>
	public static VectorTransform Rotation( float degrees )
	{
		var r = degrees * MathF.PI / 180.0f;
		var c = MathF.Cos( r );
		var s = MathF.Sin( r );
		return new( c, s, -s, c, 0, 0 );
	}

	/// <summary>Where a point goes.</summary>
	public Vector2 Apply( Vector2 p ) => new( A * p.x + C * p.y + E, B * p.x + D * p.y + F );

	/// <summary>Where a direction goes: the same, without the move.</summary>
	public Vector2 ApplyVector( Vector2 v ) => new( A * v.x + C * v.y, B * v.x + D * v.y );

	/// <summary>This, then <paramref name="then"/>: a point goes through this transform first.</summary>
	public VectorTransform Then( in VectorTransform then ) => new(
		then.A * A + then.C * B,
		then.B * A + then.D * B,
		then.A * C + then.C * D,
		then.B * C + then.D * D,
		then.A * E + then.C * F + then.E,
		then.B * E + then.D * F + then.F );

	/// <summary>How much a length grows through this, on average over its directions: the square root of the area it scales by. What a stroke's width is scaled by, and what a curve is flattened for.</summary>
	public float Scale => MathF.Sqrt( MathF.Abs( A * D - B * C ) );

	/// <summary>The inverse, or identity for a transform that flattens everything onto a line.</summary>
	public VectorTransform Inverted
	{
		get
		{
			var det = A * D - B * C;
			if ( MathF.Abs( det ) < 1e-12f ) return Identity;

			var i = 1.0f / det;
			return new( D * i, -B * i, -C * i, A * i, ( C * F - D * E ) * i, ( B * E - A * F ) * i );
		}
	}

	/// <summary>An SVG transform list - <c>translate( 10 20 ) rotate( 45 ) scale( 2 )</c> and the rest, applied right to left as SVG applies them. Identity for nothing, or for something it cannot read.</summary>
	public static VectorTransform Parse( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return Identity;

		var result = Identity;
		var at = 0;
		Span<float> args = stackalloc float[6];

		while ( true )
		{
			while ( at < text.Length && ( char.IsWhiteSpace( text[at] ) || text[at] == ',' ) ) at++;
			if ( at >= text.Length ) break;

			var nameStart = at;
			while ( at < text.Length && char.IsLetter( text[at] ) ) at++;
			var name = text.AsSpan( nameStart, at - nameStart );

			while ( at < text.Length && char.IsWhiteSpace( text[at] ) ) at++;
			if ( at >= text.Length || text[at] != '(' ) return Identity;
			at++;

			var count = 0;

			while ( true )
			{
				while ( at < text.Length && ( char.IsWhiteSpace( text[at] ) || text[at] == ',' ) ) at++;
				if ( at >= text.Length ) return Identity;
				if ( text[at] == ')' ) { at++; break; }
				if ( count >= 6 || !VectorPath.TryReadNumber( text, ref at, out args[count] ) ) return Identity;
				count++;
			}

			VectorTransform step;

			switch ( name )
			{
				case "matrix" when count == 6:
					step = new( args[0], args[1], args[2], args[3], args[4], args[5] );
					break;

				case "translate" when count is 1 or 2:
					step = Translation( args[0], count == 2 ? args[1] : 0 );
					break;

				case "scale" when count is 1 or 2:
					step = Scaling( args[0], count == 2 ? args[1] : args[0] );
					break;

				case "rotate" when count == 1:
					step = Rotation( args[0] );
					break;

				case "rotate" when count == 3:
					step = Translation( -args[1], -args[2] ).Then( Rotation( args[0] ) ).Then( Translation( args[1], args[2] ) );
					break;

				case "skewX" when count == 1:
					step = new( 1, 0, MathF.Tan( args[0] * MathF.PI / 180.0f ), 1, 0, 0 );
					break;

				case "skewY" when count == 1:
					step = new( 1, MathF.Tan( args[0] * MathF.PI / 180.0f ), 0, 1, 0, 0 );
					break;

				default:
					return Identity;
			}

			//Written left to right, applied right to left: the step nearest the shape goes first.
			result = step.Then( result );
		}

		return result;
	}

	public override string ToString() => string.Create( CultureInfo.InvariantCulture, $"matrix({A} {B} {C} {D} {E} {F})" );
}
