using System.Collections.Generic;
using Rinten.Rendering;
using Rinten.Volumes;

namespace SceneTests.Components;

[TestClass]
public class PostProcessingComponentTest
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

	//Finds the post process layer the given camera holds for a stage by debug name, using the internal PostProcessLayers
	//storage that BasePostProcess.InsertCommandList writes into. Returns null when no such layer exists.
	static PostProcessLayer FindLayer( CameraComponent cam, Stage stage, string name )
	{
		if ( !cam.PostProcess.Layers.TryGetValue( stage, out var list ) )
			return null;

		return list.FirstOrDefault( x => x.Name == name );
	}

	//Counts every post process layer registered on the camera across all stages. Only BasePostProcess.InsertCommandList
	//creates these layers, so the count is an exact measure of how many effects built themselves this tick.
	static int TotalLayerCount( CameraComponent cam )
	{
		return cam.PostProcess.Layers.Sum( x => x.Value.Count );
	}

	//Creates the component on a fresh GameObject, cycles it disabled and enabled again, then destroys
	//the GameObject - the construction / lifecycle smoke test applied to every post processing component.
	static void CreateAndCycle<T>( Scene scene ) where T : Component, new()
	{
		var go = scene.CreateObject();
		var effect = go.Components.Create<T>();

		Assert.IsTrue( effect.IsValid(), $"{typeof( T ).Name} should construct" );
		Assert.IsTrue( effect.Enabled, $"{typeof( T ).Name} should be enabled after create" );

		effect.Enabled = false;
		Assert.IsFalse( effect.Enabled, $"{typeof( T ).Name} should disable safely" );

		effect.Enabled = true;
		Assert.IsTrue( effect.Enabled, $"{typeof( T ).Name} should re-enable safely" );

		go.Destroy();
		scene.ProcessDeletes();

		Assert.IsFalse( effect.IsValid(), $"{typeof( T ).Name} should be destroyed with its GameObject" );
	}

	//Every post-process component survives construct, enable cycles and destroy without scene objects
	[TestMethod]
	public void EveryEffectConstructsAndSurvivesEnableCycles()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var baseline = scene.SceneWorld.SceneObjects.Count();

		CreateAndCycle<AmbientOcclusion>( scene );
		CreateAndCycle<Bloom>( scene );
		CreateAndCycle<BlitOverlay>( scene );
		CreateAndCycle<Blur>( scene );
		CreateAndCycle<ChromaticAberration>( scene );
		CreateAndCycle<ColorAdjustments>( scene );
		CreateAndCycle<ColorGrading>( scene );
		CreateAndCycle<DepthOfField>( scene );
		CreateAndCycle<FilmGrain>( scene );
		CreateAndCycle<Highlight>( scene );
		CreateAndCycle<HighlightOutline>( scene );
		CreateAndCycle<MotionBlur>( scene );
		CreateAndCycle<Pixelate>( scene );
		CreateAndCycle<ScreenSpaceReflections>( scene );
		CreateAndCycle<Sharpen>( scene );
		CreateAndCycle<Tonemapping>( scene );
		CreateAndCycle<Vignette>( scene );
		CreateAndCycle<PostProcessVolume>( scene );

		Assert.AreEqual( baseline, scene.SceneWorld.SceneObjects.Count(), "Post process components should not create scene objects" );
	}

	//Pins the default property values of every post process effect exactly as the implementations declare them, so accidental default changes are caught.
	[TestMethod]
	public void EffectDefaultValuesArePinned()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var ao = go.Components.Create<AmbientOcclusion>( false );
		Assert.AreEqual( 1.0f, ao.Intensity );

		var bloom = go.Components.Create<Bloom>( false );
		Assert.AreEqual( Rinten.Bloom.BloomMode.Additive, bloom.Mode );
		Assert.AreEqual( 1.0f, bloom.Strength );
		Assert.AreEqual( 1.0f, bloom.Threshold );
		Assert.AreEqual( Color.White, bloom.Tint );

		var overlay = go.Components.Create<BlitOverlay>( false );
		Assert.AreEqual( 0.1f, overlay.Blend );
		Assert.AreEqual( BlendMode.Normal, overlay.BlendMode );
		Assert.IsNull( overlay.Material );
		Assert.AreEqual( 0, overlay.Order );

		var blur = go.Components.Create<Blur>( false );
		Assert.AreEqual( 1.0f, blur.Size );

		var ca = go.Components.Create<ChromaticAberration>( false );
		Assert.AreEqual( 0.33f, ca.Scale );
		Assert.AreEqual( new Vector3( 6, 2, 4 ), ca.Offset );

		var adjust = go.Components.Create<ColorAdjustments>( false );
		Assert.AreEqual( 1.0f, adjust.Blend );
		Assert.AreEqual( 1.0f, adjust.Saturation );
		Assert.AreEqual( 0.0f, adjust.HueRotate );
		Assert.AreEqual( 1.0f, adjust.Brightness );
		Assert.AreEqual( 1.0f, adjust.Contrast );

		var grading = go.Components.Create<ColorGrading>( false );
		Assert.AreEqual( ColorGrading.GradingType.None, grading.GradingMethod );
		Assert.AreEqual( 6500.0f, grading.ColorTempK );
		Assert.AreEqual( 1.0f, grading.BlendFactor );
		Assert.AreEqual( Texture.White, grading.LookupTexture );
		Assert.AreEqual( ColorGrading.ColorSpaceEnum.None, grading.ColorSpace );
		Assert.AreEqual( 2, grading.RedCurve.Frames.Length, "Default per-channel curves have two frames" );
		Assert.AreEqual( 0.5f, grading.RedCurve.Frames[0].Value, 0.001f );
		Assert.AreEqual( 1.0f, grading.RedCurve.Frames[1].Value, 0.001f );

		var dof = go.Components.Create<DepthOfField>( false );
		Assert.AreEqual( 30.0f, dof.BlurSize );
		Assert.AreEqual( 200.0f, dof.FocalDistance );
		Assert.AreEqual( 500.0f, dof.FocusRange );

		var grain = go.Components.Create<FilmGrain>( false );
		Assert.AreEqual( 0.1f, grain.Intensity );
		Assert.AreEqual( 0.5f, grain.Response );

		var outline = go.Components.Create<HighlightOutline>( false );
		Assert.AreEqual( Color.White, outline.Color );
		Assert.AreEqual( Color.Black * 0.4f, outline.ObscuredColor );
		Assert.AreEqual( Color.Transparent, outline.InsideColor );
		Assert.AreEqual( Color.Transparent, outline.InsideObscuredColor );
		Assert.AreEqual( 0.25f, outline.Width );
		Assert.IsFalse( outline.OverrideTargets );
		Assert.IsNull( outline.Targets );
		Assert.IsNull( outline.Material );

		var motion = go.Components.Create<MotionBlur>( false );
		Assert.AreEqual( 0.05f, motion.Scale );

		var pixelate = go.Components.Create<Pixelate>( false );
		Assert.AreEqual( 0.25f, pixelate.Scale );

		var ssr = go.Components.Create<ScreenSpaceReflections>( false );

		var sharpen = go.Components.Create<Sharpen>( false );
		Assert.AreEqual( 2.0f, sharpen.Scale );
		Assert.AreEqual( 1.0f, sharpen.TexelSize );

		var tonemap = go.Components.Create<Tonemapping>( false );
		Assert.AreEqual( Tonemapping.TonemappingMode.HableFilmic, tonemap.Mode );

		var vignette = go.Components.Create<Vignette>( false );
		Assert.AreEqual( Color.Black, vignette.Color );
		Assert.AreEqual( 1.0f, vignette.Intensity );
		Assert.AreEqual( 1.0f, vignette.Smoothness );
		Assert.AreEqual( 1.0f, vignette.Roundness );
		Assert.AreEqual( new Vector2( 0.5f, 0.5f ), vignette.Center );

		var volume = go.Components.Create<PostProcessVolume>( false );
		Assert.AreEqual( 0, volume.Priority );
		Assert.AreEqual( 1.0f, volume.BlendWeight );
		Assert.AreEqual( 50.0f, volume.BlendDistance );
		Assert.IsTrue( volume.EditorPreview );
		Assert.IsFalse( volume.IsInfinite, "Volumes default to a box volume" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//All the simple screen-blit effects round trip their [Property] state through json serialization on a single GameObject:
	//blur, chromatic aberration, color adjustments, film grain, pixelate, sharpen, vignette, motion blur and blit overlay.
	[TestMethod]
	public void ScreenEffectSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var blur = go.Components.Create<Blur>();
		blur.Size = 0.5f;

		var ca = go.Components.Create<ChromaticAberration>();
		ca.Scale = 0.75f;
		ca.Offset = new Vector3( 1, 2, 3 );

		var adjust = go.Components.Create<ColorAdjustments>();
		adjust.Blend = 0.5f;
		adjust.Saturation = 1.5f;
		adjust.HueRotate = 90.0f;
		adjust.Brightness = 1.25f;
		adjust.Contrast = 0.75f;

		var grain = go.Components.Create<FilmGrain>();
		grain.Intensity = 0.4f;
		grain.Response = 0.8f;

		var pixelate = go.Components.Create<Pixelate>();
		pixelate.Scale = 0.6f;

		var sharpen = go.Components.Create<Sharpen>();
		sharpen.Scale = 3.5f;
		sharpen.TexelSize = 2.5f;

		var vignette = go.Components.Create<Vignette>();
		vignette.Color = new Color( 1.0f, 0.0f, 0.0f, 0.5f );
		vignette.Intensity = 0.7f;
		vignette.Smoothness = 0.3f;
		vignette.Roundness = 0.4f;
		vignette.Center = new Vector2( 0.25f, 0.75f );

		var motion = go.Components.Create<MotionBlur>();
		motion.Scale = 0.5f;

		var overlay = go.Components.Create<BlitOverlay>();
		overlay.Blend = 0.9f;
		overlay.BlendMode = BlendMode.Multiply;
		overlay.Order = 7;

		var clone = SerializeRoundTrip( scene, go );

		var loadedBlur = clone.Components.Get<Blur>();
		Assert.IsNotNull( loadedBlur );
		Assert.AreEqual( 0.5f, loadedBlur.Size );

		var loadedCa = clone.Components.Get<ChromaticAberration>();
		Assert.IsNotNull( loadedCa );
		Assert.AreEqual( 0.75f, loadedCa.Scale );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), loadedCa.Offset );

		var loadedAdjust = clone.Components.Get<ColorAdjustments>();
		Assert.IsNotNull( loadedAdjust );
		Assert.AreEqual( 0.5f, loadedAdjust.Blend );
		Assert.AreEqual( 1.5f, loadedAdjust.Saturation );
		Assert.AreEqual( 90.0f, loadedAdjust.HueRotate );
		Assert.AreEqual( 1.25f, loadedAdjust.Brightness );
		Assert.AreEqual( 0.75f, loadedAdjust.Contrast );

		var loadedGrain = clone.Components.Get<FilmGrain>();
		Assert.IsNotNull( loadedGrain );
		Assert.AreEqual( 0.4f, loadedGrain.Intensity );
		Assert.AreEqual( 0.8f, loadedGrain.Response );

		var loadedPixelate = clone.Components.Get<Pixelate>();
		Assert.IsNotNull( loadedPixelate );
		Assert.AreEqual( 0.6f, loadedPixelate.Scale );

		var loadedSharpen = clone.Components.Get<Sharpen>();
		Assert.IsNotNull( loadedSharpen );
		Assert.AreEqual( 3.5f, loadedSharpen.Scale );
		Assert.AreEqual( 2.5f, loadedSharpen.TexelSize );

		var loadedVignette = clone.Components.Get<Vignette>();
		Assert.IsNotNull( loadedVignette );
		Assert.AreEqual( new Color( 1.0f, 0.0f, 0.0f, 0.5f ), loadedVignette.Color );
		Assert.AreEqual( 0.7f, loadedVignette.Intensity );
		Assert.AreEqual( 0.3f, loadedVignette.Smoothness );
		Assert.AreEqual( 0.4f, loadedVignette.Roundness );
		Assert.AreEqual( new Vector2( 0.25f, 0.75f ), loadedVignette.Center );

		var loadedMotion = clone.Components.Get<MotionBlur>();
		Assert.IsNotNull( loadedMotion );
		Assert.AreEqual( 0.5f, loadedMotion.Scale );

		var loadedOverlay = clone.Components.Get<BlitOverlay>();
		Assert.IsNotNull( loadedOverlay );
		Assert.AreEqual( 0.9f, loadedOverlay.Blend );
		Assert.AreEqual( BlendMode.Multiply, loadedOverlay.BlendMode );
		Assert.AreEqual( 7, loadedOverlay.Order );
		Assert.IsNull( loadedOverlay.Material, "A null overlay material round trips as null" );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	//The heavier rendering effects round trip their configuration: ambient occlusion, bloom, depth of field, color grading curves,
	//tonemapping and highlight outline. ScreenSpaceReflections and Highlight have no serializable settings but survive as component presence.
	[TestMethod]
	public void RenderingEffectSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var ao = go.Components.Create<AmbientOcclusion>();
		ao.Intensity = 0.5f;

		var bloom = go.Components.Create<Bloom>();
		bloom.Mode = Rinten.Bloom.BloomMode.Screen;
		bloom.Strength = 5.0f;
		bloom.Threshold = 1.5f;
		bloom.Tint = new Color( 0.1f, 0.2f, 0.3f );

		var dof = go.Components.Create<DepthOfField>();
		dof.BlurSize = 50.0f;
		dof.FocalDistance = 300.0f;
		dof.FocusRange = 100.0f;

		var grading = go.Components.Create<ColorGrading>();
		grading.GradingMethod = ColorGrading.GradingType.TemperatureControl;
		grading.ColorTempK = 9000.0f;
		grading.BlendFactor = 0.5f;
		grading.ColorSpace = ColorGrading.ColorSpaceEnum.RGB;
		grading.RedCurve = new Curve( new Curve.Frame( 0.0f, 0.25f ), new Curve.Frame( 1.0f, 0.75f ) );

		var tonemap = go.Components.Create<Tonemapping>();
		tonemap.Mode = Tonemapping.TonemappingMode.AgX;

		var outline = go.Components.Create<HighlightOutline>();
		outline.Color = Color.Red;
		outline.InsideColor = Color.Green;
		outline.Width = 2.0f;
		outline.OverrideTargets = true;

		go.Components.Create<ScreenSpaceReflections>();
		go.Components.Create<Highlight>();

		var clone = SerializeRoundTrip( scene, go );

		var loadedAo = clone.Components.Get<AmbientOcclusion>();
		Assert.IsNotNull( loadedAo );
		Assert.AreEqual( 0.5f, loadedAo.Intensity );

		var loadedBloom = clone.Components.Get<Bloom>();
		Assert.IsNotNull( loadedBloom );
		Assert.AreEqual( Rinten.Bloom.BloomMode.Screen, loadedBloom.Mode );
		Assert.AreEqual( 5.0f, loadedBloom.Strength );
		Assert.AreEqual( 1.5f, loadedBloom.Threshold, 0.001f, "Threshold round trips untouched because the serialized version is current" );
		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f ), loadedBloom.Tint );

		var loadedDof = clone.Components.Get<DepthOfField>();
		Assert.IsNotNull( loadedDof );
		Assert.AreEqual( 50.0f, loadedDof.BlurSize );
		Assert.AreEqual( 300.0f, loadedDof.FocalDistance );
		Assert.AreEqual( 100.0f, loadedDof.FocusRange );

		var loadedGrading = clone.Components.Get<ColorGrading>();
		Assert.IsNotNull( loadedGrading );
		Assert.AreEqual( ColorGrading.GradingType.TemperatureControl, loadedGrading.GradingMethod );
		Assert.AreEqual( 9000.0f, loadedGrading.ColorTempK );
		Assert.AreEqual( 0.5f, loadedGrading.BlendFactor );
		Assert.AreEqual( ColorGrading.ColorSpaceEnum.RGB, loadedGrading.ColorSpace );
		Assert.AreEqual( 2, loadedGrading.RedCurve.Frames.Length, "Curve key frames should round trip" );
		Assert.AreEqual( 0.25f, loadedGrading.RedCurve.Frames[0].Value, 0.001f );
		Assert.AreEqual( 0.75f, loadedGrading.RedCurve.Frames[1].Value, 0.001f );

		var loadedTonemap = clone.Components.Get<Tonemapping>();
		Assert.IsNotNull( loadedTonemap );
		Assert.AreEqual( Tonemapping.TonemappingMode.AgX, loadedTonemap.Mode );

		var loadedOutline = clone.Components.Get<HighlightOutline>();
		Assert.IsNotNull( loadedOutline );
		Assert.AreEqual( Color.Red, loadedOutline.Color );
		Assert.AreEqual( Color.Green, loadedOutline.InsideColor );
		Assert.AreEqual( 2.0f, loadedOutline.Width );
		Assert.IsTrue( loadedOutline.OverrideTargets );

		Assert.IsNotNull( clone.Components.Get<ScreenSpaceReflections>(), "SSR should survive the round trip" );
		Assert.IsNotNull( clone.Components.Get<Highlight>(), "Highlight should survive the round trip" );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	//A PostProcessVolume round trips its blend configuration and the SceneVolume shape
	//it inherits from VolumeComponent - here a sphere volume with a custom radius.
	[TestMethod]
	public void PostProcessVolumeSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var volume = go.Components.Create<PostProcessVolume>();
		volume.Priority = 3;
		volume.BlendWeight = 0.5f;
		volume.BlendDistance = 100.0f;
		volume.EditorPreview = false;

		var sv = volume.SceneVolume;
		sv.Type = SceneVolume.VolumeTypes.Sphere;
		sv.Sphere = new Sphere( 0, 64 );
		volume.SceneVolume = sv;

		var clone = SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<PostProcessVolume>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a PostProcessVolume" );
		Assert.AreEqual( 3, loaded.Priority );
		Assert.AreEqual( 0.5f, loaded.BlendWeight );
		Assert.AreEqual( 100.0f, loaded.BlendDistance );
		Assert.IsFalse( loaded.EditorPreview );
		Assert.AreEqual( SceneVolume.VolumeTypes.Sphere, loaded.SceneVolume.Type );
		Assert.AreEqual( 64.0f, loaded.SceneVolume.Sphere.Radius, 0.001f );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	//Each tick rebuilds the camera's post-process layers from enabled effects; r_postprocess 0 goes stale
	[TestMethod]
	public void EffectsRegisterCommandListLayersOnCameraTick()
	{
		var oldEnable = PostProcessSystem.EnablePostProcess;
		PostProcessSystem.EnablePostProcess = true;

		try
		{
			var scene = new Scene();
			using var sceneScope = scene.Push();

			var camGo = scene.CreateObject();
			var cam = camGo.Components.Create<CameraComponent>();

			camGo.Components.Create<Vignette>();
			camGo.Components.Create<Blur>();
			var sharpen = camGo.Components.Create<Sharpen>();
			camGo.Components.Create<ChromaticAberration>();
			camGo.Components.Create<Pixelate>();

			var childGo = scene.CreateObject();
			childGo.SetParent( camGo );
			childGo.Components.Create<FilmGrain>();

			var unrelatedGo = scene.CreateObject();
			unrelatedGo.Components.Create<ColorAdjustments>();

			scene.GameTick();

			Assert.AreEqual( 6, TotalLayerCount( cam ), "Five camera effects plus one child effect should have registered" );

			var vignetteLayer = FindLayer( cam, Stage.BeforePostProcess, "Vignette" );
			Assert.IsNotNull( vignetteLayer, "Vignette blits in BeforePostProcess" );
			Assert.AreEqual( 5000, vignetteLayer.Order );
			Assert.IsNotNull( vignetteLayer.CommandList );

			var blurLayer = FindLayer( cam, Stage.BeforePostProcess, "Blur" );
			Assert.IsNotNull( blurLayer );
			Assert.AreEqual( 4000, blurLayer.Order );

			var sharpenLayer = FindLayer( cam, Stage.AfterPostProcess, "Sharpen" );
			Assert.IsNotNull( sharpenLayer );
			Assert.AreEqual( 1, sharpenLayer.Order );

			var caLayer = FindLayer( cam, Stage.AfterPostProcess, "ChromaticAberration" );
			Assert.IsNotNull( caLayer );
			Assert.AreEqual( 1000, caLayer.Order );

			var pixelateLayer = FindLayer( cam, Stage.AfterPostProcess, "Pixelate" );
			Assert.IsNotNull( pixelateLayer );
			Assert.AreEqual( 10000, pixelateLayer.Order );

			var grainLayer = FindLayer( cam, Stage.AfterPostProcess, "FilmGrain" );
			Assert.IsNotNull( grainLayer, "Effects on descendant GameObjects register too" );
			Assert.AreEqual( 200, grainLayer.Order );

			Assert.IsNull( FindLayer( cam, Stage.AfterPostProcess, "ColorAdjustments" ), "Effects on unrelated GameObjects do not register" );

			sharpen.Enabled = false;
			scene.GameTick();

			Assert.AreEqual( 5, TotalLayerCount( cam ), "Disabled effects should drop out on the next tick" );
			Assert.IsNull( FindLayer( cam, Stage.AfterPostProcess, "Sharpen" ) );

			cam.EnablePostProcessing = false;
			scene.GameTick();

			Assert.AreEqual( 0, TotalLayerCount( cam ), "Disabling camera post processing should clear all layers" );

			cam.EnablePostProcessing = true;
			scene.GameTick();

			Assert.AreEqual( 5, TotalLayerCount( cam ), "Re-enabling should rebuild the layers" );

			PostProcessSystem.EnablePostProcess = false;
			scene.GameTick();

			Assert.AreEqual( 5, TotalLayerCount( cam ), "The global convar early-outs before the clear, so the previous layers persist untouched" );
			Assert.IsNotNull( FindLayer( cam, Stage.BeforePostProcess, "Vignette" ) );
		}
		finally
		{
			PostProcessSystem.EnablePostProcess = oldEnable;
		}
	}

	//Zero-strength effects register no layer; Vignette also skips when its colour alpha is zero
	[TestMethod]
	public void ZeroStrengthEffectsSkipLayerRegistration()
	{
		var oldEnable = PostProcessSystem.EnablePostProcess;
		var oldBloom = Bloom.UserEnabled;

		PostProcessSystem.EnablePostProcess = true;
		Bloom.UserEnabled = true;

		try
		{
			var scene = new Scene();
			using var sceneScope = scene.Push();

			var camGo = scene.CreateObject();
			var cam = camGo.Components.Create<CameraComponent>();

			var blur = camGo.Components.Create<Blur>();
			blur.Size = 0.0f;

			var vignette = camGo.Components.Create<Vignette>();
			vignette.Intensity = 0.0f;

			var sharpen = camGo.Components.Create<Sharpen>();
			sharpen.Scale = 0.0f;

			var pixelate = camGo.Components.Create<Pixelate>();
			pixelate.Scale = 0.0f;

			var grain = camGo.Components.Create<FilmGrain>();
			grain.Intensity = 0.0f;

			var ca = camGo.Components.Create<ChromaticAberration>();
			ca.Scale = 0.0f;

			var bloom = camGo.Components.Create<Bloom>();
			bloom.Strength = 0.0f;

			var grading = camGo.Components.Create<ColorGrading>();
			grading.BlendFactor = 0.0f;

			var motion = camGo.Components.Create<MotionBlur>();
			motion.Scale = 0.0f;

			scene.GameTick();

			Assert.AreEqual( 0, TotalLayerCount( cam ), "All zero-strength effects should early out without registering layers" );

			vignette.Intensity = 0.5f;
			scene.GameTick();

			Assert.AreEqual( 1, TotalLayerCount( cam ), "Only the vignette should have registered" );
			Assert.IsNotNull( FindLayer( cam, Stage.BeforePostProcess, "Vignette" ) );

			vignette.Color = Color.Transparent;
			scene.GameTick();

			Assert.AreEqual( 0, TotalLayerCount( cam ), "A fully transparent vignette color skips the blit despite the intensity" );
		}
		finally
		{
			PostProcessSystem.EnablePostProcess = oldEnable;
			Bloom.UserEnabled = oldBloom;
		}
	}

	//Highlight registers only while a HighlightOutline exists; OverrideTargets uses the manual list
	[TestMethod]
	public void HighlightRegistersOnlyWithOutlines()
	{
		var oldEnable = PostProcessSystem.EnablePostProcess;
		PostProcessSystem.EnablePostProcess = true;

		try
		{
			var scene = new Scene();
			using var sceneScope = scene.Push();

			var camGo = scene.CreateObject();
			var cam = camGo.Components.Create<CameraComponent>();
			camGo.Components.Create<Highlight>();

			scene.GameTick();

			Assert.IsNull( FindLayer( cam, Stage.AfterTransparent, "Highlight" ), "No outlines in the scene means no highlight pass" );

			var outlineGo = scene.CreateObject();
			var mr = outlineGo.Components.Create<ModelRenderer>();
			var outline = outlineGo.Components.Create<HighlightOutline>();

			Assert.IsTrue( outline.GetOutlineTargets().Contains( mr ), "Default targets are the renderers on self and descendants" );

			scene.GameTick();

			var layer = FindLayer( cam, Stage.AfterTransparent, "Highlight" );
			Assert.IsNotNull( layer, "An outline in the scene should register the highlight pass" );
			Assert.AreEqual( 1000, layer.Order );

			outline.OverrideTargets = true;

			Assert.IsFalse( outline.GetOutlineTargets().Any(), "Manual targets with a null list resolve to empty" );

			outline.Targets = new List<Renderer> { mr };

			Assert.IsTrue( outline.GetOutlineTargets().SequenceEqual( new Renderer[] { mr } ), "Manual targets return exactly the list" );

			outline.Enabled = false;
			scene.GameTick();

			Assert.IsNull( FindLayer( cam, Stage.AfterTransparent, "Highlight" ), "Disabling the only outline removes the highlight pass" );
		}
		finally
		{
			PostProcessSystem.EnablePostProcess = oldEnable;
		}
	}

	//GetWeight ramps over BlendDistance using unsigned edge distance; lookups need containment
	[TestMethod]
	public void VolumeWeightMath()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var volume = go.Components.Create<PostProcessVolume>();

		//Default volume is a 100 unit box (+-50) with BlendDistance 50 and BlendWeight 1
		Assert.IsFalse( volume.IsInfinite );
		Assert.AreEqual( 1.0f, volume.GetWeight( Vector3.Zero ), 0.001f, "The box centre is 50 units from the edge, a full blend distance" );
		Assert.AreEqual( 0.2f, volume.GetWeight( new Vector3( 40, 0, 0 ) ), 0.001f, "10 units inside the edge is a fifth of the blend distance" );
		Assert.AreEqual( 0.2f, volume.GetWeight( new Vector3( 60, 0, 0 ) ), 0.001f, "The edge distance is unsigned - 10 units outside weighs the same as 10 units inside" );

		volume.BlendWeight = 0.5f;
		Assert.AreEqual( 0.5f, volume.GetWeight( Vector3.Zero ), 0.001f, "BlendWeight caps the fully blended weight" );

		volume.BlendDistance = 200.0f;
		Assert.AreEqual( 0.125f, volume.GetWeight( Vector3.Zero ), 0.001f, "A blend distance larger than the box means even the centre is partially blended" );

		volume.BlendDistance = 0.0f;
		Assert.AreEqual( 0.0f, volume.GetWeight( Vector3.Zero ), 0.001f, "Zero blend distance degenerates the remap to zero weight, not a hard edge" );

		volume.BlendDistance = 50.0f;
		volume.BlendWeight = 0.75f;

		var sv = volume.SceneVolume;
		sv.Type = SceneVolume.VolumeTypes.Infinite;
		volume.SceneVolume = sv;

		Assert.IsTrue( volume.IsInfinite );
		Assert.AreEqual( 0.75f, volume.GetWeight( new Vector3( 99999, 0, 0 ) ), 0.001f, "Infinite volumes return BlendWeight everywhere" );

		sv.Type = SceneVolume.VolumeTypes.Box;
		volume.SceneVolume = sv;
		go.WorldPosition = new Vector3( 1000, 0, 0 );

		var volumeSystem = scene.GetSystem<VolumeSystem>();
		Assert.IsNotNull( volumeSystem, "The volume system is a standard scene system" );
		Assert.IsFalse( volumeSystem.FindAll<PostProcessVolume>( Vector3.Zero ).Contains( volume ), "The moved volume no longer contains the origin" );
		Assert.IsTrue( volumeSystem.FindAll<PostProcessVolume>( new Vector3( 1000, 0, 0 ) ).Contains( volume ), "The volume contains its new centre" );
		Assert.AreEqual( 0.75f, volume.GetWeight( new Vector3( 1000, 0, 0 ) ), 0.001f, "GetWeight follows the volume's world transform" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//A volume-only effect registers on the camera weighted by the volume; zero weight unregisters it
	[TestMethod]
	public void VolumeOnlyEffectRegistersOnCamera()
	{
		var oldEnable = PostProcessSystem.EnablePostProcess;
		PostProcessSystem.EnablePostProcess = true;

		try
		{
			var scene = new Scene();
			using var sceneScope = scene.Push();

			var camGo = scene.CreateObject();
			var cam = camGo.Components.Create<CameraComponent>();

			var volumeGo = scene.CreateObject();
			var volume = volumeGo.Components.Create<PostProcessVolume>();
			var sv = volume.SceneVolume;
			sv.Type = SceneVolume.VolumeTypes.Infinite;
			volume.SceneVolume = sv;

			volumeGo.Components.Create<Vignette>();

			scene.GameTick();

			Assert.IsNotNull( FindLayer( cam, Stage.BeforePostProcess, "Vignette" ), "A volume-only effect registers on the camera through the volume" );

			volume.BlendWeight = 0.0f;
			scene.GameTick();

			Assert.IsNull( FindLayer( cam, Stage.BeforePostProcess, "Vignette" ), "Zero volume weight zeroes the blended intensity and the effect early-outs" );
		}
		finally
		{
			PostProcessSystem.EnablePostProcess = oldEnable;
		}
	}

	//Old __version JSON runs upgraders first (Bloom threshold remap, AO drops old radius/intensity)
	[TestMethod]
	public void JsonUpgradersRewriteOldComponentData()
	{
		//The upgrader method cache is global state that other tests reinitialize with mocked type libraries - reset it to the host's library for determinism.
		JsonUpgrader.UpdateUpgraders( Game.TypeLibrary );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var bloom = go.Components.Create<Bloom>( false );
		var bloomJson = Json.ParseToJsonObject( "{\"__type\":\"Bloom\",\"Threshold\":1}" );
		bloom.DeserializeImmediately( bloomJson );

		Assert.AreEqual( 1.5f, bloom.Threshold, 0.001f, "The v1 upgrader remaps the old threshold as t/2 + 1" );

		var ao = go.Components.Create<AmbientOcclusion>( false );
		var aoJson = Json.ParseToJsonObject( "{\"__type\":\"AmbientOcclusion\",\"Radius\":32,\"Intensity\":0.25,\"FalloffRange\":0.5}" );
		ao.DeserializeImmediately( aoJson );

		Assert.AreEqual( 1.0f, ao.Intensity, 0.001f, "The v1 upgrader discards the old intensity, keeping the default" );

		go.Destroy();
		scene.ProcessDeletes();
	}
}
