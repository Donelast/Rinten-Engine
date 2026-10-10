using static Rinten.SceneCubemap;

namespace Rinten;

/// <summary>A cubemap probe that captures the environment around it.</summary>
[Expose]
[Title( "Envmap Probe" )]
[Category( "Light" )]
[Icon( "radio_button_unchecked" )]
[Alias( "EnvmapComponent" )]
public sealed partial class EnvmapProbe : Component, Component.ExecuteInEditor, Component.DontExecuteOnServer, ISceneLoadingEvents
{
	[Expose]
	public enum EnvmapProbeMode
	{
		[Icon( "🥖" )]
		Baked,

		[Icon( "📹" )]
		Realtime,

		[Icon( "📄" )]
		CustomTexture
	}

	[Expose]
	public enum CubemapResolution
	{
		[Title( "128x128" )]
		Small = 128,

		[Title( "256x256" )]
		Medium = 256,

		[Title( "512x512" )]
		Large = 512,

		[Title( "1024x1024" )]
		Huge = 1024
	}

	[Expose]
	public enum CubemapDynamicUpdate
	{
		/// <summary>Update once, when the cubemap is enabled</summary>
		OnEnabled,

		/// <summary>Update every frame (slow, not recommended)</summary>
		EveryFrame,

		/// <summary>Update every x frames</summary>
		FrameInterval,

		/// <summary>Update on a time based interval</summary>
		TimeInterval,
	}

	//Minimum seconds between captures of one probe; a cost cap, captures are expensive
	private const float LeastBetweenCaptures = 0.25f;

	private float _capturedAt;

	SceneCubemap _sceneObject;
	Texture _dynamicTexture;
	int _bouncesLeft;
	int _queuedFrames;
	float _queuedTime;

	public bool Dirty;

