namespace Rinten;

//Portable-frame sun shadow: one depth map down the light, laid ahead of the camera, texel-snapped
internal sealed class CorePortableSun
{
	//A number for what casts into a view, or live where nothing can vouch for it - see CoreSceneDrawer.LooseViewSignature.
	internal delegate ulong CasterSignature( in CoreMeshRender.ViewSetup view, out bool live );

	//How far along the light, towards the sun, a caster may stand and still be drawn.
	private const float TowardsSun = 400.0f;

	private bool casts;
	private CoreMeshRender.ViewSetup view;

	//Whether the device's map has been written once - until it has it holds nothing a pixel may read.
	private static ulong clearedMap;

	//The square both headset eyes share: the first eye plans and draws the map, the second reuses it
	private static long sharedFrame = -1;

	private static ulong sharedMap;
	private static bool sharedCasts;
	private static System.Numerics.Matrix4x4 sharedClip;
	private static float sharedSide, sharedDepth;
	private static int sharedTexels;
	private static CoreMeshRender.ViewSetup sharedView;

	//Which frame the map was last drawn in, so the eyes after the first do not draw it again.
	private static long drawnFrame = -1;

	//Whether the eyes of one headset frame share a square - see the remarks above.
	private static bool Sharing => CoreRender.SharingViews && Rinten.VR.VRSystem.IsRendering;

	internal void Plan( float[] frame, bool sunCasts, Vector3 towardsSun, float density, Vector3 eye, Vector3 forward, float far )
	{
		var theMap = CoreRender.PortableSunMap();

		//The square this frame's first eye laid, where there was one.
		if ( Sharing && sharedFrame == EngineLoop.LoopFrames && sharedMap == theMap )
		{
			casts = sharedCasts;
			view = sharedView;

			if ( casts ) CoreSceneRenderer.SetPortableSun( frame, sharedClip, sharedSide, sharedTexels, sharedDepth );
			else CoreSceneRenderer.SetPortableSun( frame, default, 0.0f, 0, 0.0f );

			return;
		}

		casts = sunCasts && theMap != 0;

		if ( !casts )
		{
			CoreSceneRenderer.SetPortableSun( frame, default, 0.0f, 0, 0.0f );
			Share( theMap );
			return;
		}

		var texels = CoreRender.PortableSunSide;

		//Twice what the light asks for at the eye, since one square is all of it - and never finer than sixty-four a metre:
		//sixty-four metres, three centimetres a texel. The pages' detail asked of one square was a square sixteen metres across.
		var side = System.Math.Clamp( texels / System.Math.Clamp( density, 4.0f, 64.0f ) * 2.0f, 8.0f, System.MathF.Max( far, 8.0f ) * 2.0f );
		var texel = side / texels;

		var basis = CoreSunPages.Basis( towardsSun );
		var centre = eye + ( forward * ( side * 0.25f ) );
		var at = System.Numerics.Vector3.Transform( new System.Numerics.Vector3( centre.x, centre.y, centre.z ), basis );

		var x0 = ( System.MathF.Floor( at.X / texel ) * texel ) - ( side * 0.5f );
		var y0 = ( System.MathF.Floor( at.Y / texel ) * texel ) - ( side * 0.5f );

		//Along the light: from far enough towards the sun that a tower behind the square still casts into it, to the far side of the square.
		var zLow = at.Z - TowardsSun;
		var zHigh = at.Z + side;

		var sx = 2.0f / side;
		var sy = 2.0f / side;
		var sz = 1.0f / ( zHigh - zLow );

		var square = new System.Numerics.Matrix4x4(
			sx, 0.0f, 0.0f, 0.0f,
			0.0f, sy, 0.0f, 0.0f,
			0.0f, 0.0f, sz, 0.0f,
			-( x0 * sx ) - 1.0f, -( y0 * sy ) - 1.0f, -zLow * sz, 1.0f );

		var worldToClip = basis * square;

		System.Span<System.Numerics.Vector4> planes = stackalloc System.Numerics.Vector4[6];
		CoreSceneRenderer.FrustumPlanes( worldToClip, planes );

		view = new CoreMeshRender.ViewSetup
		{
			WorldToClip = worldToClip,
			Camera = false,
			Casters = 0,
			ErrorScale = texels / side,
		};

		view.SetPlanes( planes );

		CoreSceneRenderer.SetPortableSun( frame, worldToClip, side, texels, sz );

		sharedClip = worldToClip;
		sharedSide = side;
		sharedTexels = texels;
		sharedDepth = sz;

		Share( theMap );
	}

	//What was just planned, kept for the other eye of this frame.
	private void Share( ulong map )
	{
		if ( !Sharing ) return;

		sharedFrame = EngineLoop.LoopFrames;
		sharedMap = map;
		sharedCasts = casts;
		sharedView = view;
	}

	//The square and the casters the map holds now, while it holds them - see Draw.
	private static bool kept;
	private static ulong keptMap;
	private static ulong keptSignature;
	private static System.Numerics.Matrix4x4 keptClip;

	//Draws casters into the map before the world; skipped if it already holds this square and casters
	internal void Draw( CoreMeshRender.LooseView loose, CasterSignature signature = null )
	{
		var map = CoreRender.PortableSunMap();
		if ( map == 0 || ( !casts && clearedMap == map ) ) return;

		//Drawn once a headset frame: the eye after the first samples what the first
		//one left, which is the square it was handed - see the remarks on sharedFrame.
		if ( Sharing && drawnFrame == EngineLoop.LoopFrames && clearedMap == map ) return;

		var live = true;
		var said = 0ul;

		if ( casts && signature is not null )
		{
			said = signature( in view, out live );

			if ( !live && kept && keptMap == map && clearedMap == map
				&& said == keptSignature && keptClip == view.WorldToClip )
			{
				drawnFrame = EngineLoop.LoopFrames;
				return;
			}
		}

		drawnFrame = EngineLoop.LoopFrames;

		if ( !CoreRenderDevice.BeginShadow( map, 0 ) ) return;

		CoreRenderDevice.ZoneBegin( "sun map" );

		var texels = CoreRender.PortableSunSide;
		CoreRenderDevice.ShadowArea( 0, 0, texels, texels, clear: true );

		var waitingBefore = CoreRender.DrawsWaiting;

		if ( casts ) loose?.Invoke( in view );

		//A caster whose pipeline was still compiling drew nothing, so redraw next frame until all drew
		var complete = CoreRender.DrawsWaiting == waitingBefore;

		CoreRenderDevice.EndTarget();
		CoreRenderDevice.ZoneEnd();

		clearedMap = map;

		kept = casts && !live && complete;
		keptMap = map;
		keptSignature = said;
		keptClip = view.WorldToClip;
	}
}
