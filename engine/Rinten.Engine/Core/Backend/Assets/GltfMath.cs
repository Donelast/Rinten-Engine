namespace Rinten;

//The matrix arithmetic the glTF importers lean on, column-major like the file, ported with them from the core. glTF's basis is the engine's
//- Y-up, -Z forward, right-handed - so a file arrives in the space it will be drawn in and there is no change of basis anywhere in here.
internal static class GltfMath
{
	/// <summary>Position, rotation and scale, in glTF's own space.</summary>
	public sealed class Trs
	{
		public readonly float[] Translation = { 0.0f, 0.0f, 0.0f };
		public readonly float[] Rotation = { 0.0f, 0.0f, 0.0f, 1.0f };
		public readonly float[] Scale = { 1.0f, 1.0f, 1.0f };
	}

	public static void Identity( Span<float> m )
	{
		m.Clear();
		m[0] = m[5] = m[10] = m[15] = 1.0f;
	}

	/// <summary>A point out of the file.</summary>
	public static Vector3 Point( ReadOnlySpan<float> v ) => new( v[0], v[1], v[2] );

	/// <summary>A 4x4 column-major matrix times a point.</summary>
	public static void TransformPoint( ReadOnlySpan<float> m, ReadOnlySpan<float> @in, Span<float> @out )
	{
		@out[0] = ( m[0] * @in[0] ) + ( m[4] * @in[1] ) + ( m[8] * @in[2] ) + m[12];
		@out[1] = ( m[1] * @in[0] ) + ( m[5] * @in[1] ) + ( m[9] * @in[2] ) + m[13];
		@out[2] = ( m[2] * @in[0] ) + ( m[6] * @in[1] ) + ( m[10] * @in[2] ) + m[14];
	}

	/// <summary>The same, for a direction - no translation.</summary>
	public static void TransformDirection( ReadOnlySpan<float> m, ReadOnlySpan<float> @in, Span<float> @out )
	{
		@out[0] = ( m[0] * @in[0] ) + ( m[4] * @in[1] ) + ( m[8] * @in[2] );
		@out[1] = ( m[1] * @in[0] ) + ( m[5] * @in[1] ) + ( m[9] * @in[2] );
		@out[2] = ( m[2] * @in[0] ) + ( m[6] * @in[1] ) + ( m[10] * @in[2] );
	}

	public static void Normalise( Span<float> v )
	{
		var length = MathF.Sqrt( ( v[0] * v[0] ) + ( v[1] * v[1] ) + ( v[2] * v[2] ) );
		if ( length < 1e-8f ) return;

		v[0] /= length;
		v[1] /= length;
		v[2] /= length;
	}

	/// <summary>A glTF transform as a row-major 4x4, with the translation scaled.</summary>
	public static void ToRowMajor( ReadOnlySpan<float> column, float scale, Span<float> outRowMajor )
	{
		for ( var row = 0; row < 3; ++row )
		{
			outRowMajor[( row * 4 ) + 0] = column[( row * 4 ) + 0];
			outRowMajor[( row * 4 ) + 1] = column[( row * 4 ) + 1];
			outRowMajor[( row * 4 ) + 2] = column[( row * 4 ) + 2];
			outRowMajor[( row * 4 ) + 3] = 0.0f;
		}

		outRowMajor[12] = column[12] * scale;
		outRowMajor[13] = column[13] * scale;
		outRowMajor[14] = column[14] * scale;
		outRowMajor[15] = 1.0f;
	}

	/// <summary>A rotation out of the file, xyzw.</summary>
	public static void Quat( ReadOnlySpan<float> xyzw, Span<float> @out )
	{
		@out[0] = xyzw[0];
		@out[1] = xyzw[1];
		@out[2] = xyzw[2];
		@out[3] = xyzw[3];
	}

	/// <summary>A scale out of the file. A mirrored rig stays mirrored.</summary>
	public static void Scale( ReadOnlySpan<float> s, Span<float> @out )
	{
		@out[0] = s[0];
		@out[1] = s[1];
		@out[2] = s[2];
	}

