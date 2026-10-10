namespace Rinten;

/// <summary>Arithmetic typed where a number goes - <c>96*2</c>, <c>(10+5)/3</c>, <c>2^8</c>. Knows <c>+ - * / % ^</c> and brackets; the numbers in it are read the way a plain number in that field would be.</summary>
public static class NumericExpression
{
	/// <summary>The characters an expression adds to the ones a number is written with.</summary>
	public static bool IsOperatorCharacter( char c ) => c is '+' or '-' or '*' or '/' or '%' or '^' or '(' or ')' or ' ';

	/// <summary>What <paramref name="text"/> comes to, if it is a number or an expression that finishes with a finite one.</summary>
	public static bool TryEvaluate( string text, out double value )
	{
		value = 0.0;

		if ( string.IsNullOrWhiteSpace( text ) )
			return false;

		//A plain number is read exactly as it always was.
		if ( double.TryParse( text, out value ) )
			return true;

		var reader = new Reader( text );

		if ( !reader.TryExpression( out value ) )
			return false;

		reader.SkipSpaces();

		return reader.IsEnd && double.IsFinite( value );
	}

	struct Reader
	{
		readonly string text;
		int position;

		public Reader( string text )
		{
			this.text = text;
			position = 0;
		}

		public readonly bool IsEnd => position >= text.Length;

		readonly char Current => position < text.Length ? text[position] : '\0';

		public void SkipSpaces()
		{
			while ( position < text.Length && char.IsWhiteSpace( text[position] ) )
				position++;
		}

		bool Accept( char c )
		{
			SkipSpaces();

			if ( Current != c )
				return false;

			position++;
			return true;
		}

		//expression := term (('+' | '-') term)*
		public bool TryExpression( out double value )
		{
			if ( !TryTerm( out value ) )
				return false;

			while ( true )
			{
				if ( Accept( '+' ) )
				{
					if ( !TryTerm( out var right ) ) return false;
					value += right;
				}
				else if ( Accept( '-' ) )
				{
					if ( !TryTerm( out var right ) ) return false;
					value -= right;
				}
				else
				{
					return true;
				}
			}
		}

		//term := unary (('*' | '/' | '%') unary)*
		bool TryTerm( out double value )
		{
			if ( !TryUnary( out value ) )
				return false;

			while ( true )
			{
				if ( Accept( '*' ) )
				{
					if ( !TryUnary( out var right ) ) return false;
					value *= right;
				}
				else if ( Accept( '/' ) )
				{
					if ( !TryUnary( out var right ) ) return false;
					value /= right;
				}
				else if ( Accept( '%' ) )
				{
					if ( !TryUnary( out var right ) ) return false;
					value %= right;
				}
				else
				{
					return true;
				}
			}
		}

		//unary := ('-' | '+') unary | power
		bool TryUnary( out double value )
		{
			if ( Accept( '-' ) )
			{
				if ( !TryUnary( out value ) ) return false;
				value = -value;
				return true;
			}

			if ( Accept( '+' ) )
				return TryUnary( out value );

			return TryPower( out value );
		}

		//power := primary ('^' unary)? - right to left, so 2^3^2 is 2^9, and -2^2 is -4.
		bool TryPower( out double value )
		{
			if ( !TryPrimary( out value ) )
				return false;

			if ( !Accept( '^' ) )
				return true;

			if ( !TryUnary( out var exponent ) )
				return false;

			value = Math.Pow( value, exponent );
			return true;
		}

		//primary := number | '(' expression ')'
		bool TryPrimary( out double value )
		{
			if ( Accept( '(' ) )
				return TryExpression( out value ) && Accept( ')' );

			SkipSpaces();

			var start = position;
			while ( position < text.Length && (char.IsDigit( text[position] ) || text[position] is '.' or ',') )
				position++;

			value = 0.0;
			return position > start && double.TryParse( text.AsSpan( start, position - start ), out value );
		}
	}
}
