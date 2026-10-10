using System;

namespace EngineTests.Core;

//The drawer's cached per-renderer queue is invalidated by every change; first two frames fill it
[TestClass]
[DoNotParallelize]
public class QueueReuseTests
{
	private const int Size = 64;

	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void WhatChangedIsDrawnChanged()
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
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1, 1, -1 ).Normal );

			var box = scene.CreateObject();
			box.WorldPosition = Vector3.Zero;

			var renderer = box.Components.Create<ModelRenderer>();
			renderer.Model = model;

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

			view = CoreRenderDevice.CreateView( Size, Size );
			Assert.AreNotEqual( 0ul, view );

			var drawer = new CoreSceneDrawer { Watched = false };
			var errorsBefore = CoreErrors.Count;
			var pixels = new byte[Size * Size * 4];

			void Draw()
			{
				Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ),
					$"the view would not begin: {CoreRenderDevice.Description}" );

				try { drawer.Draw( scene, camera, view, Size, Size ); }
				finally { Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) ); }

				Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );
			}

			//Blue and only blue is what nothing looks like.
			bool EmptyAt( int x, int y )
			{
				var at = ( ( y * Size ) + x ) * 4;

				return pixels[at + 2] > 200 && pixels[at] < 40 && pixels[at + 1] < 40;
			}

			bool Empty() => EmptyAt( Size / 2, Size / 2 );

			(int Red, int Blue) Middle()
			{
				var at = ( ( ( Size / 2 ) * Size ) + ( Size / 2 ) ) * 4;

				return ( pixels[at], pixels[at + 2] );
			}

			//Two frames to fill the memory, and the second is the one that reads it back.
			Draw();
			Draw();

			Assert.IsFalse( Empty(), "the box never reached the screen at all" );

			//A corner the box does not reach, which is how a move can be seen rather than merely survived: something culled by its sphere
			//would leave the same picture whether what was written down was stale or not, and something moved towards the camera cannot.
			Assert.IsTrue( EmptyAt( 8, 8 ), "the box already covers the corner" );

			//Moved, and still on screen - a metre from the eye it fills the view. Drawn from what was
			//written down, it would still be a metre away in the middle and the corner would stay empty.
			box.WorldPosition = new Vector3( -2.0f, 0, 0 );

			Draw();

			Assert.IsFalse( EmptyAt( 8, 8 ),
				"the box moved up to the camera and is still being drawn where it was" );

			box.WorldPosition = Vector3.Zero;

			Draw();

			Assert.IsTrue( EmptyAt( 8, 8 ), "the box went back and the corner did not" );
			Assert.IsFalse( Empty(), "the box came back and is not being drawn" );

			//And out of the view entirely, which is the culling sphere's own path rather than this one.
			box.WorldPosition = new Vector3( 0, 40, 0 );

			Draw();

			Assert.IsTrue( Empty(), "the box left the view and is still drawn" );

			box.WorldPosition = Vector3.Zero;

			Draw();

			//Red tint vs blue: the missing texture has no green, so a green tint would make the box vanish
			renderer.Tint = new Color( 1.0f, 0.0f, 0.0f );

			Draw();
			Draw();

			var red = Middle();

			renderer.Tint = new Color( 0.0f, 0.0f, 1.0f );

			Draw();

			var blue = Middle();

			Assert.IsTrue( red.Red > red.Blue,
				$"a red box came out {red.Red}/{red.Blue}" );

			Assert.IsTrue( blue.Blue > blue.Red,
				$"the tint changed to blue and the box came out {blue.Red}/{blue.Blue}" );

			renderer.Tint = Color.White;

			Draw();
			Draw();

			Assert.IsFalse( Empty(), "the box is gone after a tint" );

			//Turned off a part at a time. Nothing moved here either.
			renderer.BodyGroups = 0;

			Draw();

			Assert.IsTrue( Empty(),
				"every body group is off and the box is still there" );

			renderer.BodyGroups = ulong.MaxValue;

			Draw();

			Assert.IsFalse( Empty(), "the body groups came back and the box did not" );

			//And no model at all.
			renderer.Model = null;

			Draw();

			Assert.IsTrue( Empty(), "the model was taken away and the box is still there" );

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
