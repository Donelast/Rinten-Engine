using System;

namespace EngineTests.Core;

//Drives scenes through the real scene drawer, not the core, the way the thumbnail renderer does
[TestClass]
[DoNotParallelize]
public class SceneDrawerTests
{
	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	//Loosest useful check: solid geometry reaches the screen at all, regardless of colour or edges
	[TestMethod]
	public void ASolidMeshReachesTheScreen()
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
			//One of the built-in shapes, so this needs nothing on disk.
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

			var box = scene.CreateObject();
			box.WorldPosition = Vector3.Zero;
			box.Components.Create<ModelRenderer>().Model = model;

			var eye = scene.CreateObject();
			eye.WorldPosition = new Vector3( -3, 0, 0 );
			eye.WorldRotation = Rotation.LookAt( Vector3.Forward );

			var camera = eye.Components.Create<CameraComponent>();

			camera.BackgroundColor = new Color( 0.0f, 0.0f, 1.0f );
			camera.ZNear = 0.1f;
			camera.ZFar = 100.0f;
			camera.FieldOfView = 70.0f;

			if ( !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( $"the drawer has no pipelines here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( 64, 64 );
			Assert.AreNotEqual( 0ul, view );

			var drawer = new CoreSceneDrawer { Watched = false };
			var errorsBefore = CoreErrors.Count;
			var pixels = new byte[64 * 64 * 4];

			//Twice: a drawer keeps what it worked out about the last frame - which cascades it left
			//standing, what the queue looked like - and the second frame is the one that takes those paths.
			for ( var frame = 0; frame < 2; frame++ )
			{
				Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ),
					$"the view would not begin: {CoreRenderDevice.Description}" );

				try
				{
					drawer.Draw( scene, camera, view, 64, 64 );
				}
				finally
				{
					Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );
				}

				Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

				var middle = ( ( 32 * 64 ) + 32 ) * 4;

				//Blue is what nothing looks like. The box is lit by a sun from off to one side,
				//so what it looks like is the renderer's business - but it is not the background.
				var background = pixels[middle + 2] > 200 &&
					pixels[middle + 0] < 40 && pixels[middle + 1] < 40;

