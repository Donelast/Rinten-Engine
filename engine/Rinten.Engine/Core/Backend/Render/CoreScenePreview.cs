using System;

namespace Rinten;

//Draws a scene offscreen with the scene view's drawer, for thumbnails and widget previews
//One instance is kept and resized rather than made per picture, since each view owns its images
internal sealed class CoreScenePreview : IDisposable
{
	private ulong view;

	//The render scale this viewport's view was last given - a new view starts at one.
	private float viewScale = 1.0f;
	private bool saidScale;
	private (int Width, int Height) size;
	private byte[] pixels = Array.Empty<byte>();

	private readonly CoreSceneDrawer drawer = new();

	/// <summary>Game view: draw the game's panels and hide editor grid, icons and selection outline</summary>
	public bool GameView { get; set; }

	/// <summary>Editor grid, icons, and selection. Off for thumbnails and asset previews.</summary>
	public bool EditorOverlays { get; set; }

	/// <summary>Whether the grid on the ground is drawn. Off for a 2D view, which draws its own.</summary>
	public bool GroundGrid { get; set; } = true;

	/// <summary>Whether the grid is cut into the faces of the editable meshes.</summary>
	public bool SurfaceGrid { get; set; } = true;

	/// <summary>Whose gizmos this viewport draws. Nothing draws none - which is what a thumbnail wants, and what keeps one viewport from taking the batches the next one was going to draw.</summary>
	public Func<Gizmo.Instance> GizmoSource
	{
		get => drawer.GizmoSource;
		set => drawer.GizmoSource = value;
	}

	public CoreScenePreview()
	{
		//Asked every frame rather than set once: the same viewport is the scene view before the game starts and the game view after it.
		drawer.GameViewSource = () => GameView;
		drawer.EditorOverlaysSource = () => EditorOverlays;
		drawer.GroundGridSource = () => GroundGrid;
		drawer.SurfaceGridSource = () => SurfaceGrid;
	}

	/// <summary>The one everything shares. Made on first use.</summary>
	public static CoreScenePreview Shared => shared ??= new CoreScenePreview();

	private static CoreScenePreview shared;

	/// <summary>Whether a scene can be drawn this way at all - which is to say, whether our renderer is the one drawing.</summary>
	public static bool Available => CoreNative.Available && CoreRenderDevice.Initialize();

	/// <summary>Draws the scene from a camera into a game texture (all mips), without waiting for the GPU</summary>
	public bool RenderInto( Scene scene, CameraComponent camera, Texture target )
	{
		if ( target?.core is not { } core ) return false;
		if ( !Available ) return false;

		//On the device before anything is copied into it: the copy writes an image the texture already has.
		var texture = core.Device();
		if ( texture == 0 ) return false;

		return RenderPicture( scene, camera, core.Width, core.Height, texture ) != 0;
	}

	/// <summary>Draws a camera into this preview's view now, never deferred; returns the picture or 0 if not drawn</summary>
	public ulong RenderPicture( Scene scene, CameraComponent camera, int width, int height, ulong copyInto = 0 )
	{
		if ( !scene.IsValid() || !camera.IsValid() ) return 0;
		if ( !Available || !CoreSceneDrawer.EnsureResources() ) return 0;

		width = Math.Min( width, Widest );
		height = Math.Min( height, Widest );

		if ( width < 1 || height < 1 || !Resize( width, height ) ) return 0;

		if ( !CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) ) return 0;

		//Drawn every frame and looked at.
		drawer.Watched = true;

		var ended = false;

		try
		{
			drawer.Draw( scene, camera, view, size.Width, size.Height );

			if ( copyInto != 0 ) CoreRenderDevice.CopyViewToTexture( view, copyInto );
		}
		finally
		{
			ended = CoreRenderDevice.EndViewFrameAsync( view );
		}

