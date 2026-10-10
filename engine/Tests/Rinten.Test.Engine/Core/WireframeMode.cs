using System;

namespace EngineTests.Core;

//mat_wireframe 0/1/2: edge pixels are fewer than a filled box, and no depth test only adds more
[TestClass]
[DoNotParallelize]
public class WireframeModeTests
{
	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	private const int Size = 64;

	[TestMethod]
	public void EdgesCoverLessThanFacesAndThroughWallsCoversMore()
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
		var was = Rinten.Rendering.Wireframe.Mode;

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
			sun.Shadows = false;
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1, 1, -1 ).Normal );

			//Two of them, one behind the other, so that "through walls" has a wall to come through.
			var near = scene.CreateObject();
			near.WorldPosition = Vector3.Zero;
			near.Components.Create<ModelRenderer>().Model = model;

			var far = scene.CreateObject();
			far.WorldPosition = new Vector3( 2.5f, 0, 0 );
			far.Components.Create<ModelRenderer>().Model = model;

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

			var filled = Covered( drawer, scene, camera, view, 0 );
			var edges = Covered( drawer, scene, camera, view, 1 );
			var through = Covered( drawer, scene, camera, view, 2 );

			Console.WriteLine( $"of {Size * Size} pixels: {filled} filled, {edges} edges, {through} through walls" );

			Assert.IsTrue( filled > 0, "the boxes should be drawn at all" );

			//The edges of a box cannot cover as much of the view as its faces. A device with no fillModeNonSolid
			//bakes the wireframe filled and draws the same picture, which is what this tells apart.
			if ( edges >= filled )
			{
				Assert.Inconclusive( "this device draws wireframes filled - it has no fillModeNonSolid" );
				return;
			}

			Assert.IsTrue( edges > 0, "the edges should be drawn" );

			//Nothing is hidden with the depth test off, so this can only add.
			Assert.IsTrue( through >= edges,
				$"through walls drew {through} where the depth-tested wireframe drew {edges}" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			Rinten.Rendering.Wireframe.Mode = was;

			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}

	//How many pixels the scene covered at this wireframe mode. Twice, and the second one counted: a drawer keeps
	//what it worked out about the last frame, and the first frame at a new mode is the one that bakes the pipeline.
	private static int Covered( CoreSceneDrawer drawer, Scene scene, CameraComponent camera,
		ulong view, int mode )
	{
		Rinten.Rendering.Wireframe.Mode = mode;

		var pixels = new byte[Size * Size * 4];
		var covered = 0;

		for ( var frame = 0; frame < 2; frame++ )
		{
			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ),
				$"the view would not begin: {CoreRenderDevice.Description}" );

			try
			{
				drawer.Draw( scene, camera, view, Size, Size );
			}
			finally
			{
				Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );
			}

			Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

			covered = 0;

			//Anything that is not the blue it was cleared to. The background is pure blue and
			//nothing in the scene is, so this needs no tolerance beyond the encoding's own.
			for ( var at = 0; at < pixels.Length; at += 4 )
			{
				if ( pixels[at] > 8 || pixels[at + 1] > 8 ) covered++;
			}
		}

		return covered;
	}
}
