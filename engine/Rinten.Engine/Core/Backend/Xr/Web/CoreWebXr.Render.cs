namespace Rinten;

partial class CoreWebXr
{
	//An eye's own view and drawer, the same pair OpenXR draws through - see CoreEyes, which is all of what happens after the poses are known.
	private static readonly CoreEyes.Eye[] drawn = [new(), new()];

	//The view both eyes are drawn into at once, where that is possible.
	private static readonly CoreEyes.Eye both = new();

	//Draws each eye into an engine view, then blits it into the browser framebuffer's eye viewport
	private static void DrawEyes( Scene scene )
	{
		if ( !scene.IsValid() ) return;

		var camera = scene.Camera;
		if ( !camera.IsValid() ) return;

		if ( !CoreSceneDrawer.EnsureResources() ) return;

		var anchor = Rinten.VR.VRSystem.AnchorFor( camera );
		var scale = 1.0f / MathF.Max( Rinten.VR.VRSystem.WorldScale, 0.0001f );
		var (width, height) = EyeSize;

		//One pass for both, where the browser and the build allow it. A false answer is not a failure - a browser without the extension,
		//an export with no two-view build of a stage - and the eyes are drawn one at a time below instead, in the same frame.
		if ( CoreRenderDevice.Multiview && DrawBoth( scene, camera, anchor, scale, width, height ) ) return;

		for ( var i = 0; i < 2; i++ )
		{
			var target = XrBind.EyeTarget( i );
			if ( target == 0 ) return;

			var eye = drawn[i];

			if ( !CoreEyes.EnsureView( eye, width, height ) ) return;

			eye.Drawer.Eye = CoreEyes.Field( eyes[i].Pose, anchor, scale,
				eyes[i].Left, eyes[i].Right, eyes[i].Up, eyes[i].Down );

			if ( !CoreEyes.Draw( eye, scene, camera, width, height, false, [target] ) ) return;
		}
	}

	//Both eyes into one two-layer view, each layer blitted to its eye's viewport; like CoreXr DrawBoth
	private static bool DrawBoth( Scene scene, CameraComponent camera, in Transform anchor, float scale,
		int width, int height )
	{
		var first = XrBind.EyeTarget( 0 );
		var second = XrBind.EyeTarget( 1 );

		if ( first == 0 || second == 0 ) return false;

		//No two-layer view: warn once and fall back to drawing an eye at a time from now on
		if ( !CoreEyes.EnsureView( both, width, height, 2 ) )
		{
			if ( Warned( "no layered view" ) )
				Log.Warning( "both eyes in one pass was asked for and the view would not be made - drawing an eye at a time" );

			return false;
		}

		both.Drawer.Eye = CoreEyes.Field( eyes[0].Pose, anchor, scale,
			eyes[0].Left, eyes[0].Right, eyes[0].Up, eyes[0].Down );

		both.Drawer.SecondEye = CoreEyes.Field( eyes[1].Pose, anchor, scale,
			eyes[1].Left, eyes[1].Right, eyes[1].Up, eyes[1].Down );

		return CoreEyes.Draw( both, scene, camera, width, height, false, [first, second] );
	}

	//Whether this is the first time something went wrong in this way.
	private static bool Warned( string what ) => warned.Add( what );

	private static readonly HashSet<string> warned = new();

	//The eyes' views and drawers let go, as a session ends.
	private static void Release()
	{
		foreach ( var eye in drawn )
			CoreEyes.Let( eye );

		CoreEyes.Let( both );
	}
}
