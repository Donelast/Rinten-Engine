using System.Numerics;
using System.Runtime.CompilerServices;

namespace Rinten.CoreAnimation;

//One bone's position, rotation and scale; poses stay as TRS for blending. Row vectors (v * M)
internal struct AnimTrs
{
	public Vector3 Position;
	public Quaternion Rotation;
	public Vector3 Scale;

	/// <summary>Where nothing has moved. Used everywhere C++ said <c>Trs{}</c> - the struct default would carry a zero scale, which flattens whatever it touches.</summary>
	public static readonly AnimTrs Identity = new()
	{
		Position = Vector3.Zero,
		Rotation = Quaternion.Identity,
		Scale = Vector3.One,
	};

	/// <summary>One transform after another: the result of applying this and then <paramref name="b"/>.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static AnimTrs Then( in AnimTrs a, in AnimTrs b )
	{
		AnimTrs result;

		result.Position = (Rotation)b.Rotation * (a.Position * b.Scale) + b.Position;
		result.Rotation = b.Rotation * a.Rotation;
		result.Scale = a.Scale * b.Scale;

		return result;
	}

	/// <summary>Additive per channel: offsets added, rotations composed after base, scales multiplied</summary>
	public static AnimTrs Beside( in AnimTrs a, in AnimTrs b )
	{
		AnimTrs result;

		result.Position = a.Position + b.Position;
		result.Rotation = a.Rotation * b.Rotation;
		result.Scale = a.Scale * b.Scale;

		return result;
	}

	public static AnimTrs Invert( in AnimTrs t )
	{
		var rotation = Quaternion.Conjugate( t.Rotation );

		var scale = new Vector3(
			MathF.Abs( t.Scale.x ) < 1e-8f ? 0.0f : 1.0f / t.Scale.x,
			MathF.Abs( t.Scale.y ) < 1e-8f ? 0.0f : 1.0f / t.Scale.y,
			MathF.Abs( t.Scale.z ) < 1e-8f ? 0.0f : 1.0f / t.Scale.z );

		AnimTrs result;
		result.Rotation = rotation;
		result.Scale = scale;
		result.Position = (Rotation)rotation * (-t.Position * scale);

		return result;
	}

	/// <summary>Part of the way from one transform to another.</summary>
	public static AnimTrs Blend( in AnimTrs a, in AnimTrs b, float weight )
	{
		if ( weight <= 0.0f ) return a;
		if ( weight >= 1.0f ) return b;

		AnimTrs result;

		result.Position = Vector3.Lerp( a.Position, b.Position, weight );
		result.Scale = Vector3.Lerp( a.Scale, b.Scale, weight );

		var dot = Quaternion.Dot( a.Rotation, b.Rotation );
		var towards = dot < 0.0f ? -b.Rotation : b.Rotation;

		result.Rotation = NormaliseQuat( new Quaternion(
			a.Rotation.X + ( ( towards.X - a.Rotation.X ) * weight ),
			a.Rotation.Y + ( ( towards.Y - a.Rotation.Y ) * weight ),
			a.Rotation.Z + ( ( towards.Z - a.Rotation.Z ) * weight ),
			a.Rotation.W + ( ( towards.W - a.Rotation.W ) * weight ) ) );

		return result;
	}

	public static Quaternion NormaliseQuat( Quaternion q )
	{
		var length = q.Length();
		if ( length < 1e-8f ) return Quaternion.Identity;

		return new Quaternion( q.X / length, q.Y / length, q.Z / length, q.W / length );
	}

	public static Vector3 NormaliseVec( Vector3 v )
	{
		var length = v.Length;
		return length < 1e-8f ? new Vector3( 0.0f, 0.0f, 1.0f ) : v / length;
	}

	/// <summary>Any direction square to this one. Derived from the vector itself so it is the same answer every frame rather than a different arbitrary one.</summary>
	public static Vector3 Perpendicular( Vector3 v )
	{
		var axis = MathF.Abs( v.z ) < 0.9f ? new Vector3( 0.0f, 0.0f, 1.0f ) : new Vector3( 1.0f, 0.0f, 0.0f );
		return NormaliseVec( Vector3.Cross( v, axis ) );
	}

	public static Quaternion FromAxisAngle( Vector3 axis, float radians )
	{
		var unit = NormaliseVec( axis );
		var half = radians * 0.5f;
		var sine = MathF.Sin( half );

		return new Quaternion( unit.x * sine, unit.y * sine, unit.z * sine, MathF.Cos( half ) );
	}

	/// <summary>The shortest turn that takes one direction to another.</summary>
	public static Quaternion Between( Vector3 from, Vector3 to )
	{
		var a = NormaliseVec( from );
		var b = NormaliseVec( to );

		var dot = Vector3.Dot( a, b );

		if ( dot > 0.99999f ) return Quaternion.Identity;

		//Opposite directions have no shortest turn - every half turn about something square to them does it, so any one of those will do.
		if ( dot < -0.99999f ) return FromAxisAngle( Perpendicular( a ), MathF.PI );

		var axis = Vector3.Cross( a, b );

		return NormaliseQuat( new Quaternion( axis.x, axis.y, axis.z, 1.0f + dot ) );
	}

