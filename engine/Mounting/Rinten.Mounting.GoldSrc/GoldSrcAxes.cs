using Rinten;

//GoldSrc axes (+x forward, +y left, +z up) and the conversion to ours; format math stays native
internal static class GoldSrcAxes
{
	public static ForeignBasis Basis => ForeignBasis.Quake;

	/// <summary>A position or a direction out of a GoldSrc file.</summary>
	public static Vector3 Convert( Vector3 v ) => Basis.Vector( v );

	/// <summary>A rotation out of a GoldSrc file.</summary>
	public static Rotation Convert( Rotation r ) => Basis.Quat( r );

	/// <summary>A bone frame out of a GoldSrc file.</summary>
	public static Transform Convert( Transform t ) => Basis.Frame( t );
}
