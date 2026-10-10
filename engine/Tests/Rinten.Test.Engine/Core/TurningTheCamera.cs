using System;

namespace EngineTests.Core;

//Turning the camera must not cull everything; culling is one wrong sign from drawing nothing
[TestClass]
[DoNotParallelize]
public class TurningTheCameraTests
{
	private const int Size = 64;

	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void TheMeshesStayThroughAFullTurn()
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

			//A ring of boxes around where the camera stands, so that whichever way it faces there is something in front of it.
			for ( var i = 0; i < 24; i++ )
			{
				var angle = i * ( MathF.PI * 2.0f / 24.0f );

				var box = scene.CreateObject();

				box.WorldPosition = new Vector3(
					MathF.Cos( angle ) * 6.0f, MathF.Sin( angle ) * 6.0f, 0 );

				box.WorldScale = new Vector3( 2, 2, 2 );
				box.Components.Create<ModelRenderer>().Model = model;
			}

			var eye = scene.CreateObject();
			eye.WorldPosition = Vector3.Zero;

			var camera = eye.Components.Create<CameraComponent>();

			camera.BackgroundColor = new Color( 0.0f, 0.0f, 1.0f );
			camera.ZNear = 0.1f;
			camera.ZFar = 200.0f;
			camera.FieldOfView = 70.0f;

			if ( !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( $"the drawer has no pipelines here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( Size, Size );
			Assert.AreNotEqual( 0ul, view );

			var drawer = new CoreSceneDrawer();
			var errorsBefore = CoreErrors.Count;
			var pixels = new byte[Size * Size * 4];

			//How much of the picture is not the background, as a fraction.
			double Covered()
			{
				Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) );

				try { drawer.Draw( scene, camera, view, Size, Size ); }
				finally { Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) ); }

				Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

				var drawn = 0;

				for ( var i = 0; i < pixels.Length; i += 4 )
				{
					var background = pixels[i + 2] > 200 && pixels[i] < 40 && pixels[i + 1] < 40;

					if ( !background ) drawn++;
				}

				return drawn / (double)( Size * Size );
			}

			var least = 1.0;
			var worst = 0.0f;

			//Round it goes, a few degrees at a time. Twice round, because the second time every frame is one
			//where the frame before it looked somewhere else - which is when what is kept between frames counts.
			for ( var step = 0; step < 48; step++ )
			{
				var yaw = step * 15.0f;

				eye.WorldRotation = Rotation.From( 0, yaw, 0 );

				var covered = Covered();

				if ( covered < least )
				{
					least = covered;
					worst = yaw;
				}
			}

			Console.WriteLine( $"TURN least covered {least:P1} at {worst:F0} degrees" );

			Assert.IsTrue( least > 0.02,
				$"the screen went all but empty at {worst:F0} degrees - {least:P1} of it was drawn on. " +
				"A ring of boxes around the camera always has some of itself in front of it" );

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
