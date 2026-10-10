namespace Rinten;

/// <summary>A cube map: what a place reflects, and how far its reflection reaches.</summary>
[Expose]
public sealed class SceneCubemap : SceneObject
{
	public enum ProjectionMode
	{
		[Icon( "panorama_photosphere" )]
		Sphere = 0,

		[Icon( "check_box_outline_blank" )]
		Box = 1,
	}

	internal SceneCubemap( HandleCreationData d ) : base( d )
	{
	}

	//Twenty-five metres. A thousand of their units, which are inches.
	public SceneCubemap( SceneWorld sceneWorld ) : this( sceneWorld, default, BBox.FromPositionAndSize( 0, 25.0f ) )
	{
	}

	public SceneCubemap( SceneWorld sceneWorld, Texture texture, BBox bounds ) : this( sceneWorld, texture, bounds, Transform.Zero, Color.White, 0.25f, 1 )
	{
	}

	internal SceneCubemap( SceneWorld sceneWorld, Texture texture, BBox bounds, Transform transform, Color tint, float feathering, int projectionMode = 0 ) : base()
	{
		Assert.IsValid( sceneWorld );

		JoinWorld( sceneWorld );

		_tint = tint;
		_feathering = feathering;
		_bounds = bounds;
		_projection = (ProjectionMode)projectionMode;
		_texture = texture;

		Transform = transform;

		RenderDirty();
	}

	private int _priority;
	private ProjectionMode _projection;
	private Color _tint = Color.White;
	private float _feathering = 0.25f; //metres
	private BBox _bounds;
	private Texture _texture;

	public int Priority
	{
		get => _priority;
		set
		{
			if ( _priority == value )
				return;

			_priority = value;
			RenderDirty();
		}
	}

	public ProjectionMode Projection
	{
		get => _projection;
		set
		{
			if ( _projection == value )
				return;

			_projection = value;
			RenderDirty();
		}
	}

	public Color TintColor
	{
		get => _tint;
		set
		{
			if ( _tint == value )
				return;

			_tint = value;
			RenderDirty();
		}
	}

	public float Feathering
	{
		get => _feathering;
		set
		{
			if ( _feathering == value )
				return;

			_feathering = value;
			RenderDirty();
		}
	}

	public BBox ProjectionBounds
	{
		get => _bounds;
		set
		{
			if ( _bounds == value )
				return;

			_bounds = value;
			RenderDirty();
		}
	}

	internal override void OnTransformChanged( in Transform tx )
	{
		base.OnTransformChanged( tx );
		RenderDirty();
	}

	public Texture Texture
	{
		get => _texture;
		set
		{
			if ( _texture == value )
				return;

			_texture = value;
			RenderDirty();
		}
	}

	/// <summary>Marks the cubemap as dirty, to be re-rendered on the next render.</summary>
	public void RenderDirty()
	{
		RequiresUpdate = true;
	}

	internal bool RequiresUpdate = true;
}
