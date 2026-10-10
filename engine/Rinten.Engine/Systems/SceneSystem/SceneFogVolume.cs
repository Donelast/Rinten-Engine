namespace Rinten;

/// <summary>Represents a volume of fog in a scene, contributing to volumetric fog effects set on <see cref="SceneCamera.VolumetricFog"/>.</summary>
public sealed class SceneFogVolume : IValid
{
	uint? ID { get; set; }
	SceneWorld World { get; set; }

	Transform transform;

	/// <summary>The position and rotation of the fog volume in the scene.</summary>
	public Transform Transform
	{
		get => transform;
		set
		{
			if ( transform == value ) return;
			transform = value;
			Update();
		}
	}

	BBox boundingBox;

	/// <summary>Defines the spatial boundaries of the fog volume.</summary>
	public BBox BoundingBox
	{
		get => boundingBox;
		set
		{
			if ( boundingBox == value ) return;

			boundingBox = value;
			Update();
		}
	}

	float fogStrength;

	/// <summary>The intensity of the fog. Higher values indicate denser fog.</summary>
	public float FogStrength
	{
		get => fogStrength;
		set
		{
			if ( fogStrength == value )
				return;

			fogStrength = value;
			Update();
		}
	}

	float falloffExponent;

	/// <summary>Controls how quickly the fog fades at the edges of the volume. Higher values give sharper transitions.</summary>
	public float FalloffExponent
	{
		get => falloffExponent;
		set { falloffExponent = value; Update(); }
	}

	Color color = Color.White;

	/// <summary>Tint applied to the in-scattered light inside this fog volume.</summary>
	public Color Color
	{
		get => color;
		set { color = value; Update(); }
	}

	static uint nextId = 1;

	public SceneFogVolume( SceneWorld world, Transform transform, BBox boundingBox, float fogStrength = 1.0f, float falloffExponent = 1.0f )
	{
		World = world;

		//set the backing fields to not trigger Update() for each
		this.transform = transform;
		this.boundingBox = boundingBox;
		this.fogStrength = fogStrength;
		this.falloffExponent = falloffExponent;

		//The world's list is what the renderer walks - see CoreSceneRenderer.SetFog - so being on it is what makes this real.
		if ( world.IsValid() )
		{
			ID = nextId++;
			world.FogVolumes.Add( this );
		}
	}

	public bool IsValid => ID.HasValue && World.IsValid();

	/// <summary>Delete this fog volume. You shouldn't access it anymore.</summary>
	public void Delete()
	{
		if ( !ID.HasValue )
			return;

		if ( World.IsValid() )
			World.FogVolumes.Remove( this );

		ID = default;
	}

	//Nothing to push: the renderer reads this object's fields off the world's list each frame, so a changed field is seen the next time it is drawn.
	void Update()
	{
	}
}
