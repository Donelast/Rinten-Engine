using System;

namespace EngineTests.Core;

//Off-screen casters still shadow visible surfaces; the caster stands outside the camera view
[TestClass]
[DoNotParallelize]
public class ShadowsOffScreenTests
{
	private const int Size = 64;

	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void SomethingOffScreenStillCastsOntoWhatIsOnIt()
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


		//Pinned: the exposure follows the scene, and two pictures of the same floor are being compared.

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

			//The light travels along one, one, minus one - so a thing two metres up and two metres back along each of those lands its shadow at the origin.
			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.Shadows = true;
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1, 1, -1 ).Normal );

			var floor = scene.CreateObject();
			floor.WorldPosition = new Vector3( 0, 0, -0.1f );
			floor.WorldScale = new Vector3( 40, 40, 0.2f );
			floor.Components.Create<ModelRenderer>().Model = model;

			//Well outside a narrow view of the origin, and high enough that its shadow reaches it.
			var caster = scene.CreateObject();
			caster.WorldPosition = new Vector3( -5.0f, -5.0f, 5.0f );

			var casting = caster.Components.Create<ModelRenderer>();
			casting.Model = model;

			//Straight down at the floor, seeing about a metre and a half either way - which leaves the caster two metres out of shot.
			var eye = scene.CreateObject();
			eye.WorldPosition = new Vector3( 0, 0, 10.0f );
			eye.WorldRotation = Rotation.LookAt( Vector3.Down );

			var camera = eye.Components.Create<CameraComponent>();

			camera.BackgroundColor = Color.Black;
			camera.ZNear = 0.5f;
			camera.ZFar = 200.0f;
			camera.FieldOfView = 18.0f;

			if ( !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( $"the drawer has no pipelines here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( Size, Size );
			Assert.AreNotEqual( 0ul, view );

			var drawer = new CoreSceneDrawer();
			var errorsBefore = CoreErrors.Count;

			double Middle()
			{
				var pixels = new byte[Size * Size * 4];

				//Three times: the cascades are not all redrawn every frame, and what is being measured is what settles.
				for ( var frame = 0; frame < 3; frame++ )
				{
					Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) );

					try { drawer.Draw( scene, camera, view, Size, Size ); }
					finally { Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) ); }
				}

				Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

				//The middle of the picture, over a few pixels, because a cascade edge is not exactly where arithmetic says it is.
				var total = 0.0;
				var count = 0;

				for ( var y = ( Size / 2 ) - 3; y <= ( Size / 2 ) + 3; y++ )
				for ( var x = ( Size / 2 ) - 3; x <= ( Size / 2 ) + 3; x++ )
				{
					var at = ( ( y * Size ) + x ) * 4;

					total += pixels[at] + pixels[at + 1] + pixels[at + 2];
					count += 3;
				}

				return total / count;
			}

			var shaded = Middle();

			//And the same floor with nothing above it.
			casting.Enabled = false;

			var lit = Middle();

			Console.WriteLine( $"SHADOW shaded {shaded:F1}, lit {lit:F1}" );

			Assert.IsTrue( lit > 20,
				$"the floor is not lit at all ({lit:F1}) - nothing to see a shadow against" );

			Assert.IsTrue( shaded < lit * 0.9,
				$"a box off screen cast no shadow onto the floor: {shaded:F1} against {lit:F1} " +
				"with it gone - the queue is what the camera can see, and the sun walks it" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{

			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}

	//A caster that leaves the view keeps casting despite per-frame caches built while visible
	[TestMethod]
	public void ACasterThatLeavesTheViewKeepsCasting()
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

			//A shallow sun, so the shadow is long and reaches well away from the thing throwing it.
			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.Shadows = true;
			sun.WorldRotation = Rotation.LookAt( new Vector3( 0, -1.0f, -0.35f ).Normal );

			var floor = scene.CreateObject();
			floor.WorldPosition = new Vector3( 0, 0, -0.1f );
			floor.WorldScale = new Vector3( 60, 60, 0.2f );
			floor.Components.Create<ModelRenderer>().Model = model;

			var caster = scene.CreateObject();
			caster.WorldPosition = new Vector3( 0, 3.0f, 0.5f );

			var casting = caster.Components.Create<ModelRenderer>();
			casting.Model = model;

			var eye = scene.CreateObject();
			eye.WorldPosition = new Vector3( 0, 1.0f, 6.0f );

			var camera = eye.Components.Create<CameraComponent>();

			camera.BackgroundColor = Color.Black;
			camera.ZNear = 0.5f;
			camera.ZFar = 200.0f;
			camera.FieldOfView = 25.0f;

			if ( !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( $"the drawer has no pipelines here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( Size, Size );
			Assert.AreNotEqual( 0ul, view );

			var drawer = new CoreSceneDrawer();
			var errorsBefore = CoreErrors.Count;

			double Middle()
			{
				var pixels = new byte[Size * Size * 4];

				for ( var frame = 0; frame < 3; frame++ )
				{
					Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) );

					try { drawer.Draw( scene, camera, view, Size, Size ); }
					finally { Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) ); }
				}

				Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

				var total = 0.0;
				var count = 0;

				for ( var y = ( Size / 2 ) - 4; y <= ( Size / 2 ) + 4; y++ )
				for ( var x = ( Size / 2 ) - 4; x <= ( Size / 2 ) + 4; x++ )
				{
					var at = ( ( y * Size ) + x ) * 4;

					total += pixels[at] + pixels[at + 1] + pixels[at + 2];
					count += 3;
				}

				return total / count;
			}

			//Looking a little up the floor, where the caster is in shot.
			eye.WorldRotation = Rotation.LookAt( new Vector3( 0, 0.6f, -1.0f ).Normal );

			var seen = Middle();

			//And now straight down, which leaves the caster out of the view while its shadow still crosses the patch being looked at.
			eye.WorldRotation = Rotation.LookAt( Vector3.Down );

			var turned = Middle();

			//The same patch of floor, with the caster gone from the scene altogether, is what "no shadow" looks like.
			casting.Enabled = false;

			var bare = Middle();

			Console.WriteLine( $"CASTER seen {seen:F1}, turned away {turned:F1}, gone {bare:F1}" );

			Assert.IsTrue( bare > 20, $"the floor is not lit at all ({bare:F1})" );

			Assert.IsTrue( turned < bare * 0.9,
				$"the caster left the view and took its shadow with it: {turned:F1} against " +
				$"{bare:F1} with it gone from the scene" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{

			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}

	//Casters behind the camera shadow what's in front; must agree with the shadow pass culling
	[TestMethod]
	public void SomethingBehindTheCameraCastsInFrontOfIt()
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

			//Low, and travelling the way the camera looks: shadows stretch away from the eye, which is what puts them in front of it.
			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.Shadows = true;
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1.0f, 0, -0.3f ).Normal );

			var floor = scene.CreateObject();
			floor.WorldPosition = new Vector3( 0, 0, -0.1f );
			floor.WorldScale = new Vector3( 80, 80, 0.2f );
			floor.Components.Create<ModelRenderer>().Model = model;

			//Four metres behind the camera and a metre up. Its shadow lands about five metres along, which is squarely in shot.
			var caster = scene.CreateObject();
			caster.WorldPosition = new Vector3( -4.0f, 0, 1.0f );
			caster.WorldScale = new Vector3( 2, 2, 2 );

			var casting = caster.Components.Create<ModelRenderer>();
			casting.Model = model;

			var eye = scene.CreateObject();
			eye.WorldPosition = new Vector3( 0, 0, 2.0f );
			eye.WorldRotation = Rotation.LookAt( new Vector3( 1.0f, 0, -0.5f ).Normal );

			var camera = eye.Components.Create<CameraComponent>();

			camera.BackgroundColor = Color.Black;
			camera.ZNear = 0.2f;
			camera.ZFar = 200.0f;
			camera.FieldOfView = 60.0f;

			if ( !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( $"the drawer has no pipelines here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( Size, Size );
			Assert.AreNotEqual( 0ul, view );

			var drawer = new CoreSceneDrawer();
			var errorsBefore = CoreErrors.Count;

			double Middle()
			{
				var pixels = new byte[Size * Size * 4];

				for ( var frame = 0; frame < 3; frame++ )
				{
					Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) );

					try { drawer.Draw( scene, camera, view, Size, Size ); }
					finally { Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) ); }
				}

				Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

				var total = 0.0;
				var count = 0;

				for ( var y = ( Size / 2 ) - 4; y <= ( Size / 2 ) + 4; y++ )
				for ( var x = ( Size / 2 ) - 4; x <= ( Size / 2 ) + 4; x++ )
				{
					var at = ( ( y * Size ) + x ) * 4;

					total += pixels[at] + pixels[at + 1] + pixels[at + 2];
					count += 3;
				}

				return total / count;
			}

			var shaded = Middle();

			casting.Enabled = false;

			var lit = Middle();

			Console.WriteLine( $"BEHIND shaded {shaded:F1}, lit {lit:F1}" );

			Assert.IsTrue( lit > 20, $"the floor is not lit at all ({lit:F1})" );

			Assert.IsTrue( shaded < lit * 0.9,
				$"a box behind the camera cast nothing in front of it: {shaded:F1} against " +
				$"{lit:F1} with it gone" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{

			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}

	//Bare scene objects (e.g. MeshComponent) are also queued for shadows when off-screen
	[TestMethod]
	public void SomethingThatIsNotARendererCastsToo()
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
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1.0f, 0, -0.3f ).Normal );

			var floor = scene.CreateObject();
			floor.WorldPosition = new Vector3( 0, 0, -0.1f );
			floor.WorldScale = new Vector3( 80, 80, 0.2f );
			floor.Components.Create<ModelRenderer>().Model = model;

			var eye = scene.CreateObject();
			eye.WorldPosition = new Vector3( 0, 0, 2.0f );
			eye.WorldRotation = Rotation.LookAt( new Vector3( 1.0f, 0, -0.5f ).Normal );

			var camera = eye.Components.Create<CameraComponent>();

			camera.BackgroundColor = Color.Black;
			camera.ZNear = 0.2f;
			camera.ZFar = 200.0f;
			camera.FieldOfView = 60.0f;

			if ( !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( $"the drawer has no pipelines here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( Size, Size );
			Assert.AreNotEqual( 0ul, view );

			//Four metres behind the camera, put there the way a MeshComponent puts its geometry there - a scene object, no renderer.
			var caster = new SceneObject( scene.SceneWorld, model,
				new Transform( new Vector3( -4.0f, 0, 1.0f ), Rotation.Identity, 2.0f ) );

			Assert.IsTrue( caster.IsValid(), "the scene object should have been made" );

			var drawer = new CoreSceneDrawer();
			var errorsBefore = CoreErrors.Count;

			double Middle()
			{
				var pixels = new byte[Size * Size * 4];

				for ( var frame = 0; frame < 3; frame++ )
				{
					Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) );

					try { drawer.Draw( scene, camera, view, Size, Size ); }
					finally { Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) ); }
				}

				Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

				var total = 0.0;
				var count = 0;

				for ( var y = ( Size / 2 ) - 4; y <= ( Size / 2 ) + 4; y++ )
				for ( var x = ( Size / 2 ) - 4; x <= ( Size / 2 ) + 4; x++ )
				{
					var at = ( ( y * Size ) + x ) * 4;

					total += pixels[at] + pixels[at + 1] + pixels[at + 2];
					count += 3;
				}

				return total / count;
			}

			var shaded = Middle();

			caster.RenderingEnabled = false;

			var lit = Middle();

			Console.WriteLine( $"OBJECT shaded {shaded:F1}, lit {lit:F1}" );

			Assert.IsTrue( lit > 20, $"the floor is not lit at all ({lit:F1})" );

			Assert.IsTrue( shaded < lit * 0.9,
				$"a scene object behind the camera cast nothing in front of it: {shaded:F1} " +
				$"against {lit:F1} with it off" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{

			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}
}