	/// <summary>TRS into a column-major matrix, like everything the file holds.</summary>
	public static void Compose( Trs trs, Span<float> @out )
	{
		float x = trs.Rotation[0], y = trs.Rotation[1], z = trs.Rotation[2], w = trs.Rotation[3];

		ReadOnlySpan<float> r = stackalloc float[9]
		{
			1.0f - ( 2.0f * ( ( y * y ) + ( z * z ) ) ), 2.0f * ( ( x * y ) + ( z * w ) ), 2.0f * ( ( x * z ) - ( y * w ) ),
			2.0f * ( ( x * y ) - ( z * w ) ), 1.0f - ( 2.0f * ( ( x * x ) + ( z * z ) ) ), 2.0f * ( ( y * z ) + ( x * w ) ),
			2.0f * ( ( x * z ) + ( y * w ) ), 2.0f * ( ( y * z ) - ( x * w ) ), 1.0f - ( 2.0f * ( ( x * x ) + ( y * y ) ) ),
		};

		for ( var column = 0; column < 3; ++column )
			for ( var row = 0; row < 3; ++row )
				@out[( column * 4 ) + row] = r[( column * 3 ) + row] * trs.Scale[column];

		@out[3] = @out[7] = @out[11] = 0.0f;
		@out[12] = trs.Translation[0];
		@out[13] = trs.Translation[1];
		@out[14] = trs.Translation[2];
		@out[15] = 1.0f;
	}

	/// <summary>a × b, both column-major.</summary>
	public static void Multiply( ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> @out )
	{
		for ( var column = 0; column < 4; ++column )
		{
			for ( var row = 0; row < 4; ++row )
			{
				var sum = 0.0f;
				for ( var k = 0; k < 4; ++k ) sum += a[( k * 4 ) + row] * b[( column * 4 ) + k];

				@out[( column * 4 ) + row] = sum;
			}
		}
	}

	/// <summary>The inverse of a transform matrix - rotation, scale and translation, no projection. False when it has been squashed flat and has no inverse.</summary>
	public static bool Invert( ReadOnlySpan<float> m, Span<float> @out )
	{
		float a = m[0], b = m[4], c = m[8];
		float d = m[1], e = m[5], f = m[9];
		float g = m[2], h = m[6], i = m[10];

		var determinant = ( a * ( ( e * i ) - ( f * h ) ) ) - ( b * ( ( d * i ) - ( f * g ) ) ) + ( c * ( ( d * h ) - ( e * g ) ) );
		if ( MathF.Abs( determinant ) < 1e-12f ) return false;

		var inverse = 1.0f / determinant;

		ReadOnlySpan<float> r = stackalloc float[9]
		{
			( ( e * i ) - ( f * h ) ) * inverse, ( ( f * g ) - ( d * i ) ) * inverse, ( ( d * h ) - ( e * g ) ) * inverse,
			( ( c * h ) - ( b * i ) ) * inverse, ( ( a * i ) - ( c * g ) ) * inverse, ( ( b * g ) - ( a * h ) ) * inverse,
			( ( b * f ) - ( c * e ) ) * inverse, ( ( c * d ) - ( a * f ) ) * inverse, ( ( a * e ) - ( b * d ) ) * inverse,
		};

		//r holds the inverse column by column already - the adjugate above is written out transposed - so it copies straight across.
		@out[0] = r[0]; @out[1] = r[1]; @out[2] = r[2]; @out[3] = 0.0f;
		@out[4] = r[3]; @out[5] = r[4]; @out[6] = r[5]; @out[7] = 0.0f;
		@out[8] = r[6]; @out[9] = r[7]; @out[10] = r[8]; @out[11] = 0.0f;

		@out[12] = -( ( @out[0] * m[12] ) + ( @out[4] * m[13] ) + ( @out[8] * m[14] ) );
		@out[13] = -( ( @out[1] * m[12] ) + ( @out[5] * m[13] ) + ( @out[9] * m[14] ) );
		@out[14] = -( ( @out[2] * m[12] ) + ( @out[6] * m[13] ) + ( @out[10] * m[14] ) );
		@out[15] = 1.0f;

		return true;
	}

