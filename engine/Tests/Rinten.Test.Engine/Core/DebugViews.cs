using System;

namespace EngineTests.Core;

//Debug views (albedo, roughness, normals) are a surface shader switch; checks the mode changes output
[TestClass]
[DoNotParallelize]
public class DebugViewTests
{
	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	private const int Size = 64;

	[TestMethod]
	public void EachViewDrawsSomethingOfItsOwn()
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
			sun.Shadows = false;
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1, 1, -1 ).Normal );

			var box = scene.CreateObject();
			box.WorldPosition = Vector3.Zero;
			box.Components.Create<ModelRenderer>().Model = model;

			//Straight down the X axis, so the face being looked at has a normal of (-1, 0, 0) and the encoding of that has no red in it.
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

			var lit = Middle( drawer, scene, camera, view, SceneCameraDebugMode.Normal );
			var albedo = Middle( drawer, scene, camera, view, SceneCameraDebugMode.Albedo );
			var normals = Middle( drawer, scene, camera, view, SceneCameraDebugMode.NormalMap );
			var rough = Middle( drawer, scene, camera, view, SceneCameraDebugMode.Roughness );

			Console.WriteLine( $"lit {lit}, albedo {albedo}, normals {normals}, roughness {rough}" );

			//Not the background: the box is there in every view.
			Assert.IsTrue( lit.b < 250 || lit.r > 8, "the box should be drawn at all" );

			Assert.AreNotEqual( ( lit.r, lit.g, lit.b ), ( albedo.r, albedo.g, albedo.b ),
				"full albedo should not be the shaded colour" );

			Assert.AreNotEqual( ( lit.r, lit.g, lit.b ), ( normals.r, normals.g, normals.b ),
				"world normals should not be the shaded colour" );

			//(-1, 0, 0) encoded is (0, 0.5, 0.5): no red, and as much green as blue. Whatever
			//the tonemapper does to the halves, the red stays the smallest of the three.
			Assert.IsTrue( normals.r < normals.g && normals.r < normals.b,
				$"a face looking back at the camera should have the least red: {normals}" );

			//Roughness is one number in all three channels, so whatever it comes out as, it comes out grey.
			Assert.AreEqual( rough.r, rough.g, 6, $"roughness should be grey: {rough}" );
			Assert.AreEqual( rough.g, rough.b, 6, $"roughness should be grey: {rough}" );

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

	//The middle pixel of the view, drawn in this mode.
	private static (byte r, byte g, byte b) Middle( CoreSceneDrawer drawer, Scene scene,
		CameraComponent camera, ulong view, SceneCameraDebugMode mode )
	{
		camera.DebugMode = mode;

		var pixels = new byte[Size * Size * 4];

		//Twice: a drawer keeps what it worked out about the last frame, and the first frame in a new mode is the one that bakes anything missing.
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
		}

		var at = ( ( ( Size / 2 ) * Size ) + ( Size / 2 ) ) * 4;

		return (pixels[at], pixels[at + 1], pixels[at + 2]);
	}
}
