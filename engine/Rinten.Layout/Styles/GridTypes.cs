using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Rinten.Layout;

//What one side of a track's sizing function is.
internal enum TrackBreadthKind : byte
{
	Auto,
	Points,
	Percent,
	Fraction,
	MinContent,
	MaxContent,
}

//One side of a track's sizing function: a length, a percentage of the grid container's inner size, a
//share of the free space ( fr ), or one of the content keywords. CSS Grid Layout Level 1, section 7.2.3.
internal readonly struct TrackBreadth : IEquatable<TrackBreadth>
{
	public readonly TrackBreadthKind Kind;
	public readonly float Value;

	private TrackBreadth( TrackBreadthKind kind, float value )
	{
		Kind = kind;
		Value = value;
	}

	public static TrackBreadth Auto => new( TrackBreadthKind.Auto, 0 );
	public static TrackBreadth MinContent => new( TrackBreadthKind.MinContent, 0 );
	public static TrackBreadth MaxContent => new( TrackBreadthKind.MaxContent, 0 );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static TrackBreadth Points( float value ) => new( TrackBreadthKind.Points, value );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static TrackBreadth Percent( float value ) => new( TrackBreadthKind.Percent, value );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static TrackBreadth Fraction( float value ) => new( TrackBreadthKind.Fraction, value );

	public bool IsFixed => Kind is TrackBreadthKind.Points or TrackBreadthKind.Percent;
	public bool IsIntrinsic => Kind is TrackBreadthKind.Auto or TrackBreadthKind.MinContent or TrackBreadthKind.MaxContent;
	public bool IsFlexible => Kind == TrackBreadthKind.Fraction;

	/// <summary>A length in pixels where the breadth is one; undefined where it is not, or is a percentage of nothing.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public float Resolve( float reference ) => Kind switch
	{
		TrackBreadthKind.Points => Value,
		TrackBreadthKind.Percent => Num.IsDefined( reference ) ? Value / 100.0f * reference : Num.Undefined,
		_ => Num.Undefined,
	};

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public bool Equals( TrackBreadth other ) => Kind == other.Kind && ( Value == other.Value || ( float.IsNaN( Value ) && float.IsNaN( other.Value ) ) );
	public override bool Equals( object obj ) => obj is TrackBreadth other && Equals( other );
	public override int GetHashCode() => HashCode.Combine( Kind, Value );

	public override string ToString() => Kind switch
	{
		TrackBreadthKind.Points => Value.ToString( "0.###", CultureInfo.InvariantCulture ) + "px",
		TrackBreadthKind.Percent => Value.ToString( "0.###", CultureInfo.InvariantCulture ) + "%",
		TrackBreadthKind.Fraction => Value.ToString( "0.###", CultureInfo.InvariantCulture ) + "fr",
		TrackBreadthKind.MinContent => "min-content",
		TrackBreadthKind.MaxContent => "max-content",
		_ => "auto",
	};
}

//A track's sizing function: a minimum and a maximum, as minmax() writes them.
internal readonly struct GridTrackSize : IEquatable<GridTrackSize>
{
	public readonly TrackBreadth Min;
	public readonly TrackBreadth Max;

	public GridTrackSize( TrackBreadth min, TrackBreadth max )
	{
		//A flexible minimum is not allowed; the specification has it read as auto.
		Min = min.IsFlexible ? TrackBreadth.Auto : min;
		Max = max;
	}

	/// <summary>A single breadth: fixed and intrinsic values are both sides at once; a flexible value is <c>minmax(auto, fr)</c>.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static GridTrackSize Of( TrackBreadth breadth )
		=> breadth.IsFlexible ? new GridTrackSize( TrackBreadth.Auto, breadth ) : new GridTrackSize( breadth, breadth );

	public static GridTrackSize AutoTrack => Of( TrackBreadth.Auto );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public bool Equals( GridTrackSize other ) => Min.Equals( other.Min ) && Max.Equals( other.Max );
	public override bool Equals( object obj ) => obj is GridTrackSize other && Equals( other );
	public override int GetHashCode() => HashCode.Combine( Min, Max );
	public override string ToString() => Min.Equals( Max ) ? Min.ToString() : $"minmax({Min}, {Max})";
}

//What a repeat() with auto-fill or auto-fit asks for.
internal enum GridRepeat : byte
{
	None,
	AutoFill,
	AutoFit,
}

