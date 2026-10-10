using System;

namespace EngineTests.Core;

//Effects run only when a component asks; settings set quality. No effects means identical bytes
[TestClass]
[DoNotParallelize]
public class EffectComponentTests
{
	private const int Size = 96;

	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void AnEffectNobodyAskedForDoesNotHappen()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		if ( !CoreRenderDevice.Initialize( validation: true ) )
		{
			Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
			return;
		}

		var wasOcclusion = AmbientOcclusion.UserQuality;
		var wasReflections = Rinten.ScreenSpaceReflections.Reflections;

		//Pinned, because the exposure follows the scene and therefore never draws quite the same picture
		//twice - see r_exposure_auto, which says so itself. Comparing two pictures needs it held still.

		var view = 0ul;

		try
		{
			var model = Model.FromCore( CoreModel.Load( "models/dev/box.vmdl" ) );

			if ( model is null )
			{
				Assert.Inconclusive( "the built-in box did not load" );
				return;
			}

			var scene = new Scene();
			using var scope = scene.Push();

			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.Shadows = true;
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1, 1, -1 ).Normal );

			//A corner, because that is what occlusion darkens: a wide floor and a box
			//standing on it, seen from low down so the join fills a good part of the picture.
			var floor = scene.CreateObject();
			floor.WorldPosition = new Vector3( 0, 0, -1.0f );
			floor.WorldScale = new Vector3( 20, 20, 1 );
			floor.Components.Create<ModelRenderer>().Model = model;

			var box = scene.CreateObject();
			box.WorldPosition = Vector3.Zero;
			box.Components.Create<ModelRenderer>().Model = model;

			var eye = scene.CreateObject();
			eye.WorldPosition = new Vector3( -3.0f, 0, 0.2f );
			eye.WorldRotation = Rotation.LookAt( Vector3.Forward );

			var camera = eye.Components.Create<CameraComponent>();

			camera.BackgroundColor = new Color( 0.0f, 0.0f, 0.2f );
			camera.ZNear = 0.1f;
			camera.ZFar = 100.0f;
			camera.FieldOfView = 80.0f;

			if ( !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( $"the drawer has no pipelines here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( Size, Size );
			Assert.AreNotEqual( 0ul, view );

			var drawer = new CoreSceneDrawer();
			var errorsBefore = CoreErrors.Count;

			byte[] Draw()
			{
				var pixels = new byte[Size * Size * 4];

				//Twice, because half the frame's decisions are about what the frame before did.
				for ( var frame = 0; frame < 2; frame++ )
				{
					Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) );

					try { drawer.Draw( scene, camera, view, Size, Size ); }
					finally { Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) ); }
				}

				Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

				return pixels;
			}

			static double Mean( byte[] pixels )
			{
				var total = 0.0;

				for ( var i = 0; i < pixels.Length; i += 4 )
					total += pixels[i] + pixels[i + 1] + pixels[i + 2];

				return total / ( pixels.Length / 4 * 3 );
			}

			static int Differences( byte[] a, byte[] b )
			{
				var count = 0;

				for ( var i = 0; i < a.Length; i++ )
				{
					if ( a[i] != b[i] ) count++;
				}

				return count;
			}

			//Nothing on the camera. The settings are on, and they have to make no difference whatsoever.
			AmbientOcclusion.UserQuality = 3;
			Rinten.ScreenSpaceReflections.Reflections = true;

			var loud = Draw();

			AmbientOcclusion.UserQuality = 0;
			Rinten.ScreenSpaceReflections.Reflections = false;

			var quiet = Draw();

			Assert.AreEqual( 0, Differences( loud, quiet ),
				"a scene with no effects on its camera drew differently with the settings on - " +
				"the passes are reading the settings rather than the components" );

			//And with the component, the setting is what it always was: how good, not whether.
			var occlusion = eye.Components.Create<AmbientOcclusion>();

			AmbientOcclusion.UserQuality = 3;

			var asked = Draw();

			Assert.AreNotEqual( 0, Differences( loud, asked ),
				"the camera was given an ambient occlusion component and the picture did not change" );

			Assert.IsTrue( Mean( asked ) < Mean( loud ),
				$"occlusion should darken: {Mean( loud ):F2} became {Mean( asked ):F2}" );

			//Turned off at the component rather than at the setting, which is the other half of the same rule.
			occlusion.Enabled = false;

			var hushed = Draw();

			Assert.AreEqual( 0, Differences( loud, hushed ),
				"the component was disabled and its effect stayed" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			AmbientOcclusion.UserQuality = wasOcclusion;
			Rinten.ScreenSpaceReflections.Reflections = wasReflections;

			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}
}
