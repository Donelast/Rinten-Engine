using System;

namespace EngineTests.Core;

//Self-drawing custom objects are culled by their bounds like every other object
[TestClass]
[DoNotParallelize]
public class CustomObjectCullingTests
{
	private const int Size = 64;

	//An object with no bounds set gets a zero box at its position; it must still be drawn
	[TestMethod]
	public void SomethingThatNeverSaidWhereItIsIsStillDrawn()
	{
		ThreadSafe.MarkMainThread();

		if ( !CoreNative.Available ) { Assert.Inconclusive( "no core" ); return; }
		if ( !CoreRenderDevice.Initialize() ) { Assert.Inconclusive( "no device" ); return; }

		var view = 0ul;

		try
		{
			if ( !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( $"the drawer has no pipelines here: {CoreRenderDevice.Description}" );
				return;
			}

			var scene = new Scene();
			using var scope = scene.Push();

			//Looking away from the origin, which is where an object that never moved stands and where its stand-in bounds would put it.
			var eye = scene.CreateObject();
			eye.WorldPosition = new Vector3( 0, 0, 0 );
			eye.WorldRotation = Rotation.LookAt( Vector3.Forward );

			var camera = eye.Components.Create<CameraComponent>();

			camera.BackgroundColor = Color.Black;
			camera.ZNear = 0.1f;
			camera.ZFar = 100.0f;
			camera.FieldOfView = 70.0f;

			var drew = 0;

			//Nothing set on it but what it draws - which is what a game that draws by hand
			//writes, and what every one of these was before anybody thought about culling them.
			var quiet = new SceneCustomObject( scene.SceneWorld )
			{
				RenderOverride = _ => drew++,
			};

			//Behind the camera on purpose: it says nothing about where it is, so where it is must not be held against it.
			quiet.Position = new Vector3( -30, 0, 0 );

			Assert.IsTrue( quiet.IsValid() );

			view = CoreRenderDevice.CreateView( Size, Size );

			Assert.AreNotEqual( 0ul, view );

			var drawer = new CoreSceneDrawer { Watched = false };

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) );

			try
			{
				drawer.Draw( scene, camera, view, Size, Size );
			}
			finally
			{
				Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );
			}

			Assert.AreEqual( 1, drew, "it said nothing about where it is, so it draws" );
		}
		finally
		{
			if ( view != 0 ) CoreRenderDevice.DestroyView( view );

			CoreResources.Clear();
			CoreRenderDevice.Shutdown();
		}
	}

	[TestMethod]
	public void SomethingBehindTheCameraDoesNotDrawItself()
	{
		ThreadSafe.MarkMainThread();

		if ( !CoreNative.Available ) { Assert.Inconclusive( "no core" ); return; }
		if ( !CoreRenderDevice.Initialize() ) { Assert.Inconclusive( "no device" ); return; }

		var view = 0ul;

		try
		{
			if ( !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( $"the drawer has no pipelines here: {CoreRenderDevice.Description}" );
				return;
			}

			var scene = new Scene();
			using var scope = scene.Push();

			var eye = scene.CreateObject();
			eye.WorldPosition = Vector3.Zero;
			eye.WorldRotation = Rotation.LookAt( Vector3.Forward );

			var camera = eye.Components.Create<CameraComponent>();

			camera.BackgroundColor = Color.Black;
			camera.ZNear = 0.1f;
			camera.ZFar = 100.0f;
			camera.FieldOfView = 70.0f;

			var ahead = 0;
			var behind = 0;

			//One in front of the camera and one behind it, both the same size, so what tells them apart is where they are and nothing else.
			var seen = new SceneCustomObject( scene.SceneWorld )
			{
				Bounds = BBox.FromPositionAndSize( new Vector3( 5, 0, 0 ), 2.0f ),
				RenderOverride = _ => ahead++,
			};

			var hidden = new SceneCustomObject( scene.SceneWorld )
			{
				Bounds = BBox.FromPositionAndSize( new Vector3( -20, 0, 0 ), 2.0f ),
				RenderOverride = _ => behind++,
			};

			Assert.IsTrue( seen.IsValid() && hidden.IsValid() );

			view = CoreRenderDevice.CreateView( Size, Size );

			Assert.AreNotEqual( 0ul, view );

			var drawer = new CoreSceneDrawer { Watched = false };

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) );

			try
			{
				drawer.Draw( scene, camera, view, Size, Size );
			}
			finally
			{
				Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );
			}

			Console.WriteLine( $"CUSTOM ahead {ahead}, behind {behind}" );

			Assert.AreEqual( 1, ahead, "the one in front of the camera should have been asked to draw" );
			Assert.AreEqual( 0, behind, "and the one behind it should not have been" );
		}
		finally
		{
			if ( view != 0 ) CoreRenderDevice.DestroyView( view );

			CoreResources.Clear();
			CoreRenderDevice.Shutdown();
		}
	}
}
