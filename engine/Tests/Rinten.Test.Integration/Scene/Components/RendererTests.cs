using System.Collections.Generic;
using Rinten.Rendering;

namespace SceneTests.Components;

//Shared helpers for the renderer component tests.
static class RenderTestUtility
{
	//Serializes a GameObject to json, destroys the original, then deserializes the json back into the
	//scene and enables it - the standard save/load round trip idiom used by the integration tests.
	internal static GameObject SerializeRoundTrip( Scene scene, GameObject go )
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
}

[TestClass]
public class RenderComponentTest
{
	//Enabling a ModelRenderer creates a SceneObject. With no model assigned the scene object renders the dev box fallback model. Disabling deletes
	//the scene object and nulls the SceneObject property, re-enabling creates a fresh scene object, and destroying the GameObject tears it all down.
	[TestMethod]
	public void ModelRendererSceneObjectLifecycle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var mr = go.Components.Create<ModelRenderer>();

		var so = mr.SceneObject;
		Assert.IsNotNull( so, "Enabling a ModelRenderer should create a SceneObject" );
		Assert.IsTrue( so.IsValid() );
		Assert.IsNull( mr.Model, "No model was assigned to the component" );
		Assert.AreEqual( Model.Load( "models/dev/box.vmdl" ), so.Model, "A null model should fall back to the dev box on the scene object" );

		mr.Enabled = false;

		Assert.IsNull( mr.SceneObject, "Disabling should null the scene object" );
		Assert.IsFalse( so.IsValid(), "Disabling should delete the scene object" );

		mr.Enabled = true;

		var second = mr.SceneObject;
		Assert.IsNotNull( second, "Re-enabling should create a new scene object" );
		Assert.IsTrue( second.IsValid() );
		Assert.AreNotSame( so, second );

		go.Destroy();
		scene.ProcessDeletes();