		return ended ? CoreRenderDevice.ViewTexture( view ) : 0;
	}

	public bool Render( Scene scene, CameraComponent camera, Bitmap target )
	{
		if ( !scene.IsValid() || !camera.IsValid() ) return false;
		if ( target is null || !target.IsValid ) return false;

		//Floating point bitmaps are for images that carry more than a screen can show.
		//A finished picture is eight bits a channel by the time anything looks at it.
		if ( target.IsFloatingPoint ) return false;

		if ( !Available ) return false;
		if ( !CoreSceneDrawer.EnsureResources() ) return false;

		var width = target.Width;
		var height = target.Height;

		if ( !Resize( width, height ) ) return false;

		if ( !CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) ) return false;

		//A picture on its way to a bitmap - a thumbnail, a screenshot - is not a viewport anybody is watching
		drawer.Watched = false;

		var ended = false;

		try
		{
			drawer.Draw( scene, camera, view, width, height );
		}
		finally
		{
			ended = CoreRenderDevice.EndViewFrame( view );
		}

		if ( !ended ) return false;

		//Allocated here, not in Resize: viewports resize every frame while dragged and never read it
		var wanted = width * height * 4;

		if ( pixels.Length != wanted ) pixels = new byte[wanted];

		//Finished by the time that returned, so this is a copy and not a wait.
		if ( !CoreRenderDevice.ReadView( view, pixels ) ) return false;

		target.SetPixelsRgba( pixels );
		return true;
	}

	/// <summary>Draws and returns the on-device texture with no readback; valid until resize or dispose, so re-ask</summary>
	public Texture RenderToTexture( Scene scene, CameraComponent camera, int width, int height )
	{
		if ( !scene.IsValid() || !camera.IsValid() ) return null;
		if ( width < 8 || height < 8 ) return null;

		//Cut down here rather than inside the resize, so that what is drawn and what the texture says it is are the same numbers.
		width = Math.Min( width, Widest );
		height = Math.Min( height, Widest );

		if ( !Available ) return null;
		if ( !CoreSceneDrawer.EnsureResources() ) return null;

		//One named block for the whole scene drawing, so the profiler shows what the viewport costs the frame in one line rather than as loose render zones
		using var scope = Diagnostics.Performance.Scope( "Scene View" );

		bool sized;

		//Named, because "Scene View" carried 74 ms of self time in a switch frame and every zone under it was
		//small - so the time was in here, in what makes and remakes a viewport's pictures, and nothing said so.
		using ( Diagnostics.Performance.Scope( "view resize" ) )
		{
			sized = Resize( width, height );
		}

		if ( !sized ) return null;

		//Draw at most a couple of views per frame; waiting views show last frame's picture until starved
		var frame = EngineLoop.LoopFrames;

		//A new view starts its clock now, or it would count as starving and bypass the budget
		if ( drewAt == long.MinValue ) drewAt = frame;

		var starving = frame - drewAt > StarveFrames;

		if ( !starving && !TakeDraw() )
		{
			skippedDraws++;

			return borrowed;
		}

		drewAt = frame;

		drawnViews++;

		//Whatever the viewport actually is, which is not what was asked for while a drag is being waited out - see Resize. Everything
		//below draws, borrows and reports the same numbers, and the panel stretches the picture over the widget until the size settles.
		(width, height) = size;

		//Cameras rendering into textures draw before the view that shows them
		CoreCameraTargets.Draw( scene );

		//Apply the render scale to this viewport's images; a scale change rebuilds them
		if ( view != 0 )
		{
			var scale = CoreUpscale.WindowScale;

			if ( Math.Abs( scale - viewScale ) > 0.001f )
			{
				if ( CoreRenderDevice.SetViewScale( view, scale ) )
				{
					viewScale = scale;
					drawer.Release();
					Log.Info( $"viewport: the world drawn at {scale:0.##} of its size" );
				}
				else if ( !saidScale )
				{
					saidScale = true;
					Log.Warning( $"viewport: render scale {scale:0.##} refused - {CoreRenderDevice.Description}" );
				}
			}
		}

		bool begun;

		using ( Diagnostics.Performance.Scope( "view begin" ) )
		{
			begun = CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor );
		}

		if ( !begun ) return null;

		//Straight onto the screen and drawn again next frame - this is the one the profiler should be reading
		drawer.Watched = true;

		var ended = false;

		try
		{
			using var drawing = Diagnostics.Performance.Scope( "view draw" );

			drawer.Draw( scene, camera, view, width, height );
		}
		finally
		{
			using var submitting = Diagnostics.Performance.Scope( "view submit" );

			ended = CoreRenderDevice.EndViewFrameAsync( view );
		}

		if ( !ended ) return null;

		ulong handle;

		using ( Diagnostics.Performance.Scope( "view texture" ) )
		{
			handle = CoreRenderDevice.ViewTexture( view );
		}

		if ( handle == 0 ) return null;

		//One texture object, pointed at whatever the viewport's picture is now: a resize remakes
		//the picture, and a new object every frame would be a new object every frame for no reason.
		if ( borrowed is null )
		{
			borrowed = new Texture( CoreTexture.Borrowing( handle, width, height ) );
		}
		else
		{
			borrowed.core.Borrow( handle, width, height );
		}

		//Whether a panel wearing this hides what is behind it. The view is cleared to the camera's own colour and every pass after that draws over it,
		//so an opaque clear is an opaque picture - and that lets the interface leave the ground under the viewport unpainted. See CoreTexture.Opaque.
		borrowed.core.Opaque = camera.BackgroundColor.a >= 1.0f;

		return borrowed;
	}

	private Texture borrowed;

	/// <summary>Draws the scene once into an offscreen view and returns RGBA pixels, as a window would show it</summary>
	public bool Shoot( Scene scene, CameraComponent camera, int width, int height, out byte[] rgba )
	{
		rgba = null;

		if ( !DrawOwn( scene, camera, width, height ) ) return false;

		(width, height) = size;

		var pixels = new byte[width * height * 4];

		if ( !CoreRenderDevice.ReadView( view, pixels ) ) return false;

		rgba = pixels;
		return true;
	}

	//Draws the scene once into this preview's own view, for what is in it to be read - see Shoot and TryReadPixel. A
	//viewport drawn straight into a window's frame, or onto a layer of its own, has no picture of its own otherwise.
	internal bool DrawOwn( Scene scene, CameraComponent camera, int width, int height )
	{
		if ( !scene.IsValid() || !camera.IsValid() || width < 8 || height < 8 ) return false;
		if ( !Available || !CoreSceneDrawer.EnsureResources() ) return false;
		if ( !Resize( width, height ) ) return false;
		if ( !CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) ) return false;

		drawer.Watched = false;

		var ended = false;

		try
		{
			drawer.Draw( scene, camera, view, width, height );
		}
		finally
		{
			ended = CoreRenderDevice.EndViewFrame( view );
		}

		return ended;
	}

	/// <summary>Draws the scene into a window's own frame, at the size the window said the world is - see CoreUiWindow.WorldPanel. No picture of this viewport's own is involved: the frame is the window's, and so is the picture that ends up on the screen.</summary>
	public bool DrawInto( Scene scene, CameraComponent camera, ulong swapchain, int width, int height )
	{
		if ( !scene.IsValid() || !camera.IsValid() ) return false;
		if ( swapchain == 0 || width < 1 || height < 1 ) return false;

		if ( !Available ) return false;
		if ( !CoreSceneDrawer.EnsureResources() ) return false;

		//The cameras drawing into textures or laid over the screen, before the world that shows them.
		CoreCameraTargets.Draw( scene );

		drawer.Watched = true;
		drawnViews++;

		using var drawing = Diagnostics.Performance.Scope( "view draw" );

		drawer.Draw( scene, camera, swapchain, width, height );
		return true;
	}

	//The most a viewport may be asked for. A widget whose layout went wrong asks for tens of thousands of
	//pixels, and no device will make a picture that big - so the ask is cut down here rather than failing there.
	private const int Widest = 16384;

	//Whether a viewport waits for a size to settle before it remakes its pictures at it. -noviewhold is the control half of measuring it.
	private static readonly bool Hold = !Rinten.Utility.CommandLine.HasSwitch( "-noviewhold" );

	//How long a size has to stand still before it is acted on. Kept short for the same reason as the
	//window's: this is how far behind the widget the picture in it is allowed to be while an edge is moving.
	private const float HoldSeconds = 0.05f;

	private (int Width, int Height) asked;
	private RealTimeSince sinceAsked;

	//Limits views rebuilding their images per frame, e.g. when a workspace switch resizes them all
	private static long budgetAt = -1;
	private static int budgetSpent;

	//How many views may make their pictures in one frame.
	private const int RebuildsPerFrame = 1;

	//The same again for drawing, with its own budget: making pictures and drawing
	//into them are different costs and a frame can afford different numbers of each.
	private static long drawBudgetAt = -1;
	private static int drawBudgetSpent;

	//How many scenes may be drawn in one frame.
	private const int DrawsPerFrame = 2;

	//How many frames a view may be held back before it draws regardless. Without this the same two views win
	//the budget every frame - the order they are ticked in does not change - and the rest never draw at all.
	private const int StarveFrames = 3;

	//Views wait for their in-flight frame instead of skipping a draw, which halved the view's frame rate

	//The frame this view last drew in, or long.MinValue for a new one.
	private long drewAt = long.MinValue;

	/// <summary>How many draws have been held back, for the trace line.</summary>
	public static long skippedDraws;

	/// <summary>How many draws were left for the next frame because the card had not finished the last one.</summary>
	public static long skippedBusy;

	/// <summary>And how many actually drew.</summary>
	public static long drawnViews;

	//Whether this view may draw this frame.
	private static bool TakeDraw()
	{
		if ( !Spread ) return true;

		var frame = EngineLoop.LoopFrames;

		if ( drawBudgetAt != frame )
		{
			drawBudgetAt = frame;
			drawBudgetSpent = 0;
		}

		if ( drawBudgetSpent >= DrawsPerFrame ) return false;

		drawBudgetSpent++;

		return true;
	}

	//Whether this view may be the one that does it this frame.
	private static bool TakeRebuild()
	{
		if ( !Spread ) return true;

		//The loop's own frame number, not a clock. RealTime.Now is live - it reads the timer every time it is asked - so using it to mean
		//"this frame" gave every viewport its own frame and its own budget, and all eight rebuilt in the one frame this exists to prevent.
		var frame = EngineLoop.LoopFrames;

		if ( budgetAt != frame )
		{
			budgetAt = frame;
			budgetSpent = 0;
		}

		if ( budgetSpent >= RebuildsPerFrame ) return false;

		budgetSpent++;

		return true;
	}

	//Whether making pictures is spread over frames at all. -noviewspread is the control half of measuring it.
	private static readonly bool Spread = !Rinten.Utility.CommandLine.HasSwitch( "-noviewspread" );

	private bool Resize( int width, int height )
	{
		width = Math.Min( width, Widest );
		height = Math.Min( height, Widest );

		if ( view != 0 && size == (width, height) ) return true;

		//Ignore sizes during a drag; stretch the old picture until the size holds for a tenth of a second
		if ( Hold && view != 0 && !Settled( width, height ) ) return true;

		//At most one resize per frame, but a view with no images is never made to wait
		if ( view != 0 && !TakeRebuild() ) return true;

		//Forgotten first: a resize that fails leaves the viewport with no pictures at all,
		//and remembering the size it used to be would have the next frame draw into them.
		size = default;

		if ( view == 0 )
		{
			view = CoreRenderDevice.CreateView( width, height );
			if ( view == 0 ) return false;
			viewScale = 1.0f;
		}
		else if ( !CoreRenderDevice.ResizeView( view, width, height ) )
		{
			return false;
		}

		size = (width, height);

		//The materials the passes read through name the pictures that were just remade, so they are stale whoever else is using this.
		drawer.Release();

		return true;
	}

	//Whether this size has been the one asked for long enough to be worth remaking the pictures at.
	private bool Settled( int width, int height )
	{
		if ( asked != (width, height) )
		{
			asked = (width, height);
			sinceAsked = 0;

			return false;
		}

		return sinceAsked > HoldSeconds;
	}

	public void Dispose()
	{
		drawer.Release();

		CoreRenderDevice.DestroyView( view );
		view = 0;

		if ( ReferenceEquals( shared, this ) ) shared = null;
	}

	//Picture size last drawn, which can lag the widget during a resize.
	internal (int Width, int Height) PictureSize => size;

	//Reads one pixel from the last frame this viewport drew.
	internal bool TryReadPixel( int x, int y, out Color color )
	{
		color = default;

		if ( view == 0 || size.Width <= 0 || size.Height <= 0 ) return false;
		if ( x < 0 || y < 0 || x >= size.Width || y >= size.Height ) return false;

		var wanted = size.Width * size.Height * 4;

		if ( pixels.Length != wanted ) pixels = new byte[wanted];

		if ( !CoreRenderDevice.ReadView( view, pixels ) ) return false;

		var i = (y * size.Width + x) * 4;

		color = new Color(
			pixels[i] / 255f,
			pixels[i + 1] / 255f,
			pixels[i + 2] / 255f,
			pixels[i + 3] / 255f );

		return color.a > 0.001f;
	}
}
