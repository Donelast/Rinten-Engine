using System;

namespace EngineTests.Core;

//Translucent box over blue: half alpha must give half the background, opaque gives the box
[TestClass]
[DoNotParallelize]
public class TransparencyTests
{
	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	private const int Size = 64;

	[TestMethod]
	public void AHalfAlphaSurfaceLetsTheBackgroundThrough()
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

			var renderer = box.Components.Create<ModelRenderer>();
			renderer.Model = model;

			var eye = scene.CreateObject();
			eye.WorldPosition = new Vector3( -3, 0, 0 );
			eye.WorldRotation = Rotation.LookAt( Vector3.Forward );

			var camera = eye.Components.Create<CameraComponent>();

			//Blue, so that anything coming through is unmistakable: nothing in the scene is blue and the box is grey.
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

			var solid = Middle( drawer, scene, camera, view );

			//The same box, asked to blend, at half alpha.
			var glass = Material.Create( $"glass-{Guid.NewGuid():N}", "shaders/standard.shader" );

			glass.SetFeature( "F_TRANSLUCENT", 1 );

			renderer.MaterialOverride = glass;
			renderer.Tint = Color.White.WithAlpha( 0.5f );

			var blended = Middle( drawer, scene, camera, view );

			Console.WriteLine( $"solid {solid}, blended {blended}" );

			Assert.IsTrue( solid.b < 160, $"a solid box should hide the blue behind it: {solid}" );

			Assert.IsTrue( blended.b > solid.b + 40,
				$"half of the background should come through: solid {solid}, blended {blended}" );

			//And it is a mix rather than the background alone - the box is still there.
			Assert.IsTrue( blended.r > 20, $"the box should still be drawn: {blended}" );

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

	//The middle pixel of the view, drawn twice.
	private static (byte r, byte g, byte b) Middle( CoreSceneDrawer drawer, Scene scene,
		CameraComponent camera, ulong view )
	{
		var pixels = new byte[Size * Size * 4];

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
