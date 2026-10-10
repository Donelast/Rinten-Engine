using System.Globalization;

namespace Rinten;

//Evaluates typed arithmetic (+ - * / %, brackets, sign) invariantly; replaces DataTable.Compute
internal static class ArithmeticExpression
{
	/// <summary>Works out <paramref name="text"/>; false where it is not arithmetic or divides by nought.</summary>
	public static bool TryEvaluate( string text, out double value )
	{
		value = 0;
		if ( string.IsNullOrWhiteSpace( text ) ) return false;

		var at = 0;
		if ( !Sum( text, ref at, out value ) ) return false;

		Skip( text, ref at );
		return at == text.Length && double.IsFinite( value );
	}

	private static bool Sum( string text, ref int at, out double value )
	{
		if ( !Product( text, ref at, out value ) ) return false;

		while ( true )
		{
			Skip( text, ref at );
			if ( at >= text.Length || ( text[at] != '+' && text[at] != '-' ) ) return true;

			var op = text[at++];
			if ( !Product( text, ref at, out var right ) ) return false;

			value = op == '+' ? value + right : value - right;
		}
	}

	private static bool Product( string text, ref int at, out double value )
	{
		if ( !Unary( text, ref at, out value ) ) return false;

		while ( true )
		{
			Skip( text, ref at );
			if ( at >= text.Length || ( text[at] != '*' && text[at] != '/' && text[at] != '%' ) ) return true;

			var op = text[at++];
			if ( !Unary( text, ref at, out var right ) ) return false;

			if ( op != '*' && right == 0 ) return false;

			value = op switch
			{
				'*' => value * right,
				'/' => value / right,
				_ => value % right,
			};
		}
	}

	private static bool Unary( string text, ref int at, out double value )
	{
		Skip( text, ref at );

		if ( at < text.Length && ( text[at] == '-' || text[at] == '+' ) )
		{
			var negative = text[at++] == '-';
			if ( !Unary( text, ref at, out value ) ) return false;
			if ( negative ) value = -value;
			return true;
		}

		return Atom( text, ref at, out value );
	}

	private static bool Atom( string text, ref int at, out double value )
	{
		value = 0;
		Skip( text, ref at );
		if ( at >= text.Length ) return false;

		if ( text[at] == '(' )
		{
			at++;
			if ( !Sum( text, ref at, out value ) ) return false;

			Skip( text, ref at );
			if ( at >= text.Length || text[at] != ')' ) return false;

			at++;
			return true;
		}

		//A number: digits and a point, then an exponent with its own sign.
		var start = at;

		while ( at < text.Length && ( char.IsAsciiDigit( text[at] ) || text[at] == '.' ) ) at++;

		if ( at > start && at < text.Length && ( text[at] == 'e' || text[at] == 'E' ) )
		{
			var exponent = at + 1;
			if ( exponent < text.Length && ( text[exponent] == '+' || text[exponent] == '-' ) ) exponent++;

			if ( exponent < text.Length && char.IsAsciiDigit( text[exponent] ) )
			{
				at = exponent;
				while ( at < text.Length && char.IsAsciiDigit( text[at] ) ) at++;
			}
		}

		return at > start && double.TryParse( text.AsSpan( start, at - start ), NumberStyles.Float, CultureInfo.InvariantCulture, out value );
	}

	private static void Skip( string text, ref int at )
	{
		while ( at < text.Length && char.IsWhiteSpace( text[at] ) ) at++;
	}
}
