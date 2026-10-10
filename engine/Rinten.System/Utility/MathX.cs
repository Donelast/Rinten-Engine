using System.Runtime.CompilerServices;

namespace Rinten;

/// <summary>A class to add functionality to the math library that System.Math and System.MathF don't provide. A lot of these methods are also extensions, so you can use for example `int i = 1.0f.FloorToInt();`</summary>
public static partial class MathX
{
	internal const float toRadians = (float)Math.PI * 2F / 360F;
	internal const float toDegrees = 1.0f / toRadians;
	internal const float toGradiansDegrees = 0.9f;
	internal const float toGradiansRadians = 0.01570796326f;

	/// <summary>Convert degrees to radians. 180 degrees is <see cref="Math.PI"/> (roughly 3.14) radians, etc.</summary>
	public static float DegreeToRadian( this float deg ) => deg * toRadians;

	/// <summary>Convert radians to degrees. 180 degrees is <see cref="Math.PI"/> (roughly 3.14) radians, etc.</summary>
	public static float RadianToDegree( this float rad ) => rad * toDegrees;

	/// <summary>Convert gradians to degrees. 100 gradian is 90 degrees, 200 gradian is 180 degrees, etc.</summary>
	public static float GradiansToDegrees( this float grad ) => grad * toGradiansDegrees;

	/// <summary>Convert gradians to radians. 200 gradian is <see cref="Math.PI"/> (roughly 3.14) radians, etc.</summary>
	public static float GradiansToRadians( this float grad ) => grad * toGradiansRadians;

	//The inch conversions that were here are gone, and so is the constant they were written against. A unit is a metre; the only
	//thing that ever needs to know what an inch is worth is the port that reads somebody else's content, and it keeps that to itself.

	/// <summary>Snap number to grid</summary>
	public static float SnapToGrid( this float f, float gridSize )
	{
		if ( gridSize.AlmostEqual( 0.0f ) ) return f;
		var snapped = MathF.Round( f / gridSize ) * gridSize;
		return snapped == 0.0f ? 0.0f : snapped;
	}

	/// <summary>Snap number to grid</summary>
	public static int SnapToGrid( this int f, int gridSize )
	{
		return (f / gridSize) * gridSize;
	}

	/// <summary>Remove the fractional part and return the float as an integer.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static int FloorToInt( this float f )
	{
		return (int)MathF.Floor( f );
	}

	/// <summary>Remove the fractional part of given floating point number</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float Floor( this float f )
	{
		return MathF.Floor( f );
	}

	/// <summary>Rounds up given float to next integer value.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static int CeilToInt( this float f )
	{
		return (int)MathF.Ceiling( f );
	}

	//Orders the two given numbers so that a is less than b.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static void Order( ref float a, ref float b )
	{
		if ( a <= b ) return;

		(b, a) = (a, b);
	}

	/// <summary>Clamp a float between 2 given extremes. If given value is lower than the given minimum value, returns the minimum value, etc.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float Clamp( this float v, float min, float max )
	{
		Order( ref min, ref max );

		return v < min ? min : v < max ? v : max;
	}

	/// <summary>Performs linear interpolation on floating point numbers.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float Lerp( float from, float to, float frac, bool clamp = true )
	{
		if ( clamp ) frac = frac.Clamp( 0, 1 );
		return (from + frac * (to - from));
	}

