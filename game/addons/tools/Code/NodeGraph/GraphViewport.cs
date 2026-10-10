namespace Editor.NodeEditor;

//Only the arithmetic of it: nothing here knows what a panel is. The view owns one of these and writes its transform from what it says.
/// <summary>Where a graph is being looked at from, and how close.</summary>
public sealed class GraphViewport
{
	public const float MinZoom = 0.1f;
	public const float MaxZoom = 3.0f;

	/// <summary>Raised whenever the view has moved or scaled.</summary>
	public event Action Changed;

	private Vector2 _centre;
	private float _zoom = 1.0f;
	private Vector2 _size;

	/// <summary>The point of the graph the middle of the view is on.</summary>
	public Vector2 Centre
	{
		get => _centre;
		set
		{
			if ( _centre == value ) return;

			_centre = value;
			Changed?.Invoke();
		}
	}

	/// <summary>How many pixels one unit of the graph is drawn as.</summary>
	public float Zoom
	{
		get => _zoom;
		set
		{
			value = Math.Clamp( value, MinZoom, MaxZoom );

			if ( _zoom.AlmostEqual( value ) ) return;

			_zoom = value;
			Changed?.Invoke();
		}
	}

	/// <summary>How big the window onto the graph is, in pixels.</summary>
	public Vector2 Size
	{
		get => _size;
		set
		{
			if ( _size == value ) return;

			_size = value;
			Changed?.Invoke();
		}
	}

	/// <summary>Where a point of the view is in the graph.</summary>
	public Vector2 ToScene( Vector2 view ) => ((view - (_size * 0.5f)) / _zoom) + _centre;

	/// <summary>And the other way.</summary>
	public Vector2 FromScene( Vector2 scene ) => ((scene - _centre) * _zoom) + (_size * 0.5f);

	/// <summary>The part of the graph the view is showing.</summary>
	public Rect SceneRect
	{
		get
		{
			var topLeft = ToScene( 0.0f );

			return new Rect( topLeft, ToScene( _size ) - topLeft );
		}
	}

	//Paired with Zoom this is the whole transform: a point of the graph is drawn at scene * Zoom + Offset , which is a scale and then a translation and nothing else.
	/// <summary>Where the graph's origin lands, for the transform that draws it.</summary>
	public Vector2 Offset => (_size * 0.5f) - (_centre * _zoom);

	/// <summary>Slides the view by a distance measured on screen.</summary>
	public void Pan( Vector2 viewDelta )
	{
		if ( viewDelta.IsNearlyZero() ) return;

		Centre -= viewDelta / _zoom;
	}

	/// <summary>Closer or further out, about a point of the view that stays where it is.</summary>
	public void ZoomTo( float wanted, Vector2 viewAnchor )
	{
		var before = ToScene( viewAnchor );

		Zoom = wanted;

		Centre += before - ToScene( viewAnchor );
	}

	/// <summary>Look at this part of the graph, all of it, and no closer than it takes.</summary>
	public void FitInView( Rect scene, float margin = 0.0f )
	{
		scene = scene.Grow( margin );

		if ( scene.Width <= 0.0f || scene.Height <= 0.0f || _size.x <= 0.0f || _size.y <= 0.0f )
		{
			Centre = scene.Center;
			return;
		}

		Zoom = MathF.Min( _size.x / scene.Width, _size.y / scene.Height );
		Centre = scene.Center;
	}
}
