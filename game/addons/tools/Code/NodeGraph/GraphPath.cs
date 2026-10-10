namespace Editor.NodeEditor;

//A curve is kept as the short straight lengths it is made of, which is what a curve is by the time it reaches the screen anyway. Keeping it that way means hit testing and drawing read the same list.
/// <summary>The line a connection follows, in graph units.</summary>
public sealed class GraphPath
{
	//How many lengths one cubic section is drawn as.
	private const int CurveSteps = 16;

	private readonly List<Vector2> _points = new();

	private Rect _bounds;
	private bool _boundsValid;

	public IReadOnlyList<Vector2> Points => _points;

	public int Count => _points.Count;

	/// <summary>The smallest rectangle the whole path fits in.</summary>
	public Rect Bounds
	{
		get
		{
			if ( _boundsValid ) return _bounds;

			_boundsValid = true;
			_bounds = default;

			if ( _points.Count == 0 ) return _bounds;

			_bounds = new Rect( _points[0] );

			for ( var i = 1; i < _points.Count; i++ )
			{
				_bounds = _bounds.AddPoint( _points[i] );
			}

			return _bounds;
		}
	}

	public void Clear()
	{
		_points.Clear();
		_boundsValid = false;
	}

	public void MoveTo( Vector2 point )
	{
		_points.Clear();
		_points.Add( point );
		_boundsValid = false;
	}

	public void LineTo( Vector2 point )
	{
		_points.Add( point );
		_boundsValid = false;
	}

	/// <summary>A cubic section, added as the lengths it is made of.</summary>
	public void CubicLineTo( Vector2 c1, Vector2 c2, Vector2 point )
	{
		var from = _points.Count > 0 ? _points[^1] : point;

		for ( var i = 1; i <= CurveSteps; i++ )
		{
			var t = i / (float)CurveSteps;
			var n = 1.0f - t;

			_points.Add(
				(n * n * n * from) +
				(3 * n * n * t * c1) +
				(3 * n * t * t * c2) +
				(t * t * t * point) );
		}

		_boundsValid = false;
	}

	/// <summary>Whether a point is on the wire itself, not just inside its bounding box</summary>
	public bool IsNear( Vector2 point, float reach )
	{
		if ( _points.Count == 0 ) return false;

		var reachSquared = reach * reach;

		if ( _points.Count == 1 ) return _points[0].DistanceSquared( point ) <= reachSquared;

		for ( var i = 1; i < _points.Count; i++ )
		{
			if ( DistanceSquaredToSegment( point, _points[i - 1], _points[i] ) <= reachSquared ) return true;
		}

		return false;
	}

	//How far a point is off a length, squared - no root taken, because it is only ever compared against another squared length.
	private static float DistanceSquaredToSegment( Vector2 point, Vector2 from, Vector2 to )
	{
		var along = to - from;
		var lengthSquared = along.LengthSquared;

		//The two ends in the same place: the length is a point.
		if ( lengthSquared <= 0.0001f ) return from.DistanceSquared( point );

		var t = Math.Clamp( Vector2.Dot( point - from, along ) / lengthSquared, 0.0f, 1.0f );

		return (from + (along * t)).DistanceSquared( point );
	}
}