		Assert.IsFalse( second.IsValid(), "Destroying the GameObject should delete the scene object" );
	}

	//Assigning a model to a live ModelRenderer pushes it straight through to the
	//scene object and enables rendering, because the citizen model has render meshes.
	[TestMethod]
	public void ModelRendererModelAssignment()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var mr = go.Components.Create<ModelRenderer>();

		var citizen = Model.Load( "models/citizen/citizen.vmdl" );
		mr.Model = citizen;

		Assert.AreEqual( citizen, mr.Model );
		Assert.AreEqual( citizen, mr.SceneObject.Model, "Model should propagate to the scene object" );
		Assert.IsTrue( mr.SceneObject.RenderingEnabled, "A model with render meshes should be rendering" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//The Tint setter writes through to the scene object's ColorTint while the component keeps its exact value. MaterialOverride is plain
	//component state readable back via GetMaterial, and ClearMaterialOverrides resets it to null without harming the live scene object.
	[TestMethod]
	public void ModelRendererTintAndMaterialOverride()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var mr = go.Components.Create<ModelRenderer>();

		var tint = new Color( 0.25f, 0.5f, 0.75f, 0.5f );
		mr.Tint = tint;

		Assert.AreEqual( tint, mr.Tint );
		Assert.AreEqual( 0.25f, mr.SceneObject.ColorTint.r, 0.01f );
		Assert.AreEqual( 0.5f, mr.SceneObject.ColorTint.g, 0.01f );
		Assert.AreEqual( 0.75f, mr.SceneObject.ColorTint.b, 0.01f );
		Assert.AreEqual( 0.5f, mr.SceneObject.ColorTint.a, 0.01f );

		var mat = Material.Load( "materials/default/white.vmat" );
		Assert.IsNotNull( mat, "white.vmat should ship in core" );

		mr.MaterialOverride = mat;

		Assert.AreEqual( mat, mr.MaterialOverride );
		Assert.AreEqual( mat, mr.GetMaterial(), "IMaterialSetter.GetMaterial should return the override" );
		Assert.IsFalse( mr.HasMaterialGroups, "An override disables material group selection" );
		Assert.IsTrue( mr.SceneObject.IsValid(), "Applying an override should not invalidate the scene object" );

		mr.ClearMaterialOverrides();

		Assert.IsNull( mr.MaterialOverride, "ClearMaterialOverrides should null the override" );
		Assert.IsTrue( mr.SceneObject.IsValid() );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//RenderType maps onto the scene object flags: On casts shadows in the game layer, Off clears the shadow flag, ShadowsOnly
	//keeps shadows but excludes the game layer, and going back to On restores the game layer via RenderOptions.Apply.
	[TestMethod]
	public void ModelRendererShadowRenderTypeFlags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var mr = go.Components.Create<ModelRenderer>();
		var so = mr.SceneObject;

		Assert.AreEqual( ModelRenderer.ShadowRenderType.On, mr.RenderType );
		Assert.IsTrue( so.Flags.CastShadows, "Default render type should cast shadows" );
		Assert.IsFalse( so.Flags.ExcludeGameLayer );

		mr.RenderType = ModelRenderer.ShadowRenderType.Off;

		Assert.IsFalse( so.Flags.CastShadows, "Off should clear the shadow flag" );
		Assert.IsFalse( so.Flags.ExcludeGameLayer );

		mr.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;

		Assert.IsTrue( so.Flags.CastShadows, "ShadowsOnly still casts shadows" );
		Assert.IsTrue( so.Flags.ExcludeGameLayer, "ShadowsOnly should exclude the game layer" );

		mr.RenderType = ModelRenderer.ShadowRenderType.On;

		Assert.IsTrue( so.Flags.CastShadows );
		Assert.IsFalse( so.Flags.ExcludeGameLayer, "Returning to On should re-include the game layer" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//With no model the local bounds are a placeholder 16-inch box around the origin.
	[TestMethod]
	public void ModelRendererBoundsFollowModelAndTransform()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var mr = go.Components.Create<ModelRenderer>();

		Assert.AreEqual( BBox.FromPositionAndSize( Vector3.Zero, 0.4064f ), mr.LocalBounds, "Null model should give the placeholder bounds" );

		var box = Model.Load( "models/dev/box.vmdl" );
		mr.Model = box;

		Assert.AreEqual( box.RenderBounds.Mins, mr.LocalBounds.Mins, "Local bounds should be the model render bounds" );
		Assert.AreEqual( box.RenderBounds.Maxs, mr.LocalBounds.Maxs );

		var offset = new Vector3( 100, 200, 300 );
		go.WorldPosition = offset;

		Assert.IsTrue( mr.Bounds.Center.Distance( box.RenderBounds.Center + offset ) < 0.01f, "World bounds should follow the GameObject position" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//ModelRenderer round-trips; quirk: CastShadows reads true if either shadow bit is set
	[TestMethod]
	public void ModelRendererSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var box = Model.Load( "models/dev/box.vmdl" );
		var mat = Material.Load( "materials/default/white.vmat" );

		var go = scene.CreateObject();
		var mr = go.Components.Create<ModelRenderer>();
		mr.Model = box;
		mr.Tint = new Color( 0.1f, 0.2f, 0.3f );
		mr.RenderType = ModelRenderer.ShadowRenderType.Off;
		mr.MaterialOverride = mat;

		var clone = RenderTestUtility.SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<ModelRenderer>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a ModelRenderer" );
		Assert.AreEqual( box.Name, loaded.Model?.Name, "Model should round trip by path" );
		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f ), loaded.Tint );
		Assert.AreEqual( ModelRenderer.ShadowRenderType.Off, loaded.RenderType );
		Assert.AreEqual( mat.Name, loaded.MaterialOverride?.Name, "Material override should round trip by path" );

		Assert.IsNotNull( loaded.SceneObject, "Deserialized renderer should create its scene object when enabled" );
		Assert.IsFalse( loaded.SceneObject.Flags.HasFlag( SceneObjectFlags.CastShadowsEnabled ), "RenderType Off should clear the shadow-enable bit on the new scene object" );
		Assert.IsTrue( loaded.SceneObject.Flags.HasFlag( SceneObjectFlags.MaterialSupportsShadows ), "Applying the material override re-asserts MaterialSupportsShadows from the material" );
		Assert.IsTrue( loaded.SceneObject.Flags.CastShadows, "The any-bit CastShadows accessor reads true despite RenderType Off because of the material bit" );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	//A LineRenderer using vector points builds its line during the pre-render stage: the scene object starts hidden, becomes rendering after a tick with
	//two or more points, the line bounds cover every point, and dropping to a single point hides it again. Disabling the component deletes the scene object.
	[TestMethod]
	public void LineRendererVectorPointsDriveSceneObject()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var lr = go.Components.Create<LineRenderer>( false );
		lr.UseVectorPoints = true;
		lr.VectorPoints = new List<Vector3>
		{
			new Vector3( 0, 0, 0 ),
			new Vector3( 100, 0, 0 ),
			new Vector3( 100, 100, 0 ),
		};
		lr.Enabled = true;

		var so = scene.SceneWorld.SceneObjects.OfType<SceneLineObject>().Single();
		Assert.IsFalse( so.RenderingEnabled, "The line starts hidden until the first pre-render builds it" );

		scene.GameTick();

		Assert.IsTrue( so.RenderingEnabled, "Two or more points should enable rendering" );
		Assert.IsTrue( so.Bounds.Contains( new Vector3( 0, 0, 0 ) ) );
		Assert.IsTrue( so.Bounds.Contains( new Vector3( 100, 0, 0 ) ) );
		Assert.IsTrue( so.Bounds.Contains( new Vector3( 100, 100, 0 ) ), "Line bounds should cover every point" );

		lr.VectorPoints = new List<Vector3> { new Vector3( 0, 0, 0 ) };
		scene.GameTick();

		Assert.IsFalse( so.RenderingEnabled, "A single point is not a line" );

		lr.Enabled = false;

		Assert.IsFalse( so.IsValid(), "Disabling the component should delete the scene object" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//In GameObject point mode the line follows the point objects' world positions, and disabled
	//point objects are skipped - leaving fewer than two active points turns rendering off.
	[TestMethod]
	public void LineRendererGameObjectPoints()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = scene.CreateObject();
		a.WorldPosition = new Vector3( 0, 0, 0 );

		var b = scene.CreateObject();
		b.WorldPosition = new Vector3( 0, 50, 0 );

		var go = scene.CreateObject();
		var lr = go.Components.Create<LineRenderer>( false );
		lr.Points = new List<GameObject> { a, b };
		lr.Enabled = true;

		var so = scene.SceneWorld.SceneObjects.OfType<SceneLineObject>().Single();

		scene.GameTick();

		Assert.IsTrue( so.RenderingEnabled, "Two active point objects should enable rendering" );
		Assert.IsTrue( so.Bounds.Contains( new Vector3( 0, 50, 0 ) ), "Bounds should cover the point object positions" );

		b.Enabled = false;
		scene.GameTick();

		Assert.IsFalse( so.RenderingEnabled, "Disabled point objects are skipped, one point is not a line" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//LineRenderer appearance properties are copied to the scene object every pre-render:
	//face mode, caps, wireframe, lighting, shadow casting and the opaque/translucent flags.
	[TestMethod]
	public void LineRendererPropertyPropagation()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var lr = go.Components.Create<LineRenderer>( false );
		lr.UseVectorPoints = true;
		lr.VectorPoints = new List<Vector3> { new Vector3( 0, 0, 0 ), new Vector3( 50, 0, 0 ) };
		lr.Face = SceneLineObject.FaceMode.Normal;
		lr.StartCap = SceneLineObject.CapStyle.Arrow;
		lr.EndCap = SceneLineObject.CapStyle.Rounded;
		lr.Wireframe = true;
		lr.Lighting = true;
		lr.CastShadows = false;
		lr.Opaque = false;
		lr.Enabled = true;

		scene.GameTick();

		var so = scene.SceneWorld.SceneObjects.OfType<SceneLineObject>().Single();

		Assert.AreEqual( SceneLineObject.FaceMode.Normal, so.Face );
		Assert.AreEqual( SceneLineObject.CapStyle.Arrow, so.StartCap );
		Assert.AreEqual( SceneLineObject.CapStyle.Rounded, so.EndCap );
		Assert.IsTrue( so.Wireframe );
		Assert.IsTrue( so.Lighting );
		Assert.IsFalse( so.Flags.CastShadows );
		Assert.IsFalse( so.Flags.IsOpaque, "A non-opaque line should not be in the opaque pass" );
		Assert.IsTrue( so.Flags.IsTranslucent, "A non-opaque line should be in the translucent pass" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//LineRenderer points, spline settings, rendering switches, color gradient and width curve all survive a GameObject serialize/deserialize round trip.
	[TestMethod]
	public void LineRendererSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var points = new List<Vector3> { new Vector3( 1, 2, 3 ), new Vector3( 4, 5, 6 ), new Vector3( 7, 8, 9 ) };

		var go = scene.CreateObject();
		var lr = go.Components.Create<LineRenderer>();
		lr.UseVectorPoints = true;
		lr.VectorPoints = points;
		lr.Face = SceneLineObject.FaceMode.Normal;
		lr.SplineInterpolation = 4;
		lr.SplineTension = 0.5f;
		lr.CastShadows = false;
		lr.Opaque = false;
		lr.Additive = true;
		lr.DepthFeather = 2.0f;
		lr.FogStrength = 0.25f;
		lr.Color = Color.Red;
		lr.Width = 12.0f;

		var clone = RenderTestUtility.SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<LineRenderer>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a LineRenderer" );
		Assert.IsTrue( loaded.UseVectorPoints );
		Assert.IsTrue( loaded.VectorPoints.SequenceEqual( points ), "Vector points should round trip" );
		Assert.AreEqual( SceneLineObject.FaceMode.Normal, loaded.Face );
		Assert.AreEqual( 4, loaded.SplineInterpolation );
		Assert.AreEqual( 0.5f, loaded.SplineTension );
		Assert.IsFalse( loaded.CastShadows );
		Assert.IsFalse( loaded.Opaque );
		Assert.IsTrue( loaded.Additive );
		Assert.AreEqual( 2.0f, loaded.DepthFeather );
		Assert.AreEqual( 0.25f, loaded.FogStrength );
		Assert.AreEqual( Color.Red, loaded.Color.Evaluate( 0.5f ), "Solid gradient should round trip" );
		Assert.AreEqual( 12.0f, loaded.Width.Evaluate( 0.5f ), 0.001f, "Constant width curve should round trip" );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	//TextRenderer state lives in its TextScope: the defaults are pinned, the convenience accessors (Text, FontSize, Color,
	//FontFamily) write into the scope, and the custom text scene object is created on enable and deleted on disable.
	[TestMethod]
	public void TextRendererStateAndSceneObjectLifecycle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var baseline = scene.SceneWorld.SceneObjects.OfType<SceneCustomObject>().Count();

		var go = scene.CreateObject();
		var tr = go.Components.Create<TextRenderer>();

		Assert.AreEqual( "Hello! ❤", tr.Text, "Default text is pinned" );
		Assert.AreEqual( 32.0f, tr.FontSize );
		Assert.AreEqual( "Inter", tr.FontFamily );
		Assert.AreEqual( 400, tr.FontWeight );
		Assert.AreEqual( Color.White, tr.Color );
		Assert.AreEqual( 1.0f, tr.Scale );
		Assert.AreEqual( TextRenderer.HAlignment.Center, tr.HorizontalAlignment );
		Assert.AreEqual( TextRenderer.VAlignment.Center, tr.VerticalAlignment );
		Assert.AreEqual( BlendMode.Normal, tr.BlendMode );
		Assert.AreEqual( 1.0f, tr.FogStrength );

		Assert.AreEqual( baseline + 1, scene.SceneWorld.SceneObjects.OfType<SceneCustomObject>().Count(), "Enabling should create the text scene object" );

		tr.Text = "changed";
		tr.FontSize = 48.0f;
		tr.Color = Color.Red;
		tr.FontFamily = "Roboto";

		Assert.AreEqual( "changed", tr.Text );
		Assert.AreEqual( 48.0f, tr.FontSize );
		Assert.AreEqual( Color.Red, tr.Color );
		Assert.AreEqual( "Roboto", tr.FontFamily );

		tr.Enabled = false;

		Assert.AreEqual( baseline, scene.SceneWorld.SceneObjects.OfType<SceneCustomObject>().Count(), "Disabling should delete the text scene object" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//A TextRenderer with non-default text, font, color, scale, alignment, blend mode and fog strength
	//survives a serialize/deserialize round trip and recreates its scene object when the clone is enabled.
	[TestMethod]
	public void TextRendererSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var baseline = scene.SceneWorld.SceneObjects.OfType<SceneCustomObject>().Count();

		var go = scene.CreateObject();
		var tr = go.Components.Create<TextRenderer>();
		tr.Text = "round trip";
		tr.FontSize = 64.0f;
		tr.FontFamily = "Roboto";
		tr.Color = new Color( 0.1f, 0.2f, 0.3f );
		tr.Scale = 0.5f;
		tr.HorizontalAlignment = TextRenderer.HAlignment.Left;
		tr.VerticalAlignment = TextRenderer.VAlignment.Bottom;
		tr.BlendMode = BlendMode.Multiply;
		tr.FogStrength = 0.5f;

		var clone = RenderTestUtility.SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<TextRenderer>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a TextRenderer" );
		Assert.AreEqual( "round trip", loaded.Text );
		Assert.AreEqual( 64.0f, loaded.FontSize );
		Assert.AreEqual( "Roboto", loaded.FontFamily );
		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f ), loaded.Color );
		Assert.AreEqual( 0.5f, loaded.Scale );
		Assert.AreEqual( TextRenderer.HAlignment.Left, loaded.HorizontalAlignment );
		Assert.AreEqual( TextRenderer.VAlignment.Bottom, loaded.VerticalAlignment );
		Assert.AreEqual( BlendMode.Multiply, loaded.BlendMode );
		Assert.AreEqual( 0.5f, loaded.FogStrength );

		Assert.AreEqual( baseline + 1, scene.SceneWorld.SceneObjects.OfType<SceneCustomObject>().Count(), "Enabled clone should have a live text scene object" );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	//SpriteRenderer defaults; headless registration leaks a half-built batch object (suspected bug)
	[TestMethod]
	public void SpriteRendererDefaultsAndRegistration()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sr = go.Components.Create<SpriteRenderer>();

		Assert.IsNull( sr.Sprite );
		Assert.AreEqual( new Vector2( 10, 10 ), sr.Size );
		Assert.AreEqual( Color.White, sr.Color );
		Assert.IsFalse( sr.Additive );
		Assert.IsFalse( sr.Opaque );
		Assert.AreEqual( 0.5f, sr.AlphaCutoff );
		Assert.AreEqual( FilterMode.Bilinear, sr.TextureFilter );
		Assert.AreEqual( SpriteRenderer.BillboardMode.None, sr.Billboard );
		Assert.AreEqual( 1.0f, sr.FogStrength );
		Assert.IsFalse( sr.IsAnimated, "No sprite means no animations" );
		Assert.IsNull( sr.CurrentAnimation );
		Assert.AreEqual( Texture.Transparent, sr.Texture, "Sprite-less renderer falls back to the transparent texture" );

		sr.PlayAnimation( "missing" );
		sr.PlayAnimation( 5 );

		scene.GameTick();

		var batch = scene.SceneWorld.SceneObjects.OfType<SpriteBatchSceneObject>().Single();
		Assert.IsFalse( batch.ContainsSprite( sr.Id ), "Registration aborts mid-construction in the test host, leaving the batch scene object empty" );

		sr.Enabled = false;
		scene.GameTick();

		Assert.AreEqual( 1, scene.SceneWorld.SceneObjects.OfType<SpriteBatchSceneObject>().Count(), "A disabled sprite is skipped, so no further registration attempt creates another batch object" );
		Assert.IsFalse( batch.ContainsSprite( sr.Id ), "The sprite never made it into the batch" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//SpriteRenderer visual state - size, colors, blending, sorting, flips, filtering and billboard mode - survives a serialize/deserialize round trip.
	[TestMethod]
	public void SpriteRendererSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sr = go.Components.Create<SpriteRenderer>();
		sr.Size = new Vector2( 32, 16 );
		sr.Color = new Color( 0.5f, 0.25f, 0.125f );
		sr.Additive = true;
		sr.Opaque = true;
		sr.AlphaCutoff = 0.25f;
		sr.Lighting = true;
		sr.DepthFeather = 4.0f;
		sr.FogStrength = 0.75f;
		sr.FlipHorizontal = true;
		sr.FlipVertical = true;
		sr.TextureFilter = FilterMode.Point;
		sr.Billboard = SpriteRenderer.BillboardMode.YOnly;
		sr.IsSorted = true;

		var clone = RenderTestUtility.SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<SpriteRenderer>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a SpriteRenderer" );
		Assert.AreEqual( new Vector2( 32, 16 ), loaded.Size );
		Assert.AreEqual( new Color( 0.5f, 0.25f, 0.125f ), loaded.Color );
		Assert.IsTrue( loaded.Additive );
		Assert.IsTrue( loaded.Opaque );
		Assert.AreEqual( 0.25f, loaded.AlphaCutoff );
		Assert.IsTrue( loaded.Lighting );
		Assert.AreEqual( 4.0f, loaded.DepthFeather );
		Assert.AreEqual( 0.75f, loaded.FogStrength );
		Assert.IsTrue( loaded.FlipHorizontal );
		Assert.IsTrue( loaded.FlipVertical );
		Assert.AreEqual( FilterMode.Point, loaded.TextureFilter );
		Assert.AreEqual( SpriteRenderer.BillboardMode.YOnly, loaded.Billboard );
		Assert.IsTrue( loaded.IsSorted );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	//Trails add a point per tick when moved past PointDistance; Emitting and MaxPoints limit them
	[TestMethod]
	public void TrailRendererEmitsPointsWhileMoving()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var tr = go.Components.Create<TrailRenderer>();

		var so = scene.SceneWorld.SceneObjects.OfType<SceneTrailObject>().Single();

		scene.GameTick();

		Assert.AreEqual( 1, so.PointCount, "The first update should add the initial point" );
		Assert.IsTrue( so.IsEmpty, "A single point is an empty trail" );

		for ( int i = 1; i <= 5; i++ )
		{
			go.WorldPosition = new Vector3( i * 20, 0, 0 );
			scene.GameTick();
		}

		Assert.AreEqual( 6, so.PointCount, "Each move beyond PointDistance should add a point" );
		Assert.IsFalse( so.IsEmpty );
		Assert.AreEqual( tr.MaxPoints, so.MaxPoints, "Trail settings should be copied to the scene object" );
		Assert.AreEqual( tr.PointDistance, so.PointDistance );
		Assert.AreEqual( tr.LifeTime, so.LifeTime );

		for ( int i = 0; i < 3; i++ )
		{
			scene.GameTick();
		}

		Assert.AreEqual( 6, so.PointCount, "Stationary ticks should not add points" );

		tr.Emitting = false;
		go.WorldPosition = new Vector3( 200, 0, 0 );
		scene.GameTick();

		Assert.AreEqual( 6, so.PointCount, "Emitting=false should suppress new points" );

		tr.Emitting = true;
		go.WorldPosition = new Vector3( 300, 0, 0 );
		scene.GameTick();

		Assert.AreEqual( 7, so.PointCount, "Re-enabling emission should add points again" );

		tr.MaxPoints = 4;
		go.WorldPosition = new Vector3( 400, 0, 0 );
		scene.GameTick();

		Assert.AreEqual( 4, so.PointCount, "MaxPoints should trim the oldest points" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//Trail points decay after LifeTime and stationary ticks add none, leaving an empty trail
	[TestMethod]
	public void TrailRendererLifetimeExpiresPoints()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var tr = go.Components.Create<TrailRenderer>();
		tr.LifeTime = 0.6f;

		var so = scene.SceneWorld.SceneObjects.OfType<SceneTrailObject>().Single();

		scene.GameTick();

		for ( int i = 1; i <= 2; i++ )
		{
			go.WorldPosition = new Vector3( i * 20, 0, 0 );
			scene.GameTick();
		}

		Assert.AreEqual( 3, so.PointCount, "Three points should exist before decay" );

		for ( int i = 0; i < 8; i++ )
		{
			scene.GameTick();
		}

		Assert.AreEqual( 0, so.PointCount, "All points should have aged out" );
		Assert.IsTrue( so.IsEmpty );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//TrailRenderer emission and appearance settings - point budget, distances, lifetime, emission
	//switch, render switches, blend mode and face mode - survive a serialize/ deserialize round trip.
	[TestMethod]
	public void TrailRendererSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var tr = go.Components.Create<TrailRenderer>();
		tr.MaxPoints = 16;
		tr.PointDistance = 4.0f;
		tr.LifeTime = 0.5f;
		tr.Emitting = false;
		tr.Opaque = false;
		tr.BlendMode = BlendMode.Lighten;
		tr.Wireframe = true;
		tr.CastShadows = true;
		tr.Face = SceneLineObject.FaceMode.Normal;
		tr.Width = 9.0f;

		var clone = RenderTestUtility.SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<TrailRenderer>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a TrailRenderer" );
		Assert.AreEqual( 16, loaded.MaxPoints );
		Assert.AreEqual( 4.0f, loaded.PointDistance );
		Assert.AreEqual( 0.5f, loaded.LifeTime );
		Assert.IsFalse( loaded.Emitting );
		Assert.IsFalse( loaded.Opaque );
		Assert.AreEqual( BlendMode.Lighten, loaded.BlendMode );
		Assert.IsTrue( loaded.Wireframe );
		Assert.IsTrue( loaded.CastShadows );
		Assert.AreEqual( SceneLineObject.FaceMode.Normal, loaded.Face );
		Assert.AreEqual( 9.0f, loaded.Width.Evaluate( 0.5f ), 0.001f );

		clone.Destroy();
		scene.ProcessDeletes();
	}
}

[TestClass]
public class RenderEffectComponentTest
{
	//Enabling a SkyBox2D tags the GameObject "skybox" and creates a SceneSkyBox, tint writes through to it, and
	//disabling removes it. What the sky lights the world with is its table, not a probe - see SkyAmbient in frame.slang.
	[TestMethod]
	public void SkyBoxLifecycleAndTint()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var skyBaseline = scene.SceneWorld.SceneObjects.OfType<SceneSkyBox>().Count();

		var go = scene.CreateObject();
		var sky = go.Components.Create<SkyBox2D>();

		Assert.IsTrue( go.Tags.Has( "skybox" ), "SkyBox2D should tag its GameObject" );
		Assert.AreEqual( skyBaseline + 1, scene.SceneWorld.SceneObjects.OfType<SceneSkyBox>().Count(), "Enabling should create a SceneSkyBox" );

		var so = scene.SceneWorld.SceneObjects.OfType<SceneSkyBox>().Single();

		sky.Tint = new Color( 0.25f, 0.5f, 0.75f );

		Assert.AreEqual( 0.25f, so.SkyTint.r, 0.01f );
		Assert.AreEqual( 0.5f, so.SkyTint.g, 0.01f );
		Assert.AreEqual( 0.75f, so.SkyTint.b, 0.01f );

		sky.Enabled = false;

		Assert.AreEqual( skyBaseline, scene.SceneWorld.SceneObjects.OfType<SceneSkyBox>().Count(), "Disabling should delete the SceneSkyBox" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//The SkyMaterial setter takes a material on a sky shader and refuses any other, keeping the one
	//it had: a surface shader drawn as the sky is a black screen with no way back from the inspector.
	[TestMethod]
	public void SkyBoxTakesOnlySkyMaterials()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var sky = go.Components.Create<SkyBox2D>();

		Assert.AreEqual( SkyBox2D.DefaultMaterial, sky.SkyMaterial.Name, "The engine's own sky is what a new SkyBox2D draws with" );

		sky.SkyMaterial = Material.Load( "materials/default.mat" );

		Assert.AreEqual( SkyBox2D.DefaultMaterial, sky.SkyMaterial.Name, "A surface material is refused" );

		var overcast = Material.FromShader( "shaders/engine/sky.shader" );

		sky.SkyMaterial = overcast;

		Assert.AreSame( overcast, sky.SkyMaterial, "A material on a sky shader is taken" );
		Assert.IsTrue( scene.SceneWorld.SceneObjects.OfType<SceneSkyBox>().Any( one => one.SkyMaterial == overcast ),
			"and is what the scene's sky draws with" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//A SkyBox2D's tint and sky material survive a serialize/deserialize round trip, and the clone creates a SceneSkyBox of its own.
	[TestMethod]
	public void SkyBoxSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var skyBaseline = scene.SceneWorld.SceneObjects.OfType<SceneSkyBox>().Count();

		var go = scene.CreateObject();
		var sky = go.Components.Create<SkyBox2D>();
		sky.Tint = new Color( 0.5f, 0.25f, 0.125f );

		var materialName = sky.SkyMaterial.Name;

		var clone = RenderTestUtility.SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<SkyBox2D>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a SkyBox2D" );
		Assert.AreEqual( new Color( 0.5f, 0.25f, 0.125f ), loaded.Tint );
		Assert.AreEqual( materialName, loaded.SkyMaterial.Name, "Sky material should round trip by path" );

		Assert.AreEqual( skyBaseline + 1, scene.SceneWorld.SceneObjects.OfType<SceneSkyBox>().Count(), "Enabled clone should create its SceneSkyBox" );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	//Enabling a Decal creates a DecalSceneObject with definition state, sort layer and bounds
	[TestMethod]
	public void DecalSceneObjectState()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var decal = go.Components.Create<Decal>( false );
		decal.Decals.Add( new DecalDefinition { ColorTexture = Texture.White } );
		decal.Size = new Vector2( 2, 4 );
		decal.Depth = 16.0f;
		decal.AttenuationAngle = 0.5f;
		decal.SortLayer = 5;
		decal.Enabled = true;

		var so = scene.SceneWorld.SceneObjects.OfType<DecalSceneObject>().Single();

		Assert.IsTrue( so.RenderingEnabled, "A decal with a definition should be rendering" );
		Assert.AreEqual( 1.0f, so.Color.r, 0.01f, "White definition tint x white color tint should stay white" );
		Assert.AreEqual( 1.0f, so.Color.a, 0.01f );
		Assert.AreEqual( 1.0f, so.ColorMix, 0.01f );
		Assert.AreEqual( 0.5f, so.AttenuationAngle, 0.001f );
		Assert.AreEqual( 0.25f, so.ParallaxStrength, 0.01f, "Parallax 1 x definition 1 x 0.25 scale factor" );
		Assert.AreEqual( 0u, so.SequenceIndex, "Sheet sequences are off by default" );
		Assert.AreEqual( 5u, so.SortOrder >> 24, "SortLayer should occupy the top byte of the sort order" );

		//Size (2,4) x definition 16x16, depth 16 => (32, 64, 16) world volume, the depth riding the -z it projects along.
		Assert.IsTrue( decal.WorldBounds.Size.Distance( new Vector3( 32, 64, 16 ) ) < 0.01f, "World bounds derive from depth, size and the definition dimensions" );

		Assert.IsTrue( ((Component.ITemporaryEffect)decal).IsActive, "A decal with no lifetime stays active" );

		decal.SheetSequence = true;
		decal.SequenceId = 3;

		Assert.AreEqual( 3u, so.SequenceIndex, "Sequence id should write through when sheet sequences are on" );

		decal.Enabled = false;

		Assert.IsFalse( so.IsValid(), "Disabling should delete the decal scene object" );
		Assert.AreEqual( 0, scene.SceneWorld.SceneObjects.OfType<DecalSceneObject>().Count() );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//A Decal with an empty definition list enables safely: the scene object exists but has
	//rendering disabled, survives ticks and repeated enable cycles, and tears down cleanly.
	[TestMethod]
	public void DecalEmptyListSafety()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var decal = go.Components.Create<Decal>();

		var so = scene.SceneWorld.SceneObjects.OfType<DecalSceneObject>().Single();
		Assert.IsFalse( so.RenderingEnabled, "No definitions means nothing to render" );

		scene.GameTick();
		Assert.IsFalse( so.RenderingEnabled );

		decal.Enabled = false;
		decal.Enabled = true;
		scene.GameTick();

		Assert.AreEqual( 1, scene.SceneWorld.SceneObjects.OfType<DecalSceneObject>().Count(), "Enable cycles should not leak scene objects" );

		go.Destroy();
		scene.ProcessDeletes();

		Assert.AreEqual( 0, scene.SceneWorld.SceneObjects.OfType<DecalSceneObject>().Count() );
	}

	//Decal placement and lifetime properties - size, depth, attenuation, sort layer, looping, sheet
	//sequence settings and the ParticleFloat lifetime - survive a serialize/deserialize round trip.
	[TestMethod]
	public void DecalSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var decal = go.Components.Create<Decal>();
		decal.Size = new Vector2( 8, 12 );
		decal.Depth = 24.0f;
		decal.AttenuationAngle = 0.25f;
		decal.SortLayer = 7;
		decal.Looped = true;
		decal.SheetSequence = true;
		decal.SequenceId = 3;
		decal.LifeTime = 2.0f;

		var clone = RenderTestUtility.SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<Decal>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a Decal" );
		Assert.AreEqual( new Vector2( 8, 12 ), loaded.Size );
		Assert.AreEqual( 24.0f, loaded.Depth );
		Assert.AreEqual( 0.25f, loaded.AttenuationAngle );
		Assert.AreEqual( 7u, loaded.SortLayer );
		Assert.IsTrue( loaded.Looped );
		Assert.IsTrue( loaded.SheetSequence );
		Assert.AreEqual( 3u, loaded.SequenceId );
		Assert.AreEqual( 2.0f, loaded.LifeTime.Evaluate( 0f, 0f ), 0.001f, "Constant lifetime should round trip" );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	//GradientFog writes scene world fog in pre-render; fog resets each tick, so disabling turns it off
	[TestMethod]
	public void GradientFogWritesToSceneWorldFog()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		Assert.IsFalse( scene.SceneWorld.GradientFog.Enabled, "Fog should start disabled" );

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 0, 0, 64 );

		var fog = go.Components.Create<GradientFog>();
		fog.Color = new Color( 0.5f, 0.25f, 0.1f, 0.5f );
		fog.StartDistance = 10.0f;
		fog.EndDistance = 500.0f;
		fog.FalloffExponent = 2.0f;
		fog.VerticalFalloffExponent = 3.0f;
		fog.Height = 100.0f;

		scene.GameTick();

		var setup = scene.SceneWorld.GradientFog;
		Assert.IsTrue( setup.Enabled, "Pre-render should enable the world fog" );
		Assert.AreEqual( 10.0f, setup.StartDistance );
		Assert.AreEqual( 500.0f, setup.EndDistance );
		Assert.AreEqual( new Color( 0.5f, 0.25f, 0.1f, 1.0f ), setup.Color, "Fog color is written with full alpha" );
		Assert.AreEqual( 0.5f, setup.MaximumOpacity, "The color alpha becomes the maximum opacity" );
		Assert.AreEqual( 2.0f, setup.DistanceFalloffExponent );
		Assert.AreEqual( 3.0f, setup.VerticalFalloffExponent );
		Assert.AreEqual( 64.0f, setup.StartHeight, "Fog starts at the GameObject height" );
		Assert.AreEqual( 164.0f, setup.EndHeight, "Fog ends Height units above the GameObject" );

		fog.Enabled = false;
		scene.GameTick();

		Assert.IsFalse( scene.SceneWorld.GradientFog.Enabled, "With the component disabled the per-tick reset leaves fog off" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//GradientFog's color, distances, exponents and height survive a serialize/deserialize round trip.
	[TestMethod]
	public void GradientFogSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var fog = go.Components.Create<GradientFog>();
		fog.Color = new Color( 0.1f, 0.2f, 0.3f, 0.4f );
		fog.StartDistance = 32.0f;
		fog.EndDistance = 2048.0f;
		fog.FalloffExponent = 1.5f;
		fog.VerticalFalloffExponent = 2.5f;
		fog.Height = 320.0f;

		var clone = RenderTestUtility.SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<GradientFog>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a GradientFog" );
		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f, 0.4f ), loaded.Color );
		Assert.AreEqual( 32.0f, loaded.StartDistance );
		Assert.AreEqual( 2048.0f, loaded.EndDistance );
		Assert.AreEqual( 1.5f, loaded.FalloffExponent );
		Assert.AreEqual( 2.5f, loaded.VerticalFalloffExponent );
		Assert.AreEqual( 320.0f, loaded.Height );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	//Enabling a BeamEffect spawns InitialBurst hidden LineRenderer components on the same GameObject and reports itself active.
	//Without looping the beams expire after their lifetime: the renderers are destroyed and the effect becomes inactive.
	[TestMethod]
	public void BeamEffectBurstAndExpiry()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var beam = go.Components.Create<BeamEffect>( false );
		beam.TargetPosition = new Vector3( 100, 0, 0 );
		beam.InitialBurst = 2;
		beam.MaxBeams = 4;
		beam.BeamLifetime = 0.3f;
		beam.Looped = false;
		beam.Enabled = true;

		Assert.AreEqual( 2, go.Components.GetAll<LineRenderer>().Count(), "InitialBurst should spawn one LineRenderer per beam" );
		Assert.IsTrue( ((Component.ITemporaryEffect)beam).IsActive, "Live beams mean the effect is active" );

		for ( int i = 0; i < 6; i++ )
		{
			scene.GameTick();
		}

		Assert.AreEqual( 0, go.Components.GetAll<LineRenderer>().Count(), "Expired beams should destroy their renderers" );
		Assert.IsFalse( ((Component.ITemporaryEffect)beam).IsActive, "No beams means the effect is inactive" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//A looping beam respawns in place when it expires - the same renderer keeps living well past the
	//beam lifetime - until DisableLooping is called, after which the next expiry kills it for good.
	[TestMethod]
	public void BeamEffectLoopedRespawnUntilDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var beam = go.Components.Create<BeamEffect>( false );
		beam.TargetPosition = new Vector3( 0, 100, 0 );
		beam.InitialBurst = 1;
		beam.BeamLifetime = 0.2f;
		beam.Looped = true;
		beam.Enabled = true;

		for ( int i = 0; i < 8; i++ )
		{
			scene.GameTick();
		}

		Assert.AreEqual( 1, go.Components.GetAll<LineRenderer>().Count(), "A looped beam should survive past its lifetime" );
		Assert.IsTrue( ((Component.ITemporaryEffect)beam).IsActive );

		((Component.ITemporaryEffect)beam).DisableLooping();

		for ( int i = 0; i < 5; i++ )
		{
			scene.GameTick();
		}

		Assert.AreEqual( 0, go.Components.GetAll<LineRenderer>().Count(), "After looping is disabled the beam should die at its next expiry" );
		Assert.IsFalse( ((Component.ITemporaryEffect)beam).IsActive );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//With BeamsPerSecond set, OnUpdate spawns at most one beam per tick once the spawn interval has elapsed, and never exceeds MaxBeams.
	[TestMethod]
	public void BeamEffectSpawnRateCappedByMaxBeams()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var beam = go.Components.Create<BeamEffect>( false );
		beam.TargetPosition = new Vector3( 50, 0, 0 );
		beam.InitialBurst = 0;
		beam.BeamsPerSecond = 10.0f;
		beam.MaxBeams = 3;
		beam.BeamLifetime = 10.0f;
		beam.Enabled = true;

		Assert.AreEqual( 0, go.Components.GetAll<LineRenderer>().Count(), "No initial burst means no beams on enable" );

		for ( int i = 0; i < 6; i++ )
		{
			scene.GameTick();
		}

		Assert.AreEqual( 3, go.Components.GetAll<LineRenderer>().Count(), "Spawning should cap at MaxBeams" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//BeamEffect targeting, spawning and rendering configuration survives a serialize/ deserialize round trip - the per-beam
	//LineRenderers are flagged NotSaved so only the effect itself round trips, and the enabled clone spawns a fresh initial burst.
	[TestMethod]
	public void BeamEffectSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var beam = go.Components.Create<BeamEffect>();
		beam.TargetPosition = new Vector3( 10, 20, 30 );
		beam.FollowPoints = false;
		beam.InitialBurst = 2;
		beam.MaxBeams = 4;
		beam.Looped = true;
		beam.Additive = true;
		beam.Opaque = true;
		beam.DepthFeather = 8.0f;
		beam.BeamLifetime = 3.0f;

		var clone = RenderTestUtility.SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<BeamEffect>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a BeamEffect" );
		Assert.AreEqual( new Vector3( 10, 20, 30 ), loaded.TargetPosition );
		Assert.IsFalse( loaded.FollowPoints );
		Assert.AreEqual( 2, loaded.InitialBurst );
		Assert.AreEqual( 4, loaded.MaxBeams );
		Assert.IsTrue( loaded.Looped );
		Assert.IsTrue( loaded.Additive );
		Assert.IsTrue( loaded.Opaque );
		Assert.AreEqual( 8.0f, loaded.DepthFeather );
		Assert.AreEqual( 3.0f, loaded.BeamLifetime.Evaluate( 0f, 0f ), 0.001f );

		Assert.AreEqual( 2, clone.Components.GetAll<LineRenderer>().Count(), "The enabled clone should spawn its own initial burst" );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	//TemporaryEffect destroys its GameObject once it has been alive longer than
	//DestroyAfterSeconds - it survives early ticks and is gone after enough scene time has passed.
	[TestMethod]
	public void TemporaryEffectDestroysAfterTime()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var te = go.Components.Create<TemporaryEffect>();
		te.DestroyAfterSeconds = 0.25f;

		scene.GameTick();

		Assert.IsTrue( go.IsValid(), "The object should survive before the timeout" );

		for ( int i = 0; i < 5; i++ )
		{
			scene.GameTick();
		}

		Assert.IsFalse( go.IsValid(), "The object should be destroyed after DestroyAfterSeconds" );
	}

	//With WaitForChildEffects on, TemporaryEffect refuses to destroy the GameObject while any ITemporaryEffect on it is still active - a
	//looping BeamEffect keeps it alive far past the timeout until looping is disabled, after which the beam dies and the object is destroyed.
	[TestMethod]
	public void TemporaryEffectWaitsForActiveEffects()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var te = go.Components.Create<TemporaryEffect>();
		te.DestroyAfterSeconds = 0.1f;
		te.WaitForChildEffects = true;

		var beam = go.Components.Create<BeamEffect>( false );
		beam.TargetPosition = new Vector3( 100, 0, 0 );
		beam.InitialBurst = 1;
		beam.BeamLifetime = 0.2f;
		beam.Looped = true;
		beam.Enabled = true;

		for ( int i = 0; i < 8; i++ )
		{
			scene.GameTick();
		}

		Assert.IsTrue( go.IsValid(), "An active looping effect should block destruction" );

		Component.ITemporaryEffect.DisableLoopingEffects( go );

		for ( int i = 0; i < 8; i++ )
		{
			scene.GameTick();
		}

		Assert.IsFalse( go.IsValid(), "Once the effect finishes the object should be destroyed" );
	}

	//TemporaryEffect's configuration fields - the destroy delay, the wait switch and the orphan behavior - survive a serialize/deserialize round trip.
	[TestMethod]
	public void TemporaryEffectSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var te = go.Components.Create<TemporaryEffect>();
		te.DestroyAfterSeconds = 5.0f;
		te.WaitForChildEffects = false;
		te.BecomeOrphan = true;

		var clone = RenderTestUtility.SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<TemporaryEffect>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a TemporaryEffect" );
		Assert.AreEqual( 5.0f, loaded.DestroyAfterSeconds );
		Assert.IsFalse( loaded.WaitForChildEffects );
		Assert.IsTrue( loaded.BecomeOrphan );

		clone.Destroy();
		scene.ProcessDeletes();
	}
}