//How automatically placed items fill the grid: CSS Grid Layout Level 1, section 8.5.
internal enum GridAutoFlow : byte
{
	Row,
	Column,
	RowDense,
	ColumnDense,
}

//A list of track sizing functions, as grid-template-columns and its kin hold them: the tracks before an automatic repeat,
//the repeated tracks, and the tracks after. Immutable and shared: a string parses once, however many styles carry it.
internal sealed class GridTrackList : IEquatable<GridTrackList>
{
	public static readonly GridTrackList None = new( Array.Empty<GridTrackSize>(), null, Array.Empty<GridTrackSize>(), GridRepeat.None );
	public static readonly GridTrackList SingleAuto = new( new[] { GridTrackSize.AutoTrack }, null, Array.Empty<GridTrackSize>(), GridRepeat.None );

	public readonly GridTrackSize[] Leading;
	public readonly GridTrackSize[] Repeated;
	public readonly GridTrackSize[] Trailing;
	public readonly GridRepeat Repeat;

	public GridTrackList( GridTrackSize[] leading, GridTrackSize[] repeated, GridTrackSize[] trailing, GridRepeat repeat )
	{
		Leading = leading;
		Repeated = repeated is { Length: > 0 } ? repeated : null;
		Trailing = trailing;
		Repeat = Repeated is null ? GridRepeat.None : repeat;
	}

	public bool IsEmpty => Leading.Length == 0 && Repeated is null && Trailing.Length == 0;

	/// <summary>The tracks the list names outright, not counting an automatic repeat.</summary>
	public int FixedCount => Leading.Length + Trailing.Length;

	public bool Equals( GridTrackList other )
	{
		if ( other is null ) return false;
		if ( ReferenceEquals( this, other ) ) return true;
		if ( Repeat != other.Repeat ) return false;
		return Same( Leading, other.Leading ) && Same( Repeated, other.Repeated ) && Same( Trailing, other.Trailing );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool Same( GridTrackSize[] a, GridTrackSize[] b )
	{
		if ( a is null || b is null ) return a is null && b is null;
		if ( a.Length != b.Length ) return false;
		for ( var i = 0; i < a.Length; i++ ) if ( !a[i].Equals( b[i] ) ) return false;
		return true;
	}

	public override bool Equals( object obj ) => obj is GridTrackList other && Equals( other );

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add( Repeat );
		foreach ( var t in Leading ) hash.Add( t );
		if ( Repeated is not null ) foreach ( var t in Repeated ) hash.Add( t );
		foreach ( var t in Trailing ) hash.Add( t );
		return hash.ToHashCode();
	}

	private static readonly ConcurrentDictionary<string, GridTrackList> parsed = new( StringComparer.Ordinal );