	/// <summary>A transform matrix back into position, rotation and scale. Shear is not recovered - a matrix that has any is not a pose, and the nearest rotation is a better answer than refusing.</summary>
	public static Trs Decompose( ReadOnlySpan<float> m )
	{
		var trs = new Trs();

		trs.Translation[0] = m[12];
		trs.Translation[1] = m[13];
		trs.Translation[2] = m[14];

		Span<float> column = stackalloc float[9];

		for ( var c = 0; c < 3; ++c )
			for ( var r = 0; r < 3; ++r )
				column[( c * 3 ) + r] = m[( c * 4 ) + r];

		for ( var c = 0; c < 3; ++c )
		{
			trs.Scale[c] = MathF.Sqrt(
				( column[c * 3] * column[c * 3] ) +
				( column[( c * 3 ) + 1] * column[( c * 3 ) + 1] ) +
				( column[( c * 3 ) + 2] * column[( c * 3 ) + 2] ) );
		}

		//A negative determinant means the transform mirrors. One axis has to carry that, and the first is as good
		//as any - the alternative is a rotation matrix that is not one, and a quaternion built from it is nonsense.
		var determinant =
			( column[0] * ( ( column[4] * column[8] ) - ( column[5] * column[7] ) ) ) -
			( column[3] * ( ( column[1] * column[8] ) - ( column[2] * column[7] ) ) ) +
			( column[6] * ( ( column[1] * column[5] ) - ( column[2] * column[4] ) ) );

		if ( determinant < 0.0f ) trs.Scale[0] = -trs.Scale[0];

		Span<float> r9 = stackalloc float[9];

		for ( var c = 0; c < 3; ++c )
		{
			var length = trs.Scale[c];
			var divisor = MathF.Abs( length ) < 1e-8f ? 1.0f : length;

			for ( var row = 0; row < 3; ++row ) r9[( c * 3 ) + row] = column[( c * 3 ) + row] / divisor;
		}

		//Shepperd: take the largest of the four, so nothing divides by a vanishing root.
		float m00 = r9[0], m10 = r9[1], m20 = r9[2];
		float m01 = r9[3], m11 = r9[4], m21 = r9[5];
		float m02 = r9[6], m12 = r9[7], m22 = r9[8];

		var trace = m00 + m11 + m22;

		if ( trace > 0.0f )
		{
			var s = MathF.Sqrt( trace + 1.0f ) * 2.0f;
			trs.Rotation[3] = 0.25f * s;
			trs.Rotation[0] = ( m21 - m12 ) / s;
			trs.Rotation[1] = ( m02 - m20 ) / s;
			trs.Rotation[2] = ( m10 - m01 ) / s;
		}
		else if ( m00 > m11 && m00 > m22 )
		{
			var s = MathF.Sqrt( 1.0f + m00 - m11 - m22 ) * 2.0f;
			trs.Rotation[3] = ( m21 - m12 ) / s;
			trs.Rotation[0] = 0.25f * s;
			trs.Rotation[1] = ( m01 + m10 ) / s;
			trs.Rotation[2] = ( m02 + m20 ) / s;
		}
		else if ( m11 > m22 )
		{
			var s = MathF.Sqrt( 1.0f + m11 - m00 - m22 ) * 2.0f;
			trs.Rotation[3] = ( m02 - m20 ) / s;
			trs.Rotation[0] = ( m01 + m10 ) / s;
			trs.Rotation[1] = 0.25f * s;
			trs.Rotation[2] = ( m12 + m21 ) / s;
		}
		else
		{
			var s = MathF.Sqrt( 1.0f + m22 - m00 - m11 ) * 2.0f;
			trs.Rotation[3] = ( m10 - m01 ) / s;
			trs.Rotation[0] = ( m02 + m20 ) / s;
			trs.Rotation[1] = ( m12 + m21 ) / s;
			trs.Rotation[2] = 0.25f * s;
		}

		return trs;
	}
}
