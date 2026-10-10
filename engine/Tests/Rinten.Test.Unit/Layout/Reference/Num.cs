//Frozen test-only copy of the layout engine (Yoga port, MIT) for differential tests; do not edit
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Rinten.Layout.Reference;

//Undefined is NaN; helpers cover the cases where plain float operators give the wrong answer
internal static class Num
{
	//What an absent number is.
	public const float Undefined = float.NaN;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool IsUndefined( float value ) => float.IsNaN( value );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool IsDefined( float value ) => !float.IsNaN( value );

	//The larger of two, or whichever one is there.
	public static float MaxOrDefined( float a, float b )
	{
		if ( IsDefined( a ) && IsDefined( b ) ) return a > b ? a : b;

		return IsUndefined( a ) ? b : a;
	}

	//The smaller of two, or whichever one is there.
	public static float MinOrDefined( float a, float b )
	{
		if ( IsDefined( a ) && IsDefined( b ) ) return a < b ? a : b;

		return IsUndefined( a ) ? b : a;
	}

	//Equal, counting two undefined as equal - which == does not.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool EqualsOptional( float a, float b )
		=> a == b || ( IsUndefined( a ) && IsUndefined( b ) );

	//Equal to a ten-thousandth, or both undefined. The tolerance is the library's own and the caches are built on it.
	public static bool InexactEquals( float a, float b )
	{
		if ( IsDefined( a ) && IsDefined( b ) ) return MathF.Abs( a - b ) < 0.0001f;

		return IsUndefined( a ) && IsUndefined( b );
	}

	//The value, or a fallback where there is none.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float OrElse( float value, float fallback ) => IsUndefined( value ) ? fallback : value;
}

//A CSS length: a number and what the number means, or nothing at all. Used for margin, padding,
//border, position and gap - everything but the sizes, which may also carry keywords and use SizeLength.
internal readonly struct StyleLength : IEquatable<StyleLength>
{
	private readonly float value;
	private readonly Unit unit;

	private StyleLength( float value, Unit unit )
	{
		this.value = value;
		this.unit = unit;
	}

	public static StyleLength Undefined => default;

	//An infinity is refused rather than stored: it is not a length.
	public static StyleLength Points( float value )
		=> Num.IsUndefined( value ) || float.IsInfinity( value ) ? Undefined : new StyleLength( value, Unit.Point );

	public static StyleLength Percent( float value )
		=> Num.IsUndefined( value ) || float.IsInfinity( value ) ? Undefined : new StyleLength( value, Unit.Percent );

	public static StyleLength Auto => new( Num.Undefined, Unit.Auto );

	public bool IsAuto => unit == Unit.Auto;
	public bool IsUndefined => unit == Unit.Undefined;
	public bool IsDefined => unit != Unit.Undefined;
	public bool IsPoints => unit == Unit.Point;
	public bool IsPercent => unit == Unit.Percent;

	public override string ToString() => unit switch
	{
		Unit.Point => value.ToString( System.Globalization.CultureInfo.InvariantCulture ) + "px",
		Unit.Percent => value.ToString( System.Globalization.CultureInfo.InvariantCulture ) + "%",
		_ => unit.ToString().ToLowerInvariant(),
	};

	public float Value => value;

	//The length in points, given what a percentage is a percentage of.
	public float Resolve( float reference ) => unit switch
	{
		Unit.Point => value,
		Unit.Percent => value * reference * 0.01f,
		_ => Num.Undefined,
	};

	public bool Equals( StyleLength other ) => unit == other.unit && Num.EqualsOptional( value, other.value );

	public override bool Equals( object obj ) => obj is StyleLength other && Equals( other );

	public override int GetHashCode() => HashCode.Combine( value, unit );

	public static bool operator ==( StyleLength a, StyleLength b ) => a.Equals( b );
	public static bool operator !=( StyleLength a, StyleLength b ) => !a.Equals( b );
}

//The same for a size: width, height and their minima and maxima, which may also be a keyword. The keywords past auto - max-content , fit-content
//, stretch - are here because the algorithm asks about them; nothing in the engine sets one, since the seam only offers points, percent and auto.
internal readonly struct SizeLength : IEquatable<SizeLength>
{
	private readonly float value;
	private readonly Unit unit;

	private SizeLength( float value, Unit unit )
	{
		this.value = value;
		this.unit = unit;
	}

	public static SizeLength Undefined => default;

	public static SizeLength Points( float value )
		=> Num.IsUndefined( value ) || float.IsInfinity( value ) ? Undefined : new SizeLength( value, Unit.Point );

	public static SizeLength Percent( float value )
		=> Num.IsUndefined( value ) || float.IsInfinity( value ) ? Undefined : new SizeLength( value, Unit.Percent );

	public static SizeLength Auto => new( Num.Undefined, Unit.Auto );
	public static SizeLength MaxContent => new( Num.Undefined, Unit.MaxContent );
	public static SizeLength FitContent => new( Num.Undefined, Unit.FitContent );
	public static SizeLength Stretch => new( Num.Undefined, Unit.Stretch );

	public bool IsAuto => unit == Unit.Auto;
	public bool IsMaxContent => unit == Unit.MaxContent;
	public bool IsFitContent => unit == Unit.FitContent;
	public bool IsStretch => unit == Unit.Stretch;
	public bool IsUndefined => unit == Unit.Undefined;
	public bool IsDefined => unit != Unit.Undefined;
	public bool IsPoints => unit == Unit.Point;
	public bool IsPercent => unit == Unit.Percent;

	public override string ToString() => unit switch
	{
		Unit.Point => value.ToString( System.Globalization.CultureInfo.InvariantCulture ) + "px",
		Unit.Percent => value.ToString( System.Globalization.CultureInfo.InvariantCulture ) + "%",
		_ => unit.ToString().ToLowerInvariant(),
	};

	public float Value => value;

	public float Resolve( float reference ) => unit switch
	{
		Unit.Point => value,
		Unit.Percent => value * reference * 0.01f,
		_ => Num.Undefined,
	};

	public bool Equals( SizeLength other ) => unit == other.unit && Num.EqualsOptional( value, other.value );

	public override bool Equals( object obj ) => obj is SizeLength other && Equals( other );

	public override int GetHashCode() => HashCode.Combine( value, unit );

	public static bool operator ==( SizeLength a, SizeLength b ) => a.Equals( b );
	public static bool operator !=( SizeLength a, SizeLength b ) => !a.Equals( b );
}