	//The swing-twist split: a turn is a roll about the axis and a lean away from it, and this is the roll.
	/// <summary>How much of a turn is about an axis, as an angle in radians.</summary>
	public static float TwistAngle( Quaternion q, Vector3 axis )
	{
		var unit = NormaliseVec( axis );
		var vector = new Vector3( q.X, q.Y, q.Z );

		var along = Vector3.Dot( vector, unit );

		var twist = NormaliseQuat( new Quaternion( unit.x * along, unit.y * along, unit.z * along, q.W ) );

		//The angle, signed the same way round as the axis.
		var angle = 2.0f * MathF.Acos( Math.Clamp( twist.W, -1.0f, 1.0f ) );
		if ( angle > MathF.PI ) angle -= 2.0f * MathF.PI;

		return along < 0.0f ? -MathF.Abs( angle ) : MathF.Abs( angle );
	}

	/// <summary>A row-vector, row-major 4x4 as a transform - what an inverse bind read from a model file is.</summary>
	public static AnimTrs FromMatrix( ReadOnlySpan<float> m )
	{
		var trs = Identity;
		trs.Position = new Vector3( m[12], m[13], m[14] );

		Span<float> row = stackalloc float[9];

		for ( var i = 0; i < 3; ++i )
			for ( var j = 0; j < 3; ++j )
				row[( i * 3 ) + j] = m[( i * 4 ) + j];

		Span<float> length = stackalloc float[3];

		for ( var i = 0; i < 3; ++i )
		{
			length[i] = MathF.Sqrt(
				( row[i * 3] * row[i * 3] ) +
				( row[( i * 3 ) + 1] * row[( i * 3 ) + 1] ) +
				( row[( i * 3 ) + 2] * row[( i * 3 ) + 2] ) );
		}

		//A negative determinant is a mirror, and one axis has to carry it or the rotation built below is not a rotation.
		var determinant =
			( row[0] * ( ( row[4] * row[8] ) - ( row[5] * row[7] ) ) ) -
			( row[1] * ( ( row[3] * row[8] ) - ( row[5] * row[6] ) ) ) +
			( row[2] * ( ( row[3] * row[7] ) - ( row[4] * row[6] ) ) );

		if ( determinant < 0.0f ) length[0] = -length[0];

		trs.Scale = new Vector3( length[0], length[1], length[2] );

		for ( var i = 0; i < 3; ++i )
		{
			var divisor = MathF.Abs( length[i] ) < 1e-8f ? 1.0f : length[i];
			for ( var j = 0; j < 3; ++j ) row[( i * 3 ) + j] /= divisor;
		}

		//Read as a column-vector rotation - the transpose of what is stored - so the usual formulas apply unchanged.
		float m00 = row[0], m10 = row[1], m20 = row[2];
		float m01 = row[3], m11 = row[4], m21 = row[5];
		float m02 = row[6], m12 = row[7], m22 = row[8];

		var trace = m00 + m11 + m22;
		Quaternion rotation;

		if ( trace > 0.0f )
		{
			var s = MathF.Sqrt( trace + 1.0f ) * 2.0f;
			rotation = new Quaternion( ( m21 - m12 ) / s, ( m02 - m20 ) / s, ( m10 - m01 ) / s, 0.25f * s );
		}
		else if ( m00 > m11 && m00 > m22 )
		{
			var s = MathF.Sqrt( 1.0f + m00 - m11 - m22 ) * 2.0f;
			rotation = new Quaternion( 0.25f * s, ( m01 + m10 ) / s, ( m02 + m20 ) / s, ( m21 - m12 ) / s );
		}
		else if ( m11 > m22 )
		{
			var s = MathF.Sqrt( 1.0f + m11 - m00 - m22 ) * 2.0f;
			rotation = new Quaternion( ( m01 + m10 ) / s, 0.25f * s, ( m12 + m21 ) / s, ( m02 - m20 ) / s );
		}
		else
		{
			var s = MathF.Sqrt( 1.0f + m22 - m00 - m11 ) * 2.0f;
			rotation = new Quaternion( ( m02 + m20 ) / s, ( m12 + m21 ) / s, 0.25f * s, ( m10 - m01 ) / s );
		}

		trs.Rotation = NormaliseQuat( rotation );
		return trs;
	}

	/// <summary>Converts to the engine Transform where a pose leaves the runtime; same fields, no packing</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly Transform ToTransform() => new( Position, Rotation, Scale );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static AnimTrs FromTransform( in Transform transform )
	{
		AnimTrs result;
		result.Position = transform.Position;
		result.Rotation = transform.Rotation;
		result.Scale = transform.Scale;

		return result;
	}
}