	/// <summary>Reads a track list: lengths, percentages, <c>fr</c>, <c>auto</c>, <c>min-content</c>, <c>max-content</c>, <c>minmax()</c>, <c>fit-content()</c> (read as <c>minmax(auto, limit)</c>), and <c>repeat()</c> with a count, <c>auto-fill</c> or <c>auto-fit</c>. Line names in brackets are skipped. Anything unreadable yields <see cref="None"/>. Answers are kept per string.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static GridTrackList Parse( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return None;
		return parsed.GetOrAdd( text, static t => ParseUncached( t ) );
	}

	private static GridTrackList ParseUncached( string text )
	{
		var trimmed = text.Trim();
		if ( trimmed == "none" ) return None;

		var leading = new List<GridTrackSize>();
		var trailing = new List<GridTrackSize>();
		GridTrackSize[] repeated = null;
		var repeat = GridRepeat.None;
		var into = leading;

		var i = 0;
		while ( i < trimmed.Length )
		{
			if ( char.IsWhiteSpace( trimmed[i] ) ) { i++; continue; }

			if ( trimmed[i] == '[' )
			{
				var close = trimmed.IndexOf( ']', i );
				if ( close < 0 ) return None;
				i = close + 1;
				continue;
			}

			var token = ReadToken( trimmed, ref i );
			if ( token.Length == 0 ) return None;

			if ( token.StartsWith( "repeat(", StringComparison.Ordinal ) )
			{
				var inner = token.Substring( 7, token.Length - 8 );
				var comma = inner.IndexOf( ',' );
				if ( comma < 0 ) return None;

				var count = inner.Substring( 0, comma ).Trim();
				var tracks = ParseSimpleList( inner.Substring( comma + 1 ) );
				if ( tracks is null ) return None;

				if ( count == "auto-fill" || count == "auto-fit" )
				{
					if ( repeated is not null ) return None;
					repeated = tracks;
					repeat = count == "auto-fill" ? GridRepeat.AutoFill : GridRepeat.AutoFit;
					into = trailing;
				}
				else
				{
					if ( !int.TryParse( count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n ) || n < 1 ) return None;
					for ( var k = 0; k < n; k++ ) into.AddRange( tracks );
				}

				continue;
			}

			if ( !TryParseTrack( token, out var size ) ) return None;
			into.Add( size );
		}

		if ( leading.Count == 0 && repeated is null && trailing.Count == 0 ) return None;
		return new GridTrackList( leading.ToArray(), repeated, trailing.ToArray(), repeat );
	}

	private static GridTrackSize[] ParseSimpleList( string text )
	{
		var list = new List<GridTrackSize>();
		var i = 0;

		while ( i < text.Length )
		{
			if ( char.IsWhiteSpace( text[i] ) ) { i++; continue; }

			if ( text[i] == '[' )
			{
				var close = text.IndexOf( ']', i );
				if ( close < 0 ) return null;
				i = close + 1;
				continue;
			}

			var token = ReadToken( text, ref i );
			if ( !TryParseTrack( token, out var size ) ) return null;
			list.Add( size );
		}

		return list.Count == 0 ? null : list.ToArray();
	}

	//One word, or one function with everything inside its brackets.
	private static string ReadToken( string text, ref int i )
	{
		var start = i;
		var depth = 0;

		while ( i < text.Length )
		{
			var c = text[i];
			if ( c == '(' ) depth++;
			else if ( c == ')' ) depth--;
			else if ( depth == 0 && ( char.IsWhiteSpace( c ) || c == '[' ) ) break;
			i++;
		}

		return text.Substring( start, i - start );
	}

	private static bool TryParseTrack( string token, out GridTrackSize size )
	{
		size = GridTrackSize.AutoTrack;

		if ( token.StartsWith( "minmax(", StringComparison.Ordinal ) && token.EndsWith( ")", StringComparison.Ordinal ) )
		{
			var inner = token.Substring( 7, token.Length - 8 );
			var comma = inner.IndexOf( ',' );
			if ( comma < 0 ) return false;
			if ( !TryParseBreadth( inner.Substring( 0, comma ).Trim(), out var min ) ) return false;
			if ( !TryParseBreadth( inner.Substring( comma + 1 ).Trim(), out var max ) ) return false;
			size = new GridTrackSize( min, max );
			return true;
		}

		if ( token.StartsWith( "fit-content(", StringComparison.Ordinal ) && token.EndsWith( ")", StringComparison.Ordinal ) )
		{
			if ( !TryParseBreadth( token.Substring( 12, token.Length - 13 ).Trim(), out var limit ) ) return false;
			size = new GridTrackSize( TrackBreadth.Auto, limit );
			return true;
		}

		if ( !TryParseBreadth( token, out var breadth ) ) return false;
		size = GridTrackSize.Of( breadth );
		return true;
	}

	private static bool TryParseBreadth( string token, out TrackBreadth breadth )
	{
		breadth = TrackBreadth.Auto;

		switch ( token )
		{
			case "auto": return true;
			case "min-content": breadth = TrackBreadth.MinContent; return true;
			case "max-content": breadth = TrackBreadth.MaxContent; return true;
		}

		if ( token.EndsWith( "fr", StringComparison.Ordinal ) && TryNumber( token.AsSpan( 0, token.Length - 2 ), out var fr ) && fr >= 0 )
		{
			breadth = TrackBreadth.Fraction( fr );
			return true;
		}

		if ( token.EndsWith( "%", StringComparison.Ordinal ) && TryNumber( token.AsSpan( 0, token.Length - 1 ), out var percent ) )
		{
			breadth = TrackBreadth.Percent( percent );
			return true;
		}

		if ( token.EndsWith( "px", StringComparison.Ordinal ) && TryNumber( token.AsSpan( 0, token.Length - 2 ), out var pixels ) )
		{
			breadth = TrackBreadth.Points( pixels );
			return true;
		}

		if ( TryNumber( token.AsSpan(), out var bare ) && bare == 0 )
		{
			breadth = TrackBreadth.Points( 0 );
			return true;
		}

		return false;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool TryNumber( ReadOnlySpan<char> text, out float value )
		=> float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out value );

	public override string ToString()
	{
		var parts = new List<string>();
		foreach ( var t in Leading ) parts.Add( t.ToString() );
		if ( Repeated is not null ) parts.Add( $"repeat({( Repeat == GridRepeat.AutoFill ? "auto-fill" : "auto-fit" )}, {string.Join( " ", Repeated )})" );
		foreach ( var t in Trailing ) parts.Add( t.ToString() );
		return parts.Count == 0 ? "none" : string.Join( " ", parts );
	}
}