				Assert.IsFalse( background,
					$"frame {frame}: the middle of the view is still the background - " +
					"the mesh was queued and never drawn" );
			}

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

	//Eight copies of one model must batch into one instanced draw call; checked by call count
	[TestMethod]
	public void CopiesOfOneModelLeaveAsOneCall()
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

			if ( model is null || !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( "the built-in box or the drawer's pipelines are not here" );
				return;
			}

			var scene = new Scene();
			using var scope = scene.Push();

			const int Copies = 8;

			for ( var i = 0; i < Copies; i++ )
			{
				var box = scene.CreateObject();

				box.WorldPosition = new Vector3( 0, ( i - ( Copies / 2 ) ) * 1.5f, 0 );
				box.Components.Create<ModelRenderer>().Model = model;
			}

			var eye = scene.CreateObject();

			eye.WorldPosition = new Vector3( -14, 0, 0 );
			eye.WorldRotation = Rotation.LookAt( Vector3.Forward );

			var camera = eye.Components.Create<CameraComponent>();

			camera.BackgroundColor = Color.Black;
			camera.ZNear = 0.1f;
			camera.ZFar = 100.0f;

			view = CoreRenderDevice.CreateView( 64, 64 );

			var drawer = new CoreSceneDrawer { Watched = false };

			var read = default( CoreRenderDevice.FrameProfile );

			//Twice: the first frame has no depth to cull against and no picture to classify, the second draws the world from its clusters.
			for ( var frame = 0; frame < 2; frame++ )
			{
				Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) );

				try
				{
					drawer.Draw( scene, camera, view, 64, 64 );
				}
				finally
				{
					Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );
				}

				if ( CoreRenderDevice.ReadProfile( view ) is { } profile ) read = profile;
			}

			Assert.IsTrue( read.Indirect > 0,
				"the solid world should have gone out indirect, and none of it did" );

			Assert.IsTrue( read.Draws < Copies,
				$"and in fewer calls than copies - the frame made {read.Draws}" );
		}
		finally
		{
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}

	//A second frame of a still scene skips the shadow cascades; checked by count, not pixels
	[TestMethod]
	public void AStillSceneStopsRedrawingItsShadows()
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

			if ( model is null || !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( "the built-in box or the drawer's pipelines are not here" );
				return;
			}

			var scene = new Scene();
			using var scope = scene.Push();

			var sun = scene.CreateObject().Components.Create<DirectionalLight>();

			sun.Shadows = true;
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1, 1, -1 ).Normal );

			var box = scene.CreateObject();
			box.Components.Create<ModelRenderer>().Model = model;

			var eye = scene.CreateObject();

			eye.WorldPosition = new Vector3( -4, 0, 1 );
			eye.WorldRotation = Rotation.LookAt( new Vector3( 1, 0, -0.2f ).Normal );

			var camera = eye.Components.Create<CameraComponent>();

			camera.BackgroundColor = Color.Black;
			camera.ZNear = 0.1f;
			camera.ZFar = 100.0f;

			view = CoreRenderDevice.CreateView( 64, 64 );

			var drawer = new CoreSceneDrawer { Watched = false };
			var draws = new int[4];

			for ( var frame = 0; frame < draws.Length; frame++ )
			{
				Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) );

				try
				{
					drawer.Draw( scene, camera, view, 64, 64 );
				}
				finally
				{
					Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );
				}

				draws[frame] = CoreRenderDevice.ReadProfile( view ) is { } read ? read.Draws : -1;
			}

			if ( draws[0] <= 0 )
			{
				Assert.Inconclusive( "the frame did not report itself" );
				return;
			}

			//Some later frame must be cheaper; the backstop still redraws cascades that stood too long
			var cheapest = int.MaxValue;

			for ( var frame = 1; frame < draws.Length; frame++ )
			{
				if ( draws[frame] < cheapest ) cheapest = draws[frame];
			}

			Assert.IsTrue( cheapest < draws[0],
				$"a still scene kept redrawing its shadows: {string.Join( ", ", draws )}" );
		}
		finally
		{
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}

	//The fast world matrix (scaled rotation rows plus position) equals scale * rotation * translation
	[TestMethod]
	public void TheShortWorldMatrixIsTheLongOne()
	{
		var seed = 12345u;

		float Next( float low, float high )
		{
			seed = ( seed * 1664525u ) + 1013904223u;
			return low + ( ( seed >> 8 ) & 0xFFFF ) / 65535.0f * ( high - low );
		}

		for ( var round = 0; round < 64; round++ )
		{
			var transform = new Transform(
				new Vector3( Next( -50, 50 ), Next( -50, 50 ), Next( -50, 50 ) ),
				Rotation.From( Next( -180, 180 ), Next( -180, 180 ), Next( -180, 180 ) ),
				new Vector3( Next( 0.1f, 4 ), Next( 0.1f, 4 ), Next( 0.1f, 4 ) ) );

			var expected =
				System.Numerics.Matrix4x4.CreateScale( transform.Scale.x, transform.Scale.y, transform.Scale.z ) *
				System.Numerics.Matrix4x4.CreateFromQuaternion( new System.Numerics.Quaternion(
					transform.Rotation.x, transform.Rotation.y, transform.Rotation.z, transform.Rotation.w ) ) *
				System.Numerics.Matrix4x4.CreateTranslation(
					transform.Position.x, transform.Position.y, transform.Position.z );

			var got = CoreSceneRenderer.WorldOf( transform );

			for ( var row = 1; row <= 4; row++ )
			for ( var column = 1; column <= 4; column++ )
			{
				var a = expected[row - 1, column - 1];
				var b = got[row - 1, column - 1];

				Assert.AreEqual( a, b, 0.0005f,
					$"round {round}, element {row}{column}: expected {a}, got {b}" );
			}
		}
	}

	//The cached cull sphere must be refreshed when an object moves, or it culls from the old spot
	[TestMethod]
	public void SomethingThatMovesIsCulledFromWhereItIsNow()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		if ( !CoreRenderDevice.Initialize( validation: false ) )
		{
			Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
			return;
		}

		var view = 0ul;

		try
		{
			var model = Model.FromCore( CoreModel.Load( "models/dev/box.vmdl" ) );

			if ( model is null || !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( "no box or no pipelines" );
				return;
			}

			var scene = new Scene();
			using var scope = scene.Push();

			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1, 1, -1 ).Normal );

			//Out of view to begin with, which is the half that matters: a sphere worked out here and
			//kept says "not on the screen" for as long as it is kept, and the object never comes back.
			var box = scene.CreateObject();
			box.WorldPosition = new Vector3( -400, 0, 0 );
			box.Components.Create<ModelRenderer>().Model = model;

			var eye = scene.CreateObject();
			eye.WorldPosition = new Vector3( -3, 0, 0 );
			eye.WorldRotation = Rotation.LookAt( Vector3.Forward );

			var camera = eye.Components.Create<CameraComponent>();
			camera.BackgroundColor = new Color( 0.0f, 0.0f, 1.0f );
			camera.ZNear = 0.1f;
			camera.ZFar = 100.0f;
			camera.FieldOfView = 70.0f;

			view = CoreRenderDevice.CreateView( 64, 64 );
			Assert.AreNotEqual( 0ul, view );

			var drawer = new CoreSceneDrawer();
			var pixels = new byte[64 * 64 * 4];

			bool Drawn()
			{
				//Twice, because the first frame after a change is the one that rebuilds whatever was cached about the last.
				for ( var frame = 0; frame < 2; frame++ )
				{
					if ( !CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) ) continue;

					try { drawer.Draw( scene, camera, view, 64, 64 ); }
					finally { CoreRenderDevice.EndViewFrame( view ); }
				}

				Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

				//Anywhere in the picture, not the middle of it: what is being asked is whether the thing was
				//drawn at all, and a box that comes back into view at the edge is still a box that came back.
				for ( var at = 0; at < pixels.Length; at += 4 )
				{
					var background = pixels[at + 2] > 200 && pixels[at] < 40 && pixels[at + 1] < 40;

					if ( !background ) return true;
				}

				return false;
			}

			Assert.IsFalse( Drawn(), "the box is four hundred metres behind the camera" );

			box.WorldPosition = Vector3.Zero;
			Assert.IsTrue( Drawn(), "the box moved in front of the camera and was not drawn" );

			//Aside, and then bigger: a thing can come into view by growing as well as by moving, and the sphere has to know about both.
			box.WorldPosition = new Vector3( 0, 5, 0 );
			Assert.IsFalse( Drawn(), "the box moved aside and was drawn anyway" );

			//Not so much that it swallows the camera: a box the eye is inside shows only its back faces, which are not drawn at all.
			box.WorldScale = 4.0f;
			Assert.IsTrue( Drawn(), "the box grew back into view and was not drawn" );
		}
		finally
		{
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}

	//Effects targets (AO and blur) can be resized mid-session without destroying in-use images
	[TestMethod]
	public void TheEffectsPicturesCanChangeSizeMidSession()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		if ( !CoreRenderDevice.Initialize( validation: true ) )
		{
			Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
			return;
		}

		var view = 0ul;
		var was = Rinten.AmbientOcclusion.UserResolution;

		try
		{
			var model = Model.FromCore( CoreModel.Load( "models/dev/box.vmdl" ) );

			if ( model is null || !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( "no box or no pipelines" );
				return;
			}

			var scene = new Scene();
			using var scope = scene.Push();

			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1, 1, -1 ).Normal );

			var box = scene.CreateObject();
			box.Components.Create<ModelRenderer>().Model = model;

			var eye = scene.CreateObject();
			eye.WorldPosition = new Vector3( -3, 0, 0 );
			eye.WorldRotation = Rotation.LookAt( Vector3.Forward );

			var camera = eye.Components.Create<CameraComponent>();
			camera.BackgroundColor = new Color( 0.0f, 0.0f, 1.0f );
			camera.ZNear = 0.1f;
			camera.ZFar = 100.0f;
			camera.FieldOfView = 70.0f;

			view = CoreRenderDevice.CreateView( 128, 128 );
			Assert.AreNotEqual( 0ul, view );

			var drawer = new CoreSceneDrawer();
			var pixels = new byte[128 * 128 * 4];
			var errorsBefore = CoreErrors.Count;

			//Two frames at each size, so that the frame after the change is one that records against the pictures the change made.
			foreach ( var divisor in new[] { 2, 4, 8, 2 } )
			{
				Rinten.AmbientOcclusion.UserResolution = divisor;

				for ( var frame = 0; frame < 2; frame++ )
				{
					if ( !CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) ) continue;

					try { drawer.Draw( scene, camera, view, 128, 128 ); }
					finally { CoreRenderDevice.EndViewFrame( view ); }
				}

				Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

				var middle = ( ( 64 * 128 ) + 64 ) * 4;

				var background = pixels[middle + 2] > 200 &&
					pixels[middle] < 40 && pixels[middle + 1] < 40;

				Assert.IsFalse( background,
					$"nothing was drawn with the effects pictures at a {divisor}th of the window" );
			}

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			Rinten.AmbientOcclusion.UserResolution = was;
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}
}
