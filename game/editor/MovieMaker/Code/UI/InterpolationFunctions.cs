using System;
using Rinten.Utility;
using System.Collections.Immutable;
using Rinten;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>The shapes behind the interpolation names, so a picker can draw what it is offering.</summary>
public static class InterpolationFunctions
{
	private static ImmutableArray<float> ExampleValues { get; } =
	[
		-0.5f,
		0f,
		0.67f,
		0.33f,
		1f,
		1.5f
	];

	//A run of keyframes through a cubic, so the curve shows the overshoot the mode is chosen for.
	private static float CubicExample( float t )
	{
		const int margin = 1;

		var segments = ExampleValues.Length - 1;

		t *= segments - margin * 2;

		var index = (int)MathF.Floor( t ) + margin;

		var i1 = Math.Clamp( index, 0, segments );
		var i2 = Math.Clamp( index + 1, 0, segments );

		t -= i1 - margin;

		t = Math.Clamp( t, 0f, 1f );

		var v1 = ExampleValues[i1];
		var v2 = ExampleValues[i2];

		if ( t <= 0f ) return v1;
		if ( t >= 1f ) return v2;

		var i0 = Math.Clamp( index - 1, 0, segments );
		var i3 = Math.Clamp( index + 2, 0, segments );

		var v0 = ExampleValues[i0];
		var v3 = ExampleValues[i3];

		var t0 = (v2 - v0) * 0.5f;
		var t1 = (v3 - v1) * 0.5f;

		var c0 = v1 + t0 / 3f;
		var c1 = v2 - t1 / 3f;

		var a0 = MathX.Lerp( v1, c0, t );
		var a1 = MathX.Lerp( c0, c1, t );
		var a2 = MathX.Lerp( c1, v2, t );

		var b0 = MathX.Lerp( a0, a1, t );
		var b1 = MathX.Lerp( a1, a2, t );

		return MathX.Lerp( b0, b1, t );
	}

	public static Func<float, float>? ForKeyframe( KeyframeInterpolation interpolation ) => interpolation switch
	{
		KeyframeInterpolation.Step => MathF.Floor,
		KeyframeInterpolation.Linear => Easing.Linear,
		KeyframeInterpolation.Quadratic => Easing.QuadraticInOut,
		KeyframeInterpolation.Cubic => CubicExample,
		_ => null
	};

	public static Func<float, float> ForMode( InterpolationMode mode ) => t => mode.Apply( t );
}
