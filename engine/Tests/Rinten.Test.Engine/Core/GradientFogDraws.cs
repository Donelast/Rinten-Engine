using System;

namespace EngineTests.Core;

//Gradient fog actually reaches the rendered pixels, not just the component's properties
[TestClass]
[DoNotParallelize]
public class GradientFogDrawsTests
{
	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	private const int Size = 64;

	[TestMethod]
	public void FogColoursWhatIsFarAwayAndLeavesWhatIsNear()
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

			//An editor scene, because only that one ticks here - and the tick is what clears the world's fog so a component turned off stops colouring the picture.
			var scene = Scene.CreateEditorScene();
			using var scope = scene.Push();

			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.Shadows = false;
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1, 1, -1 ).Normal );

			var box = scene.CreateObject();
			box.WorldPosition = Vector3.Zero;
			box.Components.Create<ModelRenderer>().Model = model;

			var eye = scene.CreateObject();
			eye.WorldRotation = Rotation.LookAt( Vector3.Forward );

			var camera = eye.Components.Create<CameraComponent>();

			camera.BackgroundColor = new Color( 0.0f, 0.0f, 0.0f );
			camera.ZNear = 0.1f;
			camera.ZFar = 200.0f;
			camera.FieldOfView = 70.0f;

			//Red, and thick enough overhead that height never takes any of it away: what is being measured here is the distance falloff.
			var fog = scene.CreateObject();
			fog.WorldPosition = new Vector3( 0, 0, -50 );

			var gradient = fog.Components.Create<GradientFog>();
			gradient.Color = new Color( 1.0f, 0.0f, 0.0f, 1.0f );
			gradient.Height = 200.0f;
			gradient.VerticalFalloffExponent = 1.0f;
			gradient.StartDistance = 0.0f;
			gradient.EndDistance = 40.0f;
			gradient.FalloffExponent = 1.0f;

			if ( !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( $"the drawer has no pipelines here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( Size, Size );
			Assert.AreNotEqual( 0ul, view );

			var drawer = new CoreSceneDrawer { Watched = false };
			var errorsBefore = CoreErrors.Count;

			//The same box, twice: close enough that the fog has barely started, and far enough that it has very nearly finished.
			var near = Middle( drawer, scene, camera, eye, view, -3.0f );
			var far = Middle( drawer, scene, camera, eye, view, -35.0f );

			Console.WriteLine( $"near {near}, far {far}" );

			Assert.IsTrue( far.r > near.r + 20,
				$"the far box should have taken on the red of the fog: near {near}, far {far}" );

			//Green as well as red, because a picture that merely got brighter is not the same claim. Not blue: the fog is pure red
			//and the box is not, and what the tonemapper does to the third channel on the way out is a test about the tonemapper.
			Assert.IsTrue( far.g < near.g,
				$"and lost the rest of its colour to it: near {near}, far {far}" );

			//Turned off, the far box is its own colour again - which is what says the difference above is the fog and not the distance.
			gradient.Enabled = false;

			var off = Middle( drawer, scene, camera, eye, view, -35.0f );

			Console.WriteLine( $"off {off}" );

			Assert.IsTrue( off.r < far.r - 20,
				$"with the fog off the far box should not be red: off {off}, far {far}" );

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

	//The middle pixel, with the camera stood at the given distance.
	private static (byte r, byte g, byte b) Middle( CoreSceneDrawer drawer, Scene scene,
		CameraComponent camera, GameObject eye, ulong view, float x )
	{
		eye.WorldPosition = new Vector3( x, 0, 0 );

		var pixels = new byte[Size * Size * 4];

		//Twice: a drawer keeps what it worked out about the last frame, and the fog is written by the component's own pre-render.
		for ( var frame = 0; frame < 2; frame++ )
		{
			//The tick clears the world's fog and the pre-render is what puts it back - and the pre-render does not run in a headless
			//tier, which is what this one is. Called by hand, in that order, so the frame sees what the editor's frame would see.
			scene.EditorTick( frame * 0.016f, 0.016f );
			scene.PreRender();

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
