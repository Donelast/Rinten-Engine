using Rinten;
using Rinten.Interpolation;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

/// <summary>A 3-dimentional vector. Typically represents a position, size, or direction in 3D space.</summary>
[JsonConverter( typeof( Rinten.Internal.JsonConvert.Vector3Converter ) )]
[StructLayout( LayoutKind.Sequential )]
public partial struct Vector3 : System.IEquatable<Vector3>, IParsable<Vector3>, IInterpolator<Vector3>
{
	internal System.Numerics.Vector3 _vec;

	/// <summary>The X component of this vector.</summary>
	[ActionGraphInclude( AutoExpand = true )]
	public float x
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		readonly get => _vec.X;
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		set => _vec.X = value;
	}

	/// <summary>The Y component of this vector.</summary>
	[ActionGraphInclude( AutoExpand = true )]
	public float y
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		readonly get => _vec.Y;
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		set => _vec.Y = value;
	}

	/// <summary>The Z component of this vector.</summary>
	[ActionGraphInclude( AutoExpand = true )]
	public float z
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		readonly get => _vec.Z;
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		set => _vec.Z = value;
	}

	/// <summary>Initializes a vector with given components.</summary>
	/// <param name="x">The X component.</param>
	/// <param name="y">The Y component.</param>
	/// <param name="z">The Z component.</param>
	[ActionGraphNode( "vec3.new" ), Title( "Vector3" ), Group( "Math/Geometry/Vector3" )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector3( float x, float y, float z )
	{
		_vec = default;
		_vec.X = x;
		_vec.Y = y;
		_vec.Z = z;
	}

	/// <summary>Initializes a vector with given components and Z set to 0.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector3( float x, float y ) : this( x, y, 0 )
	{
	}

	/// <summary>Initializes a Vector3 from a given Vector3, i.e. creating a copy.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector3( in Vector3 other ) : this( other.x, other.y, other.z )
	{
	}

	/// <summary>Initializes a Vector3 from given Vector2 and Z component.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector3( in Vector2 other, float z ) : this( other.x, other.y, z )
	{
	}

	/// <summary>Initializes the vector with all components set to given value.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector3( float all = 0.0f ) : this( all, all, all )
	{
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Vector3( System.Numerics.Vector3 v )
	{
		_vec = v;
	}

	/// <summary>A vector with all components set to 1.</summary>
	public static readonly Vector3 One = new Vector3( 1 );

	/// <summary>A vector with all components set to 0.</summary>
	public static readonly Vector3 Zero = new Vector3( 0 );

	/// <summary>A vector with Z set to -1. This represents the forward direction.</summary>
	public static readonly Vector3 Forward = new Vector3( 0, 0, -1 );

	/// <summary>A vector with Z set to 1. This represents the backward direction.</summary>
	public static readonly Vector3 Backward = new Vector3( 0, 0, 1 );

	/// <summary>A vector with Y set to 1. This represents the up direction.</summary>
	public static readonly Vector3 Up = new Vector3( 0, 1, 0 );

	/// <summary>A vector with Y set to -1. This represents the down direction.</summary>
	public static readonly Vector3 Down = new Vector3( 0, -1, 0 );

	/// <summary>A vector with X set to 1. This represents the right hand direction.</summary>
	public static readonly Vector3 Right = new Vector3( 1, 0, 0 );

	/// <summary>A vector with X set to -1. This represents the left hand direction.</summary>
	public static readonly Vector3 Left = new Vector3( -1, 0, 0 );

	/// <summary>Uniformly samples a 3D position from all points with distance at most 1 from the origin.</summary>
	[ActionGraphNode( "vec3.random" ), Title( "Random Vector3" ), Group( "Math/Geometry/Vector3" ), Icon( "casino" )]
	public static Vector3 Random => RintenSystem.Random.VectorInSphere();

	/// <summary>Returns a unit version of this vector. A unit vector has length of 1.</summary>
	[JsonIgnore]
	public readonly Vector3 Normal
	{
		get
		{
			//Noticing this should probably just return Zero like Vector2 does, but that would likely break some games -Carson
			if ( IsNearZeroLength ) return this;

			return System.Numerics.Vector3.Normalize( _vec );
		}
	}

	/// <summary>Length (or magnitude) of the vector (Distance from 0,0,0).</summary>
	[JsonIgnore]
	public readonly float Length => _vec.Length();

	/// <summary>Squared length of the vector. This is faster than <see cref="Length">Length</see>, and can be used for things like comparing distances, as long as only squared values are used.</summary>
	[JsonIgnore]
	public readonly float LengthSquared => _vec.LengthSquared();

	/// <summary>Returns the inverse of this vector, which is useful for scaling vectors.</summary>
	public readonly Vector3 Inverse => new( 1.0f / x, 1.0f / y, 1.0f / z );

	/// <summary>Returns true if x, y or z are NaN</summary>
	[JsonIgnore]
	public readonly bool IsNaN => float.IsNaN( x ) || float.IsNaN( y ) || float.IsNaN( z );

	/// <summary>Returns true if x, y or z are infinity</summary>
	[JsonIgnore]
	public readonly bool IsInfinity => float.IsInfinity( x ) || float.IsInfinity( y ) || float.IsInfinity( z );

	/// <summary>Returns true if the squared length is less than 1e-8 (which is really near zero)</summary>
	[JsonIgnore]
	public readonly bool IsNearZeroLength => LengthSquared <= 1e-8f;

	/// <summary>Returns this vector with given X component.</summary>
	public readonly Vector3 WithX( float x ) => new Vector3( x, y, z );

	/// <summary>Returns this vector with given Y component.</summary>
	public readonly Vector3 WithY( float y ) => new Vector3( x, y, z );

	/// <summary>Returns this vector with given Z component.</summary>
	public readonly Vector3 WithZ( float z ) => new Vector3( x, y, z );

	/// <summary>Returns true if value on every axis is less than tolerance away from zero</summary>
	public readonly bool IsNearlyZero( float tolerance = 0.0001f )
	{
		var abs = System.Numerics.Vector3.Abs( _vec );
		return abs.X <= tolerance &&
			   abs.Y <= tolerance &&
			   abs.Z <= tolerance;
	}

	/// <summary>Returns a vector whose length is limited to given maximum.</summary>
	public readonly Vector3 ClampLength( float maxLength )
	{
		var lenSqr = LengthSquared;

		if ( lenSqr <= 0.0f )
			return Zero;

		if ( lenSqr <= maxLength * maxLength )
			return this;

		return this * (maxLength / MathF.Sqrt( lenSqr ));
	}

	/// <summary>Returns a vector whose length is limited between given minimum and maximum.</summary>
	public readonly Vector3 ClampLength( float minLength, float maxLength )
	{
		float minSqr = minLength * minLength;
		float maxSqr = maxLength * maxLength;
		float lenSqr = LengthSquared;

		if ( lenSqr <= 0.0f )
			return Zero;

		if ( lenSqr <= minSqr )
			return this * (minLength / MathF.Sqrt( lenSqr ));
		if ( lenSqr >= maxSqr )
			return this * (maxLength / MathF.Sqrt( lenSqr ));

		return this;
	}

	/// <summary>Returns a vector each axis of which is clamped to between the 2 given vectors. Basically clamps a point to an Axis Aligned Bounding Box (AABB).</summary>
	public readonly Vector3 Clamp( Vector3 otherMin, Vector3 otherMax )
	{
		return System.Numerics.Vector3.Clamp( _vec, otherMin._vec, otherMax._vec );
	}

	/// <summary>Returns a vector each axis of which is clamped to given min and max values.</summary>
	public readonly Vector3 Clamp( float min, float max ) => Clamp( new Vector3( min ), new Vector3( max ) );

	/// <summary>Restricts a vector between a minimum and a maximum value.</summary>
	public static Vector3 Clamp( in Vector3 value, in Vector3 min, in Vector3 max ) => System.Numerics.Vector3.Clamp( value, min, max );

	/// <summary>Returns a vector that has the minimum values on each axis between this vector and given vector.</summary>
	public readonly Vector3 ComponentMin( in Vector3 other )
	{
		return System.Numerics.Vector3.Min( _vec, other._vec );
	}

	/// <summary>Returns a vector that has the minimum values on each axis between the 2 given vectors.</summary>
	public static Vector3 Min( in Vector3 a, in Vector3 b ) => a.ComponentMin( b );

	/// <summary>Returns a vector that has the maximum values on each axis between this vector and given vector.</summary>
	public readonly Vector3 ComponentMax( in Vector3 other )
	{
		return System.Numerics.Vector3.Max( _vec, other._vec );
	}

	/// <summary>Returns a vector that has the maximum values on each axis between the 2 given vectors.</summary>
	public static Vector3 Max( in Vector3 a, in Vector3 b ) => a.ComponentMax( b );

	/// <summary>Performs linear interpolation between 2 given vectors.</summary>
	/// <param name="a">Vector A</param>
	/// <param name="b">Vector B</param>
	/// <param name="frac">Fraction, where 0 would return Vector A, 0.5 would return a point between the 2 vectors, and 1 would return Vector B.</param>
	/// <param name="clamp">Whether to clamp the fraction argument between [0,1]</param>
	[ActionGraphNode( "geom.lerp" ), Pure, Group( "Math/Geometry" ), Icon( "timeline" )]
	public static Vector3 Lerp( Vector3 a, Vector3 b, [Range( 0f, 1f )] float frac, bool clamp = true )
	{
		if ( clamp ) frac = frac.Clamp( 0, 1 );
		return System.Numerics.Vector3.Lerp( a._vec, b._vec, frac );
	}


	/// <summary>Performs linear interpolation between this and given vectors.</summary>
	public readonly Vector3 LerpTo( in Vector3 target, float frac, bool clamp = true ) => Lerp( this, target, frac, clamp );

	/// <summary>Performs linear interpolation between 2 given vectors, with separate fraction for each vector component.</summary>
	public static Vector3 Lerp( in Vector3 a, in Vector3 b, Vector3 frac, bool clamp = true )
	{
		if ( clamp ) frac = frac.Clamp( 0, 1 );

		return System.Numerics.Vector3.Lerp( a._vec, b._vec, frac._vec );
	}

	/// <summary>Performs linear interpolation between this and given vectors, with separate fraction for each vector component.</summary>
	public readonly Vector3 LerpTo( in Vector3 target, in Vector3 frac, bool clamp = true ) => Lerp( this, target, frac, clamp );

	/// <summary>Performs spherical linear interpolation (Slerp) between two vectors.</summary>
	public static Vector3 Slerp( Vector3 a, Vector3 b, [Range( 0f, 1f )] float frac, bool clamp = true )
	{
		if ( clamp ) frac = frac.Clamp( 0, 1 );
		var dot = Dot( a, b ).Clamp( -1.0f, 1.0f );
		var theta = MathF.Acos( dot ) * frac;
		var relative = (b - a * dot).Normal;
		var c = (a * MathF.Cos( theta )) + (relative * MathF.Sin( theta ));
		return c.Normal;
	}

	/// <summary>Performs spherical linear interpolation (Slerp) between this vector and a target vector.</summary>
	public readonly Vector3 SlerpTo( in Vector3 target, float frac, bool clamp = true ) => Slerp( this, target, frac, clamp );

	/// <summary>Given a position, and two other positions, calculate the inverse lerp position between those</summary>
	public static float InverseLerp( Vector3 pos, Vector3 a, Vector3 b, bool clamp = true )
	{
		var delta = b - a;
		var delta2 = pos - a;
		var dot = Vector3.Dot( delta2, delta ) / Vector3.Dot( delta, delta );
		if ( clamp ) dot = dot.Clamp( 0, 1 );
		return dot;
	}

	/// <summary>Returns the cross product of the 2 given vectors. If the given vectors are linearly independent, the resulting vector is perpendicular to them both, also known as a normal of a plane.</summary>
	[ActionGraphNode( "geom.cross" ), Pure, Group( "Math/Geometry" ), Icon( "close" )]
	public static Vector3 Cross( in Vector3 a, in Vector3 b )
	{
		return System.Numerics.Vector3.Cross( a._vec, b._vec );
	}

	/// <summary>Returns the cross product of this and the given vector. If this and the given vectors are linearly independent, the resulting vector is perpendicular to them both, also known as a normal of a plane.</summary>
	public readonly Vector3 Cross( in Vector3 b )
	{
		return System.Numerics.Vector3.Cross( _vec, b._vec );
	}

	/// <summary>Returns the scalar/dot product of the 2 given vectors.</summary>
	[ActionGraphNode( "geom.dot" ), Pure, Group( "Math/Geometry" ), Icon( "fiber_manual_record" )]
	public static float Dot( in Vector3 a, in Vector3 b ) => System.Numerics.Vector3.Dot( a._vec, b._vec );

	/// <summary>Returns the scalar/dot product of this and the given vectors.</summary>
	public readonly float Dot( in Vector3 b ) => Dot( this, b );

	/// <summary>Returns distance between the 2 given vectors.</summary>
	[ActionGraphNode( "geom.dist" ), Pure, Title( "Distance" ), Group( "Math/Geometry" ), Icon( "straighten" )]
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float DistanceBetween( in Vector3 a, in Vector3 b )
	{
		return System.Numerics.Vector3.Distance( a._vec, b._vec );
	}

	/// <summary>Returns distance between this vector to given vector.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float Distance( in Vector3 target ) => DistanceBetween( this, target );

	/// <summary>Returns squared distance between the 2 given vectors. This is faster than <see cref="DistanceBetween">DistanceBetween</see>, and can be used for things like comparing distances, as long as only squared values are used.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float DistanceBetweenSquared( in Vector3 a, in Vector3 b )
	{
		return System.Numerics.Vector3.DistanceSquared( a._vec, b._vec );
	}

	/// <summary>Returns squared distance between this vector to given vector. This is faster than <see cref="Distance">Distance</see>, and can be used for things like comparing distances, as long as only squared values are used.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float DistanceSquared( in Vector3 target ) => DistanceBetweenSquared( this, target );

	/// <summary>Calculates the normalized direction vector from one point to another in 3D space.</summary>
	public static Vector3 Direction( in Vector3 from, in Vector3 to )
	{
		return (to - from).Normal;
	}

	/// <summary>Removes the component along direction (a normal), scaled by its length and strength</summary>
	public readonly Vector3 SubtractDirection( in Vector3 direction, float strength = 1.0f )
	{
		return this - (direction * Dot( direction ) * strength);
	}

	/// <summary>Returns a new vector whose length is closer to given target length by given amount.</summary>
	public readonly Vector3 Approach( float length, float amount )
	{
		return Normal * Length.Approach( length, amount );
	}

	/// <summary>Returns a new vector with all values positive. -5 becomes 5, etc.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly Vector3 Abs()
	{
		return System.Numerics.Vector3.Abs( _vec );
	}

	/// <summary>Returns a new vector with all values positive. -5 becomes 5, etc.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector3 Abs( in Vector3 value )
	{
		return System.Numerics.Vector3.Abs( value );
	}

	/// <summary>Returns a reflected vector based on incoming direction and plane normal. Like a ray reflecting off of a mirror.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector3 Reflect( in Vector3 direction, in Vector3 normal )
	{
		return System.Numerics.Vector3.Reflect( direction._vec, normal._vec );
	}

	/// <summary><a href="https://en.wikipedia.org/wiki/Vector_projection">Projects given vector</a> on a plane defined by <paramref name="planeNormal"/>.</summary>
	public static Vector3 VectorPlaneProject( in Vector3 v, in Vector3 planeNormal )
	{
		return v - v.ProjectOnNormal( planeNormal );
	}

	/// <summary><a href="https://en.wikipedia.org/wiki/Vector_projection">Projects this vector</a> onto another vector. Basically extends the given normal/unit vector to be as long as necessary to make a right triangle (a triangle which has a 90 degree corner) between (0,0,0), this vector and the projected vector.</summary>
	public readonly Vector3 ProjectOnNormal( in Vector3 normal )
	{
		return (normal * Dot( this, normal ));
	}

	/// <summary>Sort these two vectors into min and max. This doesn't just swap the vectors, it sorts each component. So that min will come out containing the minimum x, y and z values.</summary>
	public static void Sort( ref Vector3 min, ref Vector3 max )
	{
		var a = System.Numerics.Vector3.Min( min._vec, max._vec );
		var b = System.Numerics.Vector3.Max( min._vec, max._vec );

		min = a;
		max = b;
	}

	/// <summary>Returns true if we're nearly equal to the passed vector.</summary>
	public readonly bool AlmostEqual( in Vector3 v, float delta = 0.0001f )
	{
		if ( MathF.Abs( x - v.x ) > delta ) return false;
		if ( MathF.Abs( y - v.y ) > delta ) return false;
		if ( MathF.Abs( z - v.z ) > delta ) return false;

		return true;
	}

	/// <summary>Calculates position of a point on a cubic beizer curve at given fraction.</summary>
	public static Vector3 CubicBezier( in Vector3 source, in Vector3 target, in Vector3 sourceTangent, in Vector3 targetTangent, float t )
	{
		t = t.Clamp( 0, 1 );

		var invT = 1 - t;
		return invT * invT * invT * source +
			3 * invT * invT * t * sourceTangent +
			3 * invT * t * t * targetTangent +
			t * t * t * target;
	}

	/// <summary>Snap to grid along any of the 3 axes.</summary>
	public readonly Vector3 SnapToGrid( float gridSize, bool sx = true, bool sy = true, bool sz = true )
	{
		return gridSize.AlmostEqual( 0.0f ) ? this : new Vector3( sx ? x.SnapToGrid( gridSize ) : x, sy ? y.SnapToGrid( gridSize ) : y, sz ? z.SnapToGrid( gridSize ) : z );
	}



	/// <summary>Return the distance between the two direction vectors in degrees.</summary>
	public static float GetAngle( in Vector3 v1, in Vector3 v2 )
	{
		return MathF.Acos( Dot( v1.Normal, v2.Normal ).Clamp( -1, 1 ) ).RadianToDegree();
	}

	/// <summary>Return the distance between the two direction vectors in degrees.</summary>
	public readonly float Angle( in Vector3 other )
	{
		return GetAngle( this, other );
	}

	/// <summary>Converts a direction vector to an angle.</summary>
	public static Angles VectorAngle( in Vector3 vec )
	{
		float tmp, yaw, pitch;

		//The inverse of Angles.AngleVector: yaw is the heading in the XZ plane measured from -Z towards -X, pitch is the dip below that plane.
		if ( vec.x == 0.0f && vec.z == 0.0f )
		{
			yaw = 0.0f;
			pitch = (vec.y > 0.0f) ? 270.0f : 90.0f;
		}
		else
		{
			yaw = MathF.Atan2( -vec.x, -vec.z ) * (180.0f / MathF.PI);
			if ( yaw < 0.0f )
			{
				yaw += 360.0f;
			}
			tmp = MathF.Sqrt( (vec.x * vec.x) + (vec.z * vec.z) );
			pitch = MathF.Atan2( -vec.y, tmp ) * (180.0f / MathF.PI);
			if ( pitch < 0.0f )
			{
				pitch += 360.0f;
			}
		}
		return new Angles( pitch, yaw, 0 );
	}

	/// <summary>The Euler angles of this direction vector.</summary>
	public Angles EulerAngles
	{
		readonly get { return VectorAngle( this ); }
		set { this = Angles.AngleVector( value ); }
	}

	/// <summary>Try to add to this vector. If we're already over max then don't add. If we're over max when we add, clamp in that direction so we're not.</summary>
	public readonly Vector3 AddClamped( in Vector3 toAdd, float maxLength )
	{
		var dir = toAdd.Normal;

		//Already over - just return self
		var dot = Dot( dir );
		if ( dot > maxLength ) return this;

		//Add it
		var vec = this + toAdd;
		dot = vec.Dot( dir );
		if ( dot < maxLength ) return vec;

		//We're over, take off the rest
		vec -= dir * (dot - maxLength);
		return vec;
	}

	/// <summary>Rotate this vector around given point by given rotation and return the result as a new vector.<br/> See <see cref="Transform.RotateAround"/> for similar method that also transforms rotation.</summary>
	public readonly Vector3 RotateAround( in Vector3 center, in Rotation rot )
	{
		return center + (rot * (this - center));
	}

	#region operators
	public float this[int index]
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		readonly get => _vec[index];
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		set => _vec[index] = value;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector3 operator +( Vector3 c1, Vector3 c2 ) => System.Numerics.Vector3.Add( c1._vec, c2._vec );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector3 operator -( Vector3 c1, Vector3 c2 ) => System.Numerics.Vector3.Subtract( c1._vec, c2._vec );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector3 operator *( Vector3 c1, float f ) => System.Numerics.Vector3.Multiply( c1._vec, f );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector3 operator *( Vector3 c1, Rotation f ) => System.Numerics.Vector3.Transform( c1._vec, f._quat );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector3 operator *( Vector3 c1, Vector3 c2 ) => System.Numerics.Vector3.Multiply( c1._vec, c2._vec );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector3 operator *( float f, Vector3 c1 ) => System.Numerics.Vector3.Multiply( f, c1._vec );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector3 operator /( Vector3 c1, float f ) => System.Numerics.Vector3.Divide( c1._vec, f );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector3 operator /( Vector3 c1, in Vector3 c2 ) => System.Numerics.Vector3.Divide( c1._vec, c2._vec );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static Vector3 operator -( Vector3 value ) => System.Numerics.Vector3.Negate( value._vec );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static implicit operator Vector3( Color value ) => new Vector3( value.r, value.g, value.b );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static implicit operator Vector3( float value ) => new Vector3( value, value, value );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static implicit operator Vector3( Vector2 value ) => new Vector3( value.x, value.y, 0.0f );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static implicit operator Vector3( System.Numerics.Vector3 value ) => new Vector3 { _vec = value };
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static implicit operator System.Numerics.Vector3( Vector3 value ) => new System.Numerics.Vector3( value.x, value.y, value.z );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static implicit operator Vector3( Vector4 vec ) => new Vector3( (float)vec.x, (float)vec.y, (float)vec.z );
	#endregion

	#region equality
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool operator ==( Vector3 left, Vector3 right ) => left.AlmostEqual( right );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool operator !=( Vector3 left, Vector3 right ) => !left.AlmostEqual( right );
	public override readonly bool Equals( object obj ) => obj is Vector3 o && Equals( o );
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly bool Equals( Vector3 o ) => _vec.Equals( o._vec );
	public readonly override int GetHashCode() => _vec.GetHashCode();
	#endregion

	/// <summary>Formats the vector into a string "x,y,z"</summary>
	public readonly override string ToString()
	{
		return $"{x:0.####},{y:0.####},{z:0.####}";
	}

	public static Vector3 Parse( string str, IFormatProvider provider )
	{
		return Parse( str );
	}

	public static Vector3 Parse( string str )
	{
		if ( TryParse( str, CultureInfo.InvariantCulture, out var res ) )
			return res;

		return default;
	}

	public static bool TryParse( string str, out Vector3 result )
	{
		return TryParse( str, CultureInfo.InvariantCulture, out result );
	}

	/// <summary>Given a string, try to convert this into a vector. Example input formats that work would be "1,1,1", "1;1;1", "[1 1 1]". This handles a bunch of different separators ( ' ', ',', ';', '\n', '\r' ). It also trims surrounding characters ('[', ']', ' ', '\n', '\r', '\t', '"').</summary>
	public static bool TryParse( [NotNullWhen( true )] string str, IFormatProvider provider, [MaybeNullWhen( false )] out Vector3 result )
	{
		result = Vector3.Zero;

		if ( string.IsNullOrWhiteSpace( str ) )
			return false;

		str = str.Trim( '[', ']', ' ', '\n', '\r', '\t', '"' );

		var components = str.Split( new[] { ' ', ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries );

		if ( components.Length != 3 )
			return false;

		if ( !float.TryParse( components[0], NumberStyles.Float, provider, out float x ) ||
			!float.TryParse( components[1], NumberStyles.Float, provider, out float y ) ||
			!float.TryParse( components[2], NumberStyles.Float, provider, out float z ) )
		{
			return false;
		}

		result = new Vector3( x, y, z );
		return true;
	}

	/// <summary>Move to the target vector, by amount acceleration</summary>
	public readonly Vector3 WithAcceleration( Vector3 target, float acceleration )
	{
		if ( target.IsNearZeroLength )
			return this;

		Vector3 wishdir = target.Normal;
		float wishspeed = target.Length;

		//See if we are changing direction a bit
		var currentspeed = Dot( wishdir );

		//Reduce wishspeed by the amount of veer.
		var addspeed = wishspeed - currentspeed;

		//If not going to add any speed, done.
		if ( addspeed <= 0 )
			return this;

		//Determine amount of acceleration.
		var accelspeed = acceleration * wishspeed;

		//Cap at addspeed
		if ( accelspeed > addspeed )
			accelspeed = addspeed;

		return this + wishdir * accelspeed;
	}

	/// <summary>Apply an amount of friction to the current velocity.</summary>
	public readonly Vector3 WithFriction( float frictionAmount, float stopSpeed = 140.0f )
	{
		var speed = Length;
		if ( speed < 0.01f ) return this;

		//Bleed off some speed, but if we have less than the bleed threshold, bleed the threshold amount.
		float control = (speed < stopSpeed) ? stopSpeed : speed;

		//Add the amount to the drop amount.
		var drop = control * frictionAmount;

		//scale the velocity
		float newspeed = speed - drop;
		if ( newspeed < 0 ) newspeed = 0;
		if ( newspeed == speed ) return this;

		newspeed /= speed;
		return this * newspeed;
	}

	/// <summary>Calculates a point on a Catmull-Rom spline given four control points and a parameter t.</summary>
	public static Vector3 CatmullRomSpline( in Vector3 p0, in Vector3 p1, in Vector3 p2, in Vector3 p3, float t )
	{
		var t2 = t * t;
		var t3 = t2 * t;

		var part1 = -t3 + 2.0f * t2 - t;
		var part2 = 3.0f * t3 - 5.0f * t2 + 2.0f;
		var part3 = -3.0f * t3 + 4.0f * t2 + t;
		var part4 = t3 - t2;

		var blendedPoint = 0.5f * (p0 * part1 + p1 * part2 + p2 * part3 + p3 * part4);
		return blendedPoint;
	}

	/// <summary>Calculates an interpolated point using the Kochanek-Bartels spline (TCB spline).</summary>
	public static Vector3 TcbSpline( in Vector3 p0, in Vector3 p1, in Vector3 p2, in Vector3 p3, float tension, float continuity, float bias, float u )
	{
		return TcbSegment.Between( p0, p1, p2, p3, tension, continuity, bias ).At( u );
	}

	/// <summary>One segment of a TCB spline, worked out: the four coefficients of its cubic.</summary>
	public readonly struct TcbSegment
	{
		public readonly Vector3 A, B, C, D;

		private TcbSegment( in Vector3 a, in Vector3 b, in Vector3 c, in Vector3 d )
		{
			A = a; B = b; C = c; D = d;
		}

		/// <summary>The segment from p1 to p2, shaped by the points either side of it.</summary>
		public static TcbSegment Between( in Vector3 p0, in Vector3 p1, in Vector3 p2, in Vector3 p3,
			float tension, float continuity, float bias )
		{
			//Compute the tangent vectors using the TCB parameters
			Vector3 m1 = (1 - tension) * (1 + continuity) * (1 + bias) * (p1 - p0) / 2 +
						 (1 - tension) * (1 - continuity) * (1 - bias) * (p2 - p1) / 2;
			Vector3 m2 = (1 - tension) * (1 - continuity) * (1 + bias) * (p2 - p1) / 2 +
						 (1 - tension) * (1 + continuity) * (1 - bias) * (p3 - p2) / 2;

			//Compute the coefficients of the cubic polynomial
			return new TcbSegment(
				2 * (p1 - p2) + m1 + m2,
				-3 * (p1 - p2) - 2 * m1 - m2,
				m1,
				p1 );
		}

		/// <summary>The point a fraction of the way along it.</summary>
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public Vector3 At( float u ) => ( A * u * u * u ) + ( B * u * u ) + ( C * u ) + D;
	}

	Vector3 IInterpolator<Vector3>.Interpolate( Vector3 a, Vector3 b, float delta )
	{
		return a.LerpTo( b, delta );
	}
}
