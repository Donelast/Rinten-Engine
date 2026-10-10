namespace Rinten;

/// <summary>Another engine's axis basis and the turn onto ours (+X right, +Y up, -Z forward, right-handed)</summary>
public readonly struct ForeignBasis
{
	/// <summary>What that engine calls its axes, for reports and logs.</summary>
	public string Name { get; }

	/// <summary>Where its +X lands in ours.</summary>
	public Vector3 AxisX { get; }

	/// <summary>Where its +Y lands in ours.</summary>
	public Vector3 AxisY { get; }

	/// <summary>Where its +Z lands in ours.</summary>
	public Vector3 AxisZ { get; }

	private ForeignBasis( string name, Vector3 x, Vector3 y, Vector3 z )
	{
		Name = name;
		AxisX = x;
		AxisY = y;
		AxisZ = z;
	}

	/// <summary>A basis declared by where its three axes land in ours. Each of those has to be an axis of ours, signed - a basis that shears is not a basis change.</summary>
	public static ForeignBasis From( string name, Vector3 x, Vector3 y, Vector3 z ) => new( name, x, y, z );

	/// <summary>A source that already counts the way we do: glTF, FBX asked for right-handed Y-up, OpenXR, SteamVr, Recast, Serious Engine 3.</summary>
	public static ForeignBasis Same { get; } = From( "the same axes",
		new Vector3( 1, 0, 0 ), new Vector3( 0, 1, 0 ), new Vector3( 0, 0, 1 ) );

	/// <summary>Quake and everything descended from it: <c>+x forward, +y left, +z up</c>, right handed. The engine's own basis until this pass, which is why so much of it was written down as nothing at all.</summary>
	public static ForeignBasis Quake { get; } = From( "+x forward, +y left, +z up",
		new Vector3( 0, 0, -1 ), new Vector3( -1, 0, 0 ), new Vector3( 0, 1, 0 ) );

	/// <summary>Spark, which Natural Selection 2 is built on: <c>+x left, +y up, +z forward</c>, right handed. Half a turn about up from ours.</summary>
	public static ForeignBasis Spark { get; } = From( "+x left, +y up, +z forward",
		new Vector3( -1, 0, 0 ), new Vector3( 0, 1, 0 ), new Vector3( 0, 0, -1 ) );

	/// <summary>Whether the turn onto ours flips handedness. Nothing here does, and a basis that did would need every triangle wound the other way round.</summary>
	public bool Mirrors => Determinant < 0.0f;

	private float Determinant =>
		( AxisX.x * ( ( AxisY.y * AxisZ.z ) - ( AxisY.z * AxisZ.y ) ) ) -
		( AxisX.y * ( ( AxisY.x * AxisZ.z ) - ( AxisY.z * AxisZ.x ) ) ) +
		( AxisX.z * ( ( AxisY.x * AxisZ.y ) - ( AxisY.y * AxisZ.x ) ) );

	/// <summary>Whether this basis is ours, so a caller can skip the work.</summary>
	public bool IsIdentity => AxisX == new Vector3( 1, 0, 0 )
		&& AxisY == new Vector3( 0, 1, 0 )
		&& AxisZ == new Vector3( 0, 0, 1 );

	/// <summary>A position or a direction.</summary>
	public Vector3 Vector( Vector3 v ) => ( AxisX * v.x ) + ( AxisY * v.y ) + ( AxisZ * v.z );

	/// <summary>An extent. The axes move but the signs do not - a box does not gain a negative side because the world turned, and a mirrored rig stays mirrored rather than quietly turning right way out.</summary>
	public Vector3 Size( Vector3 s ) =>
		( AxisX.Abs() * s.x ) + ( AxisY.Abs() * s.y ) + ( AxisZ.Abs() * s.z );

	/// <summary>A rotation, conjugated by the turn. Which for a signed permutation is that same permutation on the vector part, the angle left alone.</summary>
	public Rotation Quat( Rotation r )
	{
		if ( Mirrors )
			throw new InvalidOperationException( $"{Name} mirrors, and a mirrored basis has no quaternion to conjugate by." );

		var v = Vector( new Vector3( r.x, r.y, r.z ) );

		return new Rotation( v.x, v.y, v.z, r.w );
	}

	/// <summary>Pitch, yaw and roll keep their meanings across every basis here: pitch is the dip, yaw is the heading, roll is about the way you face. So angles come over as they were written and only vectors move.</summary>
	public Angles Angles( Angles a ) => a;

	/// <summary>A box, corner by corner and then put back in order - a turned min is not a min.</summary>
	public BBox Box( BBox box )
	{
		var a = Vector( box.Mins );
		var b = Vector( box.Maxs );

		return new BBox( Vector3.Min( a, b ), Vector3.Max( a, b ) );
	}

	/// <summary>A transform: its place, its facing and its size, each by its own rule.</summary>
	public Transform Frame( Transform t ) => new( Vector( t.Position ), Quat( t.Rotation ), Size( t.Scale ) );

	/// <summary>A 4x4, conjugated as <c>P-transpose * M * P</c>. A relabelled matrix would turn a bone about the wrong axis while leaving its vertices in the right place, which reads as a rig problem and is not one.</summary>
	public Matrix Matrix( Matrix m )
	{
		//P takes a row vector of theirs into ours, so its rows are where their axes land; being a rotation, its inverse is its transpose.
		var p = System.Numerics.Matrix4x4.Identity;

		p.M11 = AxisX.x; p.M12 = AxisX.y; p.M13 = AxisX.z;
		p.M21 = AxisY.x; p.M22 = AxisY.y; p.M23 = AxisY.z;
		p.M31 = AxisZ.x; p.M32 = AxisZ.y; p.M33 = AxisZ.z;

		return (Matrix)System.Numerics.Matrix4x4.Multiply(
			System.Numerics.Matrix4x4.Multiply( System.Numerics.Matrix4x4.Transpose( p ), m ), p );
	}

	public override string ToString() => Name;
}
