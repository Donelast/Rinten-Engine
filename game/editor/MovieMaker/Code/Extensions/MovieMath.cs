using Rinten;

namespace Editor.MovieMaker;

#nullable enable

//Odds and ends the movie editor does arithmetic with.
internal static class MovieMath
{
	public static int NextPowerOfTwo( this float value )
	{
		var po2 = 1;

		while ( po2 < value )
		{
			po2 <<= 1;
		}

		return po2;
	}

	public static int NearestPowerOfTwo( this float value ) =>
		NextPowerOfTwo( value * 0.75f );

	/// <summary>Tints <paramref name="baseColor"/> in the same way that <paramref name="tintedColor"/> was tinted related to <paramref name="defaultColor"/>.</summary>
	public static Color TintRelative( this Color baseColor, Color defaultColor, Color tintedColor )
	{
		var diff = tintedColor - defaultColor;

		return baseColor + diff;
	}
}

/// <summary>A value that eases towards whatever it is asked for.</summary>
public sealed class SmoothDeltaFloat
{
	private float _value;
	private float _velocity;

	public float SmoothTime { get; }

	public float Value
	{
		get => _value;
		set
		{
			_value = value;
			Target = value;
			_velocity = 0f;
		}
	}

	public float Velocity => _velocity;

	public float Target { get; set; }

	public SmoothDeltaFloat( float smoothTime, float initial = 0f )
	{
		SmoothTime = smoothTime;
		Value = Target = initial;
	}

	public bool Update( float delta )
	{
		if ( Math.Abs( _value - Target ) <= 0.001f )
		{
			var exact = _value.Equals( Target );

			_value = Target;
			return !exact;
		}

		_value = MathX.SmoothDamp( _value, Target, ref _velocity, SmoothTime, delta );
		return true;
	}
}
