
using System.Buffers;

namespace Rinten;

public static partial class RintenSystemExtensions
{
	/// <summary>Generates a Catmull-Rom spline from a collection of Vector3 points. Needs at least 3 points to create a spline.</summary>
	public static IEnumerable<Vector3> CatmullRomSpline( this IEnumerable<Vector3> points, int interpolation )
	{
		Vector3 previousPoint = Vector3.Zero;
		Vector3 currentPoint = Vector3.Zero;
		Vector3 nextPoint = Vector3.Zero;
		Vector3 nextNextPoint = Vector3.Zero;

		int pointIndex = 0;
		foreach ( var point in points )
		{
			if ( pointIndex == 0 )
			{
				currentPoint = point;
				nextPoint = point;
			}
			else if ( pointIndex == 1 )
			{
				nextPoint = point;
				previousPoint = currentPoint; //Assuming a simple linear extrapolation for the first control point
			}
			else if ( pointIndex == 2 )
			{
				nextNextPoint = point;
				//Generate the first set of interpolated points now that we have enough points
				for ( int i = 0; i < interpolation; i++ )
				{
					float t = (float)i / interpolation;
					yield return Vector3.CatmullRomSpline( previousPoint, currentPoint, nextPoint, nextNextPoint, t );
				}
			}
			else
			{
				previousPoint = currentPoint;
				currentPoint = nextPoint;
				nextPoint = nextNextPoint;
				nextNextPoint = point;
				for ( int i = 0; i < interpolation; i++ )
				{
					float t = (float)i / interpolation;
					yield return Vector3.CatmullRomSpline( previousPoint, currentPoint, nextPoint, nextNextPoint, t );
				}
			}
			pointIndex++;
		}

		//Handle the last segment
		if ( pointIndex > 2 )
		{
			previousPoint = currentPoint;
			currentPoint = nextPoint;
			nextPoint = nextNextPoint;
			nextNextPoint = nextNextPoint + (nextNextPoint - nextPoint); //Assuming a simple linear extrapolation for the last control point

			for ( int i = 0; i <= interpolation; i++ )
			{
				float t = (float)i / interpolation;
				yield return Vector3.CatmullRomSpline( previousPoint, currentPoint, nextPoint, nextNextPoint, t );
			}
		}
	}

	/// <summary>Allocation-free TcbSpline into a caller span; returns points written (truncated if span is short)</summary>
	public static int TcbSpline( this IReadOnlyList<Vector3> points, Span<Vector3> into,
		int interpolation, float tension, float continuity, float bias )
	{
		var count = points?.Count ?? 0;
		if ( count < 3 || interpolation < 1 || into.IsEmpty ) return 0;

		//Read out of whatever holds them once, so the walk below indexes an array rather than an interface.
		var control = ArrayPool<Vector3>.Shared.Rent( count );

		try
		{
			for ( var i = 0; i < count; i++ ) control[i] = points[i];

			var written = 0;

			//The first segment is the one the enumerable form reaches at its third point,
			//with the first point standing in for the one before it - the same extrapolation.
			written = Along( into, written, control[0], control[0], control[1], control[2],
				interpolation, interpolation, tension, continuity, bias );

			for ( var i = 3; i < count; i++ )
				written = Along( into, written, control[i - 3], control[i - 2], control[i - 1], control[i],
					interpolation, interpolation, tension, continuity, bias );

			//And the last, which reaches its end: the point after the last is the last again, exactly as the enumerable form leaves it.
			written = Along( into, written, control[count - 3], control[count - 2], control[count - 1], control[count - 1],
				interpolation + 1, interpolation, tension, continuity, bias );

			return written;
		}
		finally
		{
			ArrayPool<Vector3>.Shared.Return( control );
		}
	}

	//One segment's points into the span, from where the last one left off.
	private static int Along( Span<Vector3> into, int at, in Vector3 p0, in Vector3 p1, in Vector3 p2, in Vector3 p3,
		int steps, int interpolation, float tension, float continuity, float bias )
	{
		var segment = Vector3.TcbSegment.Between( p0, p1, p2, p3, tension, continuity, bias );

		for ( var i = 0; i < steps && at < into.Length; i++ ) into[at++] = segment.At( (float)i / interpolation );

		return at;
	}

	//The cubic each segment is, worked out once for the segment rather than once for every point along it - see Vector3.TcbSegment. The
	//points are the same points to the bit. Where the caller has somewhere to put them, the span form above has no iterator in it at all.
	public static IEnumerable<Vector3> TcbSpline( this IEnumerable<Vector3> points, int interpolation, float tension, float continuity, float bias )
	{
		Vector3 previousPoint = Vector3.Zero;
		Vector3 currentPoint = Vector3.Zero;
		Vector3 nextPoint = Vector3.Zero;
		Vector3 nextNextPoint = Vector3.Zero;

		int pointIndex = 0;
		foreach ( var point in points )
		{
			if ( pointIndex == 0 )
			{
				currentPoint = point;
				nextPoint = point;
			}
			else if ( pointIndex == 1 )
			{
				nextPoint = point;
				previousPoint = currentPoint; //Assuming a simple linear extrapolation for the first control point
			}
			else if ( pointIndex == 2 )
			{
				nextNextPoint = point;

				//Generate the first set of interpolated points now that we have enough points
				var segment = Vector3.TcbSegment.Between( previousPoint, currentPoint, nextPoint, nextNextPoint, tension, continuity, bias );

				for ( int i = 0; i < interpolation; i++ )
				{
					float t = (float)i / interpolation;
					yield return segment.At( t );
				}
			}
			else
			{
				previousPoint = currentPoint;
				currentPoint = nextPoint;
				nextPoint = nextNextPoint;
				nextNextPoint = point;

				var segment = Vector3.TcbSegment.Between( previousPoint, currentPoint, nextPoint, nextNextPoint, tension, continuity, bias );

				for ( int i = 0; i < interpolation; i++ )
				{
					float t = (float)i / interpolation;
					yield return segment.At( t );
				}
			}
			pointIndex++;
		}

		//Handle the last segment
		if ( pointIndex > 2 )
		{
			previousPoint = currentPoint;
			currentPoint = nextPoint;
			nextPoint = nextNextPoint;
			nextNextPoint = nextNextPoint + (nextNextPoint - nextPoint); //Assuming a simple linear extrapolation for the last control point

			var segment = Vector3.TcbSegment.Between( previousPoint, currentPoint, nextPoint, nextNextPoint, tension, continuity, bias );

			for ( int i = 0; i <= interpolation; i++ )
			{
				float t = (float)i / interpolation;
				yield return segment.At( t );
			}
		}
	}
}
