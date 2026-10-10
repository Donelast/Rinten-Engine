using System.Numerics;
using Rinten;

/// <summary>A struct describing an origin and direction</summary>
public struct Ray : IEquatable<Ray>
{
	private Vector3 _origin;
	private Vector3 _direction;

	/// <summary>Origin of the ray.</summary>
	public Vector3 Position
	{
		readonly get => _origin;
		set => _origin = value;
	}

	/// <summary>Direction of the ray.</summary>
	public Vector3 Forward
	{
		readonly get => _direction;
		set => _direction = value;
	}

	public Ray( Vector3 origin, Vector3 direction )
	{
		_origin = origin;
		_direction = direction;
	}

	/// <summary>Convert a ray to be local to this transform</summary>
	public readonly Ray ToLocal( in Transform tx )
	{
		var ray = this;
		ray.Forward = tx.NormalToLocal( ray.Forward );
		ray.Position = tx.PointToLocal( ray.Position );

		return ray;
	}

	/// <summary>Convert a ray from being local to this transform</summary>
	public readonly Ray ToWorld( in Transform tx )
	{
		var ray = this;
		ray.Forward = tx.NormalToWorld( ray.Forward );
		ray.Position = tx.PointToWorld( ray.Position );

		return ray;
	}

	/// <summary>Returns a point on the ray at given distance.</summary>
	public readonly Vector3 Project( float distance ) => Position + Forward * distance;

	private const float SafeMagnitude = 1e7f;

	//Returns a point on the ray at given safe distance.
	internal readonly Vector3 ProjectSafe( float distance )
	{
		distance = float.IsNaN( distance ) ? SafeMagnitude : distance;
		return Position + Forward * distance.Clamp( -SafeMagnitude, SafeMagnitude );
	}

	#region equality
	public static bool operator ==( Ray left, Ray right ) => left.Equals( right );
	public static bool operator !=( Ray left, Ray right ) => !(left == right);
	public readonly override bool Equals( object obj ) => obj is Ray o && Equals( o );
	public readonly bool Equals( Ray o ) => (_origin, _direction) == (o._origin, o._direction);
	public readonly override int GetHashCode() => HashCode.Combine( _origin, _direction );
	#endregion
}
