using Rinten;

//Quake axes (+x forward, +y left, +z up) and the conversion to ours; texture math stays native
internal static class QuakeAxes
{
	public static ForeignBasis Basis => ForeignBasis.Quake;

	/// <summary>A position or a direction out of a Quake file.</summary>
	public static Vector3 Convert( Vector3 v ) => Basis.Vector( v );

	/// <summary>A rotation out of a Quake file.</summary>
	public static Rotation Convert( Rotation r ) => Basis.Quat( r );

	/// <summary>An extent out of a Quake file.</summary>
	public static Vector3 Extent( Vector3 s ) => Basis.Size( s );

	/// <summary>A box out of a Quake file, corners put back in order.</summary>
	public static BBox Convert( BBox b ) => Basis.Box( b );
}