	/// <summary>Performs linear interpolation on floating point numbers.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static double Lerp( double from, double to, double frac, bool clamp = true )
	{
		if ( clamp ) frac = frac.Clamp( 0, 1 );
		return (from + frac * (to - from));
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float LerpTo( this float from, float to, float frac, bool clamp = true )
	{
		if ( clamp ) frac = frac.Clamp( 0, 1 );
		return (from + frac * (to - from));
	}

	/// <summary>Performs multiple linear interpolations at the same time.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float[] LerpTo( this float[] from, float[] to, float delta, bool clamp = true )
	{
		//TODO: Throw on bogus input?
		if ( from == null ) return null;
		if ( to == null ) return from;

		float[] output = new float[Math.Min( from.Length, to.Length )];

		for ( int i = 0; i < output.Length; i++ )
		{
			output[i] = from[i].LerpTo( to[i], delta, clamp );
		}

		return output;
	}

	/// <summary>Linearly interpolates between two angles in degrees, taking the shortest arc.</summary>
	public static float LerpDegrees( float from, float to, float frac, bool clamp = true )
	{
		var delta = DeltaDegrees( from, to );
		var lerped = from.LerpTo( from + delta, frac, clamp ).UnsignedMod( 360f );
		return lerped >= 180f ? lerped - 360f : lerped;
	}

	public static float LerpDegreesTo( this float from, float to, float frac, bool clamp = true )
	{
		return LerpDegrees( from, to, frac, clamp );
	}

	/// <summary>Linearly interpolates between two angles in radians, taking the shortest arc.</summary>
	public static float LerpRadians( float from, float to, float frac, bool clamp = true )
	{
		var delta = DeltaRadians( from, to );
		var lerped = from.LerpTo( from + delta, frac, clamp ).UnsignedMod( MathF.Tau );
		return lerped >= MathF.PI ? lerped - MathF.Tau : lerped;
	}

	public static float LerpRadiansTo( this float from, float to, float frac, bool clamp = true )
	{
		return LerpRadians( from, to, frac, clamp );
	}

	/// <summary>Performs inverse of a linear interpolation, that is, the return value is the fraction of a linear interpolation.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float LerpInverse( this float value, float from, float to, bool clamp = true )
	{
		if ( clamp ) value = value.Clamp( from, to );

		value -= from;
		to -= from;

		if ( to == 0 ) return 0;

		return value / to;
	}

	/// <summary>Adds or subtracts given amount based on whether the input is smaller of bigger than the target.</summary>
	public static float Approach( this float f, float target, float delta )
	{
		if ( f > target )
		{
			f -= delta;
			if ( f < target ) return target;
		}
		else
		{
			f += delta;
			if ( f > target ) return target;
		}

		return f;
	}

	/// <summary>Returns true if given value is close to given value within given tolerance.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool AlmostEqual( this float value, float b, float within = 0.0001f )
	{
		return MathF.Abs( value - b ) <= within;
	}

	/// <summary>Does what you expected to happen when you did "a % b"</summary>
	public static float UnsignedMod( this float a, float b )
	{
		return a - b * (a / b).Floor();
	}

	/// <summary>Convert angle to between 0 - 360</summary>
	public static float NormalizeDegrees( this float degree )
	{
		degree = degree % 360;
		if ( degree < 0 ) degree += 360;

		return degree;
	}

	/// <summary>Difference between two angles in degrees. Will always be between -180 and +180.</summary>
	public static float DeltaDegrees( float from, float to )
	{
		var delta = (to - from).UnsignedMod( 360f );
		return delta >= 180f ? delta - 360f : delta;
	}

	/// <summary>Difference between two angles in radians. Will always be between -PI and +PI.</summary>
	public static float DeltaRadians( float from, float to )
	{
		var delta = (to - from).UnsignedMod( MathF.Tau );
		return delta >= MathF.PI ? delta - MathF.Tau : delta;
	}

	/// <summary>Remap a float value from a one range to another. Clamps value between newLow and newHigh.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float Remap( this float value, float oldLow, float oldHigh, float newLow = 0, float newHigh = 1 )
	{
		return Remap( value, oldLow, oldHigh, newLow, newHigh, true );
	}

	/// <summary>Remap a double value from one range to another. Clamps value between newLow and newHigh.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static double Remap( this double value, double oldLow, double oldHigh, double newLow = 0, double newHigh = 1 )
	{
		return Remap( value, oldLow, oldHigh, newLow, newHigh, true );
	}

	/// <summary>Remap a float value from a one range to another</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float Remap( this float value, float oldLow, float oldHigh, float newLow, float newHigh, bool clamp )
	{
		if ( MathF.Abs( oldHigh - oldLow ) < 0.0001f )
			return clamp ? newLow : value;

		var v = newLow + (value - oldLow) * (newHigh - newLow) / (oldHigh - oldLow);

		if ( clamp )
			v = v.Clamp( newLow, newHigh );

		return v;
	}

	/// <summary>Remap a double value from a one range to another</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static double Remap( this double value, double oldLow, double oldHigh, double newLow, double newHigh, bool clamp )
	{
		if ( Math.Abs( oldHigh - oldLow ) < 0.0001 )
			return clamp ? newLow : value;

		var v = newLow + (value - oldLow) * (newHigh - newLow) / (oldHigh - oldLow);

		if ( clamp )
			v = v.Clamp( newLow, newHigh );

		return v;
	}

	/// <summary>Remap an integer value from a one range to another</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static int Remap( this int value, int oldLow, int oldHigh, int newLow, int newHigh )
	{
		return (int)Remap( (float)value, (float)oldLow, (float)oldHigh, (float)newLow, (float)newHigh, true );
	}


	/// <summary>Given a sphere and a field of view, how far from the camera should we be to fully see the sphere?</summary>
	public static float SphereCameraDistance( float radius, float fieldOfView )
	{
		if ( radius < 0.001f )
			return 0.01f;

		if ( fieldOfView <= 0.01f )
			return 0.01f;

		return radius / MathF.Abs( MathF.Sin( fieldOfView.DegreeToRadian() * 0.5f ) );
	}

	/// <summary>Smoothly approach the target value using exponential decay. Cheaper than SmoothDamp but doesn't track velocity for momentum. Good for non-physical smoothing.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float ExponentialDecay( float current, float target, float halflife, float deltaTime )
	{
		//log(0.5) == -0.69314718f
		return target + (current - target) * MathF.Exp( -0.69314718f / halflife * deltaTime );
	}

	/// <summary>Smoothly move towards the target</summary>
	public static float SmoothDamp( float current, float target, ref float velocity, float smoothTime, float deltaTime )
	{
		var displacement = current - target;

		(displacement, velocity) = SpringDamper.FromSmoothingTime( smoothTime )
			.Simulate( displacement, velocity, deltaTime );

		return displacement + target;
	}

	/// <summary>Smoothly move towards the target using a spring-like motion</summary>
	public static float SpringDamp( float current, float target, ref float velocity, float deltaTime, float frequency = 2.0f, float damping = 0.5f )
	{
		var displacement = current - target;

		(displacement, velocity) = SpringDamper.FromDamping( frequency, damping )
			.Simulate( displacement, velocity, deltaTime );

		return displacement + target;
	}

	//Finds the real solutions to a quadratic equation of the form Ax² + Bx + C = 0 . Useful for determining where a parabolic curve intersects the x-axis.
	internal static List<float> SolveQuadratic( float a, float b, float c )
	{
		if ( MathF.Abs( a ).AlmostEqual( 0.0f ) )
		{
			//First coefficient is zero, so this is at most linear
			if ( MathF.Abs( b ).AlmostEqual( 0.0f ) )
			{
				//Second coefficient is also zero
				return new List<float>();
			}

			//Linear Bx + C = 0 and B != 0.
			return new List<float> { -c / b };
		}

		//normal form: Ax^2 + Bx + C = 0
		return QuadraticRoots( b / a, c / a );
	}

	//Finds the real solutions to a cubic equation of the form Ax³ + Bx² + Cx + D = 0 . Useful for finding where a cubic curve crosses the x-axis.
	internal static List<float> SolveCubic( float a, float b, float c, float d )
	{
		if ( MathF.Abs( a ).AlmostEqual( 0.0f ) )
		{
			//Leading coefficient is zero, so this is at most quadratic
			var quadraticRoots = SolveQuadratic( b, c, d );
			return quadraticRoots;
		}

		//normal form: x^3 + Ax^2 + Bx + C = 0
		return CubicRoots( b / a, c / a, d / a );
	}

	//Calculates the real roots of a simplified quadratic equation in its normal form:
	//x² + Ax + B = 0 . This is a helper method used internally by SolveQuadratic.
	private static List<float> QuadraticRoots( float a, float b )
	{
		float discriminant = 0.25f * a * a - b;
		if ( discriminant >= 0.0f )
		{
			var sqrtDiscriminant = MathF.Sqrt( discriminant );
			var r0 = -0.5f * a - sqrtDiscriminant;
			var r1 = -0.5f * a + sqrtDiscriminant;

			if ( r0.AlmostEqual( r1 ) )
			{
				return new List<float> { r0 };
			}

			return new List<float> { r0, r1 };
		}

		return new List<float>();
	}

	//Calculates the real roots of a simplified cubic equation in its normal form:
	//x³ + Ax² + Bx + C = 0 . This is a helper method used internally by SolveCubic.
	private static List<float> CubicRoots( float a, float b, float c )
	{
		/*  substitute x = y - A/3 to eliminate quadric term: x^3 +px + q = 0 */
		float squareA = a * a;
		float p = (1.0f / 3.0f) * (-1.0f / 3.0f * squareA + b);
		float q = (1.0f / 2.0f) * (2.0f / 27.0f * a * squareA - (1.0f / 3.0f) * a * b + c);
		float cubicP = p * p * p;
		float squareQ = q * q;
		float discriminant = squareQ + cubicP;

		float sub = 1.0f / 3 * a;

		if ( MathF.Abs( discriminant ).AlmostEqual( 0.0f ) )
		{

			if ( MathF.Abs( q ).AlmostEqual( 0.0f ) )
			{
				//One real root.
				return new List<float> { 0.0f - sub };
			}
			else
			{
				//One single and one double root.
				float U = MathF.Cbrt( -q );
				return new List<float> { 2.0f * U - sub, -U - sub };
			}
		}
		else if ( discriminant < 0 )
		{
			//Casus irreducibilis: three real solutions
			float phi = 1.0f / 3 * MathF.Acos( -q / MathF.Sqrt( -cubicP ) );
			float t = 2.0f * MathF.Sqrt( -p );

			return new List<float>
			{
				t * MathF.Cos( phi ) - sub,
				-t * MathF.Cos( phi + MathF.PI / 3  ) - sub,
				-t * MathF.Cos( phi - MathF.PI / 3  ) - sub
			};
		}
		else
		{
			//One real solution
			float sqrtDicriminant = MathF.Sqrt( discriminant );
			float s = MathF.Cbrt( sqrtDicriminant - q );
			float t = -MathF.Cbrt( sqrtDicriminant + q );

			return new List<float> { s + t - sub };
		}
	}
}
