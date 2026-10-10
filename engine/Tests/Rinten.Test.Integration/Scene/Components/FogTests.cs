using System.Reflection;

namespace SceneTests.Components;

[TestClass]
public class FogComponentTest
{
	//Serializes a GameObject to json, destroys the original, then deserializes the json back into the
	//scene and enables it - the standard save/load round trip idiom used by the integration tests.
	static GameObject SerializeRoundTrip( Scene scene, GameObject go )
	{
		var json = go.Serialize().ToJsonString();

		go.Destroy();
		scene.ProcessDeletes();

		var jsonObject = Json.ParseToJsonObject( json );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var clone = new GameObject( false );
		clone.Deserialize( jsonObject );
		clone.Enabled = true;

		return clone;
	}

	//Reads the component's private SceneFogVolume via reflection; it isn't in SceneWorld.SceneObjects
	static SceneFogVolume GetFogSceneObject( VolumetricFogVolume component )
	{
		var field = typeof( VolumetricFogVolume ).GetField( "sceneObject", BindingFlags.Instance | BindingFlags.NonPublic );
		return (SceneFogVolume)field.GetValue( component );
	}

	//CubemapFog is the gradient fog coloured by the sky: it writes the world's fog with its distances and heights and
	//says the colour comes from the sky. No height band is fog at every height - a band far above anything there is.
	[TestMethod]
	public void CubemapFogWritesSkyColouredWorldFog()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var fog = go.Components.Create<CubemapFog>();
		fog.StartDistance = 25.0f;
		fog.EndDistance = 1000.0f;
		fog.FalloffExponent = 2.0f;
		fog.Tint = new Color( 0.5f, 0.5f, 0.5f, 0.75f );

		scene.PreCameraRender();

		var world = scene.SceneWorld.GradientFog;