internal enum GridLineKind : byte
{
	Auto,
	Line,
	Span,
}

//One end of a grid item's placement in one axis: automatic, a line number (negative counts
//from the end of the explicit grid), or a span. CSS Grid Layout Level 1, section 8.3.
internal readonly struct GridLine : IEquatable<GridLine>
{
	public readonly GridLineKind Kind;
	public readonly int Value;

	private GridLine( GridLineKind kind, int value )
	{
		Kind = kind;
		Value = value;
	}

	public static GridLine Auto => new( GridLineKind.Auto, 0 );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static GridLine Line( int number ) => number == 0 ? Auto : new( GridLineKind.Line, number );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static GridLine Span( int count ) => count < 1 ? Auto : new( GridLineKind.Span, count );

	public bool IsAuto => Kind == GridLineKind.Auto;
	public bool IsLine => Kind == GridLineKind.Line;
	public bool IsSpan => Kind == GridLineKind.Span;

	/// <summary>Reads <c>auto</c>, a line number, or <c>span n</c>; a name yields auto, as names are not kept.</summary>
	public static GridLine Parse( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return Auto;
		var t = text.Trim();
		if ( t == "auto" ) return Auto;

		if ( t.StartsWith( "span", StringComparison.Ordinal ) )
		{
			var rest = t.Substring( 4 ).Trim();
			return int.TryParse( rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n ) ? Span( n ) : Span( 1 );
		}

		return int.TryParse( t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line ) ? Line( line ) : Auto;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public bool Equals( GridLine other ) => Kind == other.Kind && Value == other.Value;
	public override bool Equals( object obj ) => obj is GridLine other && Equals( other );
	public override int GetHashCode() => HashCode.Combine( Kind, Value );
	public static bool operator ==( GridLine a, GridLine b ) => a.Equals( b );
	public static bool operator !=( GridLine a, GridLine b ) => !a.Equals( b );

	public override string ToString() => Kind switch
	{
		GridLineKind.Line => Value.ToString( CultureInfo.InvariantCulture ),
		GridLineKind.Span => "span " + Value.ToString( CultureInfo.InvariantCulture ),
		_ => "auto",
	};
}

//The grid properties of a style, kept apart so a style that never uses them pays one reference for the lot.
internal sealed class GridStyleValues
{
	public GridTrackList TemplateColumns = GridTrackList.None;
	public GridTrackList TemplateRows = GridTrackList.None;
	public GridTrackList AutoColumns = GridTrackList.SingleAuto;
	public GridTrackList AutoRows = GridTrackList.SingleAuto;
	public GridAutoFlow AutoFlow = GridAutoFlow.Row;
	public GridLine ColumnStart = GridLine.Auto;
	public GridLine ColumnEnd = GridLine.Auto;
	public GridLine RowStart = GridLine.Auto;
	public GridLine RowEnd = GridLine.Auto;
	public Align JustifyItems = Align.Stretch;
	public Align JustifySelf = Align.Auto;

	private static readonly GridStyleValues Defaults = new();

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public GridStyleValues Clone() => (GridStyleValues)MemberwiseClone();

	/// <summary>Whether two records say the same, a missing one saying the defaults.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool Same( GridStyleValues a, GridStyleValues b )
	{
		a ??= Defaults;
		b ??= Defaults;
		return a.AutoFlow == b.AutoFlow && a.ColumnStart == b.ColumnStart && a.ColumnEnd == b.ColumnEnd
			&& a.RowStart == b.RowStart && a.RowEnd == b.RowEnd && a.JustifyItems == b.JustifyItems && a.JustifySelf == b.JustifySelf
			&& a.TemplateColumns.Equals( b.TemplateColumns ) && a.TemplateRows.Equals( b.TemplateRows )
			&& a.AutoColumns.Equals( b.AutoColumns ) && a.AutoRows.Equals( b.AutoRows );
	}
}