	[Property, EnumButtonGroup, WideMode( HasLabel = false )]
	public EnvmapProbeMode Mode
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;
			UpdateSceneObject();
		}
	}

	[Property, Space]
	public ProjectionMode Projection
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateSceneObject();
		}

	}
	[Property]
	public Color TintColor
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateSceneObject();
		}
	} = Color.White;

	[Property]
	public BBox Bounds
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateSceneObject();
		}
		//Twenty-six metres, which is the thousand and twenty-four units their editor defaulted to - and inches, as everything of theirs is. Left as
		//it came across it was a probe a kilometre across, covering the level and every other probe in it, which reads as "the box does nothing".
	} = BBox.FromPositionAndSize( 0, 26.0f );

	[Property, Range( -32.0f, 32.0f )]
	public float Feathering
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateSceneObject();
		}

		//Eight of their units, which is a fifth of a metre. A fade of eight metres on a box of twenty-six leaves nothing at full strength.
	} = 0.2f;

	/// <summary>Gets or sets the priority level for the object.</summary>
	[Property, Range( 0, 100 )]
	public int Priority
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			if ( _sceneObject.IsValid() )
			{
				_sceneObject.Priority = field;
			}
		}
	} = 0;

	/// <summary>If this is set, the EnvmapProbe will use a custom cubemap texture instead of rendering dynamically</summary>
	[Property]
	[ShowIf( nameof( Mode ), EnvmapProbeMode.CustomTexture )]
	public Texture Texture
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateSceneObject();
		}
	}

	/// <summary>The texture that was baked for this envmap probe</summary>
	[Property, Hide]
	public Texture BakedTexture { get; set; }

	[Obsolete( "Use Mode to select the update mode" ), Hide]
	public bool RenderDynamically
	{
		get => Mode == EnvmapProbeMode.Realtime;
		set => Mode = value ? EnvmapProbeMode.Realtime : EnvmapProbeMode.CustomTexture;
	}

	/// <summary>Resolution of the cubemap texture</summary>
	[Header( "Rendering" )]
	[HideIf( nameof( Mode ), EnvmapProbeMode.CustomTexture )]
	[Property]
	[EnumButtonGroup]
	public CubemapResolution Resolution
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateSceneObject();
		}
	} = CubemapResolution.Small;

	[HideIf( nameof( Mode ), EnvmapProbeMode.CustomTexture )]
	[Property]
	public float ZNear
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateSceneObject();
		}
	} = 0.4f;

	[HideIf( nameof( Mode ), EnvmapProbeMode.CustomTexture )]
	[Property]
	public float ZFar
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateSceneObject();
		}
	} = 104.0f;

	[Header( "Realtime Updates" )]
	[ShowIf( nameof( Mode ), EnvmapProbeMode.Realtime )]
	[Property]
	public CubemapDynamicUpdate UpdateStrategy
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateSceneObject();
		}
	}

	/// <summary>Only update dynamically if we're this close to it</summary>
	[ShowIf( nameof( Mode ), EnvmapProbeMode.Realtime )]
	[Property]
	public float MaxDistance { get; set; } = 13.0f;

	[ShowIf( nameof( Mode ), EnvmapProbeMode.Realtime )]
	[ShowIf( nameof( UpdateStrategy ), CubemapDynamicUpdate.TimeInterval )]
	[Property, Range( 0, 10 )]
	public float DelayBetweenUpdates { get; set; } = 0.1f;

	[ShowIf( nameof( Mode ), EnvmapProbeMode.Realtime )]
	[ShowIf( nameof( UpdateStrategy ), CubemapDynamicUpdate.FrameInterval )]
	[Property, Range( 0, 16 )]
	public int FrameInterval { get; set; } = 5;

	/// <summary>Minimum amount of reflection bounces to render when first enabled before settling, at cost of extra performance on load Often times you don't need this</summary>
	[HideIf( nameof( Mode ), EnvmapProbeMode.CustomTexture )]
	[ShowIf( nameof( UpdateStrategy ), CubemapDynamicUpdate.OnEnabled )]
	[Property]
	public bool MultiBounce
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateSceneObject();
		}
	} = false;

	/// <summary>Objects with any of these tags will be excluded when rendering this cubemap.</summary>
	[HideIf( nameof( Mode ), EnvmapProbeMode.CustomTexture )]
	[Property]
	public TagSet RenderExcludeTags { get; set; } = new();

	protected override void OnEnabled()
	{
		Assert.True( !_sceneObject.IsValid() );
		Assert.NotNull( Scene );

		_sceneObject = new SceneCubemap( Scene.SceneWorld, null, Bounds, WorldTransform, TintColor, Feathering, (int)Projection );
		_sceneObject.Tags.SetFrom( Tags );
		_sceneObject.Priority = Priority;

		Transform.OnTransformChanged += OnTransformChanged;
		UpdateSceneObject();

		//A probe that has never been drawn reflects nothing, and "nothing" is black rather than
		//absent - so the first capture is not something to leave until a strategy asks for it.
		if ( CoreNative.Available && Mode != EnvmapProbeMode.CustomTexture )
			Dirty = true;
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= OnTransformChanged;

		CoreEnvmap.Forget( this );

		Dirty = false;

		_sceneObject?.Delete();
		_sceneObject = null;

		_dynamicTexture?.Dispose();
		_dynamicTexture = null;
	}

	//First capture after loading finishes, so the probe sees the complete world and not a partial one
	void ISceneLoadingEvents.AfterLoad( Scene scene )
	{
		if ( Application.IsHeadless ) return;
		if ( Mode == EnvmapProbeMode.CustomTexture ) return;
		if ( !Dirty ) return;

		Capture( wait: true );
	}

	//Renders the six views now and folds them now or on a worker; not callable from inside a frame
	internal bool Capture( bool wait = false )
	{
		Dirty = false;

		if ( _sceneObject.IsValid() ) _sceneObject.RequiresUpdate = false;

		_queuedFrames = 0;
		_queuedTime = 0;

		if ( _bouncesLeft > 0 && UpdateStrategy == CubemapDynamicUpdate.OnEnabled )
			_bouncesLeft--;

		if ( Mode == EnvmapProbeMode.CustomTexture ) return false;
		if ( !Scene.IsValid() ) return false;

		return CoreEnvmap.Capture( Scene, this, wait );
	}

	void OnTransformChanged()
	{
		UpdateSceneObject();
	}

	void UpdateSceneObject()
	{
		if ( !_sceneObject.IsValid() )
			return;

		var tx = WorldTransform;
		var bounds = Bounds;

		if ( Mode == EnvmapProbeMode.Realtime )
		{
			tx = tx.WithScale( -1 );
			bounds = new BBox( -Bounds.Maxs, -Bounds.Mins );
		}

		_sceneObject.Transform = tx;
		_sceneObject.Projection = Projection;
		_sceneObject.TintColor = TintColor;
		_sceneObject.ProjectionBounds = bounds;
		_sceneObject.LocalBounds = _sceneObject.ProjectionBounds;
		_sceneObject.Feathering = Feathering;

		//Update bounce count when strategy or multibounce changes
		if ( UpdateStrategy == CubemapDynamicUpdate.OnEnabled )
		{
			_bouncesLeft = MultiBounce ? 4 : 0;
		}

		if ( Mode == EnvmapProbeMode.Baked )
		{
			_sceneObject.Texture = BakedTexture;
		}
		else if ( Mode == EnvmapProbeMode.CustomTexture )
		{
			_sceneObject.Texture = Texture;
		}
		else if ( Mode == EnvmapProbeMode.Realtime )
		{
			CreateTexture();
			_sceneObject.Texture = _dynamicTexture;
		}
	}

	//Tags have been updated - lets update our tags
	protected override void OnTagsChanged()
	{
		if ( _sceneObject.IsValid() )
		{
			_sceneObject.Tags.SetFrom( Tags );
			_sceneObject.RenderDirty();
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		TryToDirty();
	}

	void TryToDirty()
	{
		//Captures happen in the tick, outside the frame; a capture in progress advances one face at a time
		if ( CoreEnvmap.Capturing( this ) )
		{
			CoreEnvmap.Step( Scene, this );
			return;
		}

		//The light coming into the room changed - the sun moved or the sky turned another colour - since
		//the picture was taken: a picture of the room under that light is owed. See CoreEnvmap.Outdated.
		if ( !Dirty && CoreNative.Available && Mode != EnvmapProbeMode.CustomTexture && CoreEnvmap.Outdated( Scene, this ) )
			Dirty = true;

		if ( Dirty && CoreNative.Available )
		{
			//Throttle captures; each costs tens of milliseconds, so EveryFrame can't be honoured literally
			if ( _capturedAt > 0.0f && RealTime.Now - _capturedAt < LeastBetweenCaptures )
				return;

			//And not in the same tick as another probe's: a sky that changed asks every probe at once - see CoreEnvmap.MayCapture.
			if ( !CoreEnvmap.MayCapture ) return;

			_capturedAt = RealTime.Now;

			Capture();
			return;
		}

		if ( Mode != EnvmapProbeMode.Realtime )
		{
			//Reset counters when not rendering dynamically
			_queuedFrames = 0;
			_queuedTime = 0;
			return;
		}

		//Update counters
		_queuedFrames++;
		_queuedTime += Time.Delta;

		if ( !IsReadyToUpdate() )
			return;

		Dirty = true;
	}

	internal bool IsReadyToUpdate()
	{
		if ( UpdateStrategy == CubemapDynamicUpdate.EveryFrame )
			return true;

		if ( UpdateStrategy == CubemapDynamicUpdate.FrameInterval && _queuedFrames > FrameInterval )
			return true;

		if ( UpdateStrategy == CubemapDynamicUpdate.TimeInterval && _queuedTime > DelayBetweenUpdates )
			return true;

		//If it's dirty, always update even if we're render once
		if ( _sceneObject?.RequiresUpdate ?? false )
			return true;

		if ( UpdateStrategy == CubemapDynamicUpdate.OnEnabled && _bouncesLeft > 0 )
			return true;

		return false;
	}

	void CreateTexture()
	{
		var cubemapSize = (int)Resolution;
		var numMips = 7; //Cubemapper is calibrated for 7 mipmaps

		if ( _dynamicTexture is not null && _dynamicTexture.Width == cubemapSize && _dynamicTexture.UAVAccess )
			return;

		//Dispose old texture if it exists
		_dynamicTexture?.Dispose();

		_dynamicTexture = Texture.CreateCube( cubemapSize, cubemapSize )
							.WithUAVBinding()
							.WithMips( numMips )
							.WithFormat( ImageFormat.RGBA16161616F )
							.Finish();
	}

	internal void RenderCubemap()
	{
		//Our renderer captures from the tick, not inside the frame; below is the native-renderer path
		if ( CoreNative.Available )
		{
			Dirty = true;
			return;
		}

		RenderCubemap( _dynamicTexture, CubemapRendering.GGXFilterType.Fast, RenderExcludeTags );
	}

	internal void RenderCubemap( Texture target, CubemapRendering.GGXFilterType filterType, TagSet excludeTags = null )
	{
		//Cleared whatever happens, including on the way out below. Left set it means "still to be rendered",
		//and everything that waits on it waits for ever - which is exactly what the loading screen did.
		Dirty = false;

		if ( target is null )
			return;

		target.Clear( Color.Red );

		if ( target.UAVAccess )
		{
			CubemapRendering.Render( Scene.SceneWorld, target, WorldTransform.WithScale( 1 ), ZNear.Clamp( 1, ZFar ), ZFar.Clamp( ZNear, 1024 * 16 ), filterType, excludeTags );
		}

		//Just finished rendering, signal to component that we're done
		_sceneObject?.RequiresUpdate = false;

		//Reset counters after rendering
		_queuedFrames = 0;
		_queuedTime = 0;

		if ( _bouncesLeft > 0 && UpdateStrategy == CubemapDynamicUpdate.OnEnabled )
			_bouncesLeft--;

		Dirty = false;
	}
}