		Assert.IsTrue( world.Enabled );
		Assert.IsTrue( world.ColorFromSky, "The colour comes from the sky" );
		Assert.AreEqual( 25.0f, world.StartDistance );
		Assert.AreEqual( 1000.0f, world.EndDistance );
		Assert.AreEqual( 2.0f, world.DistanceFalloffExponent );
		Assert.AreEqual( 0.75f, world.MaximumOpacity, 0.001f, "The tint's alpha is how thick it gets" );
		Assert.IsTrue( world.StartHeight > 1e8f, "No height band is fog at every height" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//A CubemapFog with non-default distances, falloff, height setup and tint survives a serialize/deserialize round trip.
	[TestMethod]
	public void CubemapFogSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var fog = go.Components.Create<CubemapFog>();
		fog.StartDistance = 32.0f;
		fog.EndDistance = 2048.0f;
		fog.FalloffExponent = 2.0f;
		fog.HeightWidth = 100.0f;
		fog.HeightStart = 500.0f;
		fog.HeightExponent = 3.0f;
		fog.Tint = new Color( 0.1f, 0.2f, 0.3f, 0.4f );

		var clone = SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<CubemapFog>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a CubemapFog" );
		Assert.AreEqual( 32.0f, loaded.StartDistance );
		Assert.AreEqual( 2048.0f, loaded.EndDistance );
		Assert.AreEqual( 2.0f, loaded.FalloffExponent );
		Assert.AreEqual( 100.0f, loaded.HeightWidth );
		Assert.AreEqual( 500.0f, loaded.HeightStart );
		Assert.AreEqual( 3.0f, loaded.HeightExponent );
		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f, 0.4f ), loaded.Tint );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	//Enabling registers a fog volume (color stays white until pre-render); disable/destroy remove it
	[TestMethod]
	public void VolumetricFogVolumeSceneObjectLifecycle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 50, 60, 70 );

		var volume = go.Components.Create<VolumetricFogVolume>( false );

		Assert.AreEqual( BBox.FromPositionAndSize( 0, 300 ), volume.Bounds, "Default bounds are a 300 unit box" );
		Assert.AreEqual( 1.0f, volume.Strength );
		Assert.AreEqual( 1.0f, volume.FalloffExponent );
		Assert.AreEqual( Color.White, volume.Color );
		Assert.IsNull( GetFogSceneObject( volume ), "No scene fog volume should exist while disabled" );

		var bounds = new BBox( new Vector3( -10, -20, -30 ), new Vector3( 10, 20, 30 ) );
		volume.Bounds = bounds;
		volume.Strength = 0.5f;
		volume.FalloffExponent = 0.75f;
		volume.Color = Color.Red;

		volume.Enabled = true;

		var so = GetFogSceneObject( volume );
		Assert.IsNotNull( so, "Enabling should create the SceneFogVolume" );
		Assert.IsTrue( so.IsValid );
		Assert.AreEqual( bounds, so.BoundingBox );
		Assert.AreEqual( 0.5f, so.FogStrength );
		Assert.AreEqual( 0.75f, so.FalloffExponent );
		Assert.AreEqual( Color.White, so.Color, "The constructor does not take the color - it stays white until pre-render" );
		Assert.IsTrue( so.Transform.Position.Distance( new Vector3( 50, 60, 70 ) ) < 0.01f );

		scene.GameTick();

		Assert.AreEqual( Color.Red, so.Color, "The first pre-render copies the component color" );

		volume.Enabled = false;

		Assert.IsFalse( so.IsValid, "Disabling should delete the scene fog volume" );
		Assert.IsNull( GetFogSceneObject( volume ) );

		volume.Enabled = true;

		var second = GetFogSceneObject( volume );
		Assert.IsNotNull( second, "Re-enabling should create a new scene fog volume" );
		Assert.AreNotSame( so, second );
		Assert.IsTrue( second.IsValid );

		go.Destroy();
		scene.ProcessDeletes();

		Assert.IsFalse( second.IsValid, "Destroying the GameObject should delete the scene fog volume" );
	}

	//VolumetricFogVolume copies its transform, bounds, strength, falloff and color to the SceneFogVolume
	//every pre-render, so property changes and GameObject moves are visible on the scene object after a tick.
	[TestMethod]
	public void VolumetricFogVolumePropertyPropagation()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var volume = go.Components.Create<VolumetricFogVolume>();
		var so = GetFogSceneObject( volume );

		var bounds = new BBox( new Vector3( -16, -32, -48 ), new Vector3( 16, 32, 48 ) );
		volume.Bounds = bounds;
		volume.Strength = 0.25f;
		volume.FalloffExponent = 0.5f;
		volume.Color = new Color( 0.1f, 0.2f, 0.3f );
		go.WorldPosition = new Vector3( 100, 0, 0 );

		scene.GameTick();

		Assert.AreEqual( bounds, so.BoundingBox, "Bounds should propagate on pre-render" );
		Assert.AreEqual( 0.25f, so.FogStrength );
		Assert.AreEqual( 0.5f, so.FalloffExponent );
		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f ), so.Color );
		Assert.IsTrue( so.Transform.Position.Distance( new Vector3( 100, 0, 0 ) ) < 0.01f, "The scene fog volume follows the GameObject" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//Any enabled fog volume turns on camera volumetrics with fixed params; only when clearing color
	[TestMethod]
	public void VolumetricFogVolumeDrivesCameraVolumetrics()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var camGo = scene.CreateObject();
		var cam = camGo.Components.Create<CameraComponent>();
		var sceneCamera = cam.SceneCamera;

		cam.CopyPostProcessing( sceneCamera );

		Assert.IsFalse( sceneCamera.VolumetricFog.Enabled, "No fog volumes means volumetrics stay off" );
		Assert.AreEqual( 4096.0f, sceneCamera.VolumetricFog.DrawDistance );
		Assert.AreEqual( 64.0f, sceneCamera.VolumetricFog.FadeInStart );
		Assert.AreEqual( 256.0f, sceneCamera.VolumetricFog.FadeInEnd );
		Assert.AreEqual( 1.0f, sceneCamera.VolumetricFog.IndirectStrength );
		Assert.AreEqual( 1.0f, sceneCamera.VolumetricFog.Anisotropy );
		Assert.AreEqual( 1.0f, sceneCamera.VolumetricFog.Scattering );
		Assert.IsNull( sceneCamera.VolumetricFog.BakedIndirectTexture, "No controller means no baked texture" );

		var go = scene.CreateObject();
		var volume = go.Components.Create<VolumetricFogVolume>();

		cam.CopyPostProcessing( sceneCamera );
		Assert.IsTrue( sceneCamera.VolumetricFog.Enabled, "An enabled fog volume turns volumetrics on" );

		volume.Enabled = false;
		cam.CopyPostProcessing( sceneCamera );
		Assert.IsFalse( sceneCamera.VolumetricFog.Enabled, "Disabled volumes are not counted" );

		volume.Enabled = true;
		cam.ClearFlags = ClearFlags.Depth;
		cam.CopyPostProcessing( sceneCamera );
		Assert.IsFalse( sceneCamera.VolumetricFog.Enabled, "Without a color clear the volumetric block is skipped, leaving the previous state" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//Multiple fog controllers coexist; the camera uses the first enabled one in registration order
	[TestMethod]
	public void VolumetricFogControllerRegistrationSemantics()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var camGo = scene.CreateObject();
		var cam = camGo.Components.Create<CameraComponent>();
		var sceneCamera = cam.SceneCamera;

		var goA = scene.CreateObject();
		var controllerA = goA.Components.Create<VolumetricFogController>();
		controllerA.BakedFogTexture = Texture.White;
		controllerA.GlobalScale = 2.0f;

		var goB = scene.CreateObject();
		var controllerB = goB.Components.Create<VolumetricFogController>();
		controllerB.BakedFogTexture = Texture.Transparent;

		Assert.AreEqual( 2, scene.GetAllComponents<VolumetricFogController>().Count(), "Runtime-created controllers are not deduplicated" );

		cam.CopyPostProcessing( sceneCamera );
		Assert.AreEqual( Texture.White, sceneCamera.VolumetricFog.BakedIndirectTexture, "The first registered controller wins" );

		controllerA.Enabled = false;
		cam.CopyPostProcessing( sceneCamera );
		Assert.AreEqual( Texture.Transparent, sceneCamera.VolumetricFog.BakedIndirectTexture, "Disabling the first controller promotes the next one" );

		goA.Destroy();
		goB.Destroy();
		scene.ProcessDeletes();
	}

	//A VolumetricFogVolume's bounds, strength, falloff exponent and color survive a serialize/deserialize round
	//trip, and the enabled clone registers a live SceneFogVolume carrying the loaded values once a tick has run.
	[TestMethod]
	public void VolumetricFogVolumeSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var bounds = new BBox( new Vector3( -64, -32, -16 ), new Vector3( 64, 32, 16 ) );

		var go = scene.CreateObject();
		var volume = go.Components.Create<VolumetricFogVolume>();
		volume.Bounds = bounds;
		volume.Strength = 0.25f;
		volume.FalloffExponent = 0.75f;
		volume.Color = new Color( 0.1f, 0.2f, 0.3f, 0.4f );

		var clone = SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<VolumetricFogVolume>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a VolumetricFogVolume" );
		Assert.AreEqual( bounds, loaded.Bounds );
		Assert.AreEqual( 0.25f, loaded.Strength );
		Assert.AreEqual( 0.75f, loaded.FalloffExponent );
		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f, 0.4f ), loaded.Color );

		var so = GetFogSceneObject( loaded );
		Assert.IsNotNull( so, "The enabled clone should register its scene fog volume" );
		Assert.IsTrue( so.IsValid );
		Assert.AreEqual( bounds, so.BoundingBox );
		Assert.AreEqual( 0.25f, so.FogStrength );

		scene.GameTick();

		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f, 0.4f ), so.Color, "Pre-render copies the loaded color" );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	//SceneFogVolume surface; quirk: setting a property after Delete re-registers the native volume
	[TestMethod]
	public void SceneFogVolumeDirectLifecycle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var bounds = BBox.FromPositionAndSize( 0, 100 );
		var so = new SceneFogVolume( scene.SceneWorld, Transform.Zero.WithPosition( new Vector3( 1, 2, 3 ) ), bounds, 0.75f, 2.0f );

		Assert.IsTrue( so.IsValid, "Construction should register a valid fog volume" );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), so.Transform.Position );
		Assert.AreEqual( bounds, so.BoundingBox );
		Assert.AreEqual( 0.75f, so.FogStrength );
		Assert.AreEqual( 2.0f, so.FalloffExponent );
		Assert.AreEqual( Color.White, so.Color, "Color defaults to white" );

		var newBounds = new BBox( new Vector3( -50, -50, 0 ), new Vector3( 50, 50, 100 ) );
		so.BoundingBox = newBounds;
		so.FogStrength = 0.5f;
		so.FalloffExponent = 3.0f;
		so.Color = Color.Blue;
		so.Transform = Transform.Zero.WithPosition( new Vector3( 9, 8, 7 ) );

		Assert.AreEqual( newBounds, so.BoundingBox );
		Assert.AreEqual( 0.5f, so.FogStrength );
		Assert.AreEqual( 3.0f, so.FalloffExponent );
		Assert.AreEqual( Color.Blue, so.Color );
		Assert.AreEqual( new Vector3( 9, 8, 7 ), so.Transform.Position );
		Assert.IsTrue( so.IsValid, "Property updates keep the volume valid" );

		so.Delete();

		Assert.IsFalse( so.IsValid, "Delete should invalidate the volume" );

		so.Delete();

		Assert.IsFalse( so.IsValid, "Double delete is safe" );

		so.FogStrength = 0.9f;

		Assert.IsTrue( so.IsValid, "A property write after Delete re-registers the native volume" );

		so.Delete();

		Assert.IsFalse( so.IsValid );
	}
}
