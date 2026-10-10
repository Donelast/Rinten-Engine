using NativeEngine;
using System.Runtime.CompilerServices;

namespace Rinten;

/// <summary>Used to render to the screen using your Graphics Card, or whatever you kids are using in your crazy future computers. Whatever it is I'm sure it isn't fungible and everyone has free money and no-one has to ever work.</summary>
public static partial class Graphics
{
	public enum PrimitiveType
	{
		Points = NativeEngine.RenderPrimitiveType.RENDER_PRIM_POINTS,
		Lines = NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINES,
		LinesWithAdjacency = NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINES_WITH_ADJACENCY,
		LineStrip = NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINE_STRIP,
		LineStripWithAdjacency = NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINE_STRIP_WITH_ADJACENCY,
		Triangles = NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLES,
		TrianglesWithAdjacency = NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLES_WITH_ADJACENCY,
		TriangleStrip = NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLE_STRIP,
		TriangleStripWithAdjacency = NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLE_STRIP_WITH_ADJACENCY,
	}

	/// <summary>If true then we're currently rendering and you are safe to use the contents of this class</summary>
	public static bool IsActive => viewActive;

	/// <summary>Current layer; self-drawing objects run once per view, translucent if flagged, else opaque</summary>
	public static SceneLayerType LayerType => CurrentLayer;

	//Which layer the object drawing itself is in - set by the drawer around it.
	internal static SceneLayerType CurrentLayer { get; set; }

	struct RenderState
	{
		internal Rect viewport;
		internal RenderTarget renderTarget;
	}

	[ThreadStatic]
	private static RenderState _state;

	internal static ImageFormat IdealColorFormat => default;
	internal static MultisampleAmount IdealMsaaLevel => default;

	/// <summary>When Frame grabbing, we store the result in this pooled render target. It stays checked out until the end of this render scope. In pixel size, where are we rendering to?</summary>
	public static Rect Viewport
	{
		get => _state.viewport;
		set => _state.viewport = value;
	}

	/// <summary>Access to the current render context's attributes. These will be used to set attributes in materials/shaders. This is cleared at the end of the render block.</summary>
	public static RenderAttributes Attributes => viewAttributes ??= new RenderAttributes();


	//Access to the current frame's attributes.
	[Obsolete( "Frame attributes are deprecated, try to use Pipeline Texture Sets." )]
	internal static RenderAttributes FrameAttributes => Attributes;

	/// <summary>The camera transform of the currently rendering view</summary>
	public static Transform CameraTransform => viewCamera;

	/// <summary>The camera position of the currently rendering view</summary>
	public static Vector3 CameraPosition => CameraTransform.Position;

	/// <summary>The camera rotation of the currently rendering view</summary>
	public static Rotation CameraRotation => CameraTransform.Rotation;

	//What the driver says this process may have, where it says - the memory budget extension - and the size of the device's own heaps where it does not. Asked fresh every time; see CoreRenderDevice.ReadMemory.
	/// <summary>GPU video memory budget in bytes, as reported by the OS (WDDM).</summary>
	public static ulong VideoMemoryBudget
	{
		get
		{
			var memory = CoreRenderDevice.ReadMemory();

			return (ulong)Math.Max( 0L, memory.DriverAnswered ? memory.DriverBudget : memory.HeapSize );
		}
	}

	/// <summary>GPU memory used in bytes: the driver's count where available, else the engine's own allocations</summary>
	public static ulong VideoMemoryUsed
	{
		get
		{
			var memory = CoreRenderDevice.ReadMemory();

			return (ulong)Math.Max( 0L, memory.DriverAnswered ? memory.DriverUsed : memory.Ours );
		}
	}


	/// <summary>The field of view of the currently rendering camera view, in degrees.</summary>
	public static float FieldOfView => viewFieldOfView;

	/// <summary>The frustum of the currently rendering camera view.</summary>
	public static Frustum Frustum => viewFrustum;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static void AssertRenderBlock()
	{
		if ( !IsActive )
			throw new System.Exception( "Tried to render outside of rendering block" );
	}

	/// <summary>No-op: every draw is already bound with the frame's lights, sun pages and probes</summary>
	public static void SetupLighting( SceneObject obj, RenderAttributes targetAttributes = null )
	{
	}

	/// <summary>Grabs the current viewport's color texture and stores it in targetName on renderAttributes.</summary>
	public static RenderTarget GrabFrameTexture( string targetName = "FrameTexture", RenderAttributes renderAttributes = null, DownsampleMethod downsampleMethod = DownsampleMethod.None, int maxMips = 0 )
	{
		AssertRenderBlock();

		targetName ??= "FrameTexture";

		if ( !grabbed.TryGetValue( targetName, out var target ) )
		{
			target = RenderTarget.Borrowing();
			grabbed[targetName] = target;
		}

		( renderAttributes ?? Attributes ).Set( targetName, target.ColorTarget );

		CoreImmediate.Add( new CoreImmediate.Grab( target ) );

		return target;
	}

	//The targets frame copies have been handed out in, by the name they were asked for under.
	private static readonly Dictionary<string, RenderTarget> grabbed = new();

	[Obsolete( "Use GrabFrameTexture with DownsampleMethod instead" )]
	public static void GrabFrameTexture( string targetName, RenderAttributes renderAttributes, bool withMips )
	{
		GrabFrameTexture( targetName, renderAttributes, withMips ? DownsampleMethod.GaussianBlur : DownsampleMethod.None );
	}

	/// <summary>Grabs the current depth texture and stores it in targetName on renderAttributes.</summary>
	public static RenderTarget GrabDepthTexture( string targetName = "DepthTexture", RenderAttributes renderAttributes = null )
	{
		AssertRenderBlock();

		Warn( "Graphics.GrabDepthTexture has no depth to hand a scene object drawing itself - grab it from a CommandList at one of the camera's stages" );

		return null;
	}





	/// <summary>Get or set the current render target. Setting this will bind the render target and change the viewport to match it.</summary>
	public static RenderTarget RenderTarget
	{
		get => _state.renderTarget;
		set => _state.renderTarget = value;
	}

	/// <summary>Clear the current drawing context to given color.</summary>
	public static void Clear( Color color, bool clearColor = true, bool clearDepth = true, bool clearStencil = true )
	{
		if ( !clearColor ) return;

		if ( _state.renderTarget?.ColorTarget?.core is { } core )
		{
			var device = core.Device();

			if ( device != 0 ) CoreRenderDevice.ClearTexture( device, color );

			return;
		}

		Warn( "Graphics.Clear with no RenderTarget set would clear the world a scene object draws over - it is skipped" );
	}

	/// <summary>Clear the current drawing context to given color.</summary>
	public static void Clear( bool clearColor = true, bool clearDepth = true )
	{
		Clear( Color.Transparent, clearColor, clearDepth, false );
	}

	/// <summary>Render this camera to the specified texture target</summary>
	[Obsolete( "Use CameraComponent.RenderToTexture" )]
	public static bool RenderToTexture( SceneCamera camera, Texture target )
	{
		return camera is not null && camera.RenderToTexture( target, null, default );
	}

	/// <summary>Copies pixel data from one texture to another on the GPU. This does not automatically resize or scale the texture, format and size should be equal.</summary>
	public static void CopyTexture( Texture srcTexture, Texture dstTexture )
	{
		if ( srcTexture == null ) throw new ArgumentNullException( nameof( srcTexture ) );
		if ( dstTexture == null ) throw new ArgumentNullException( nameof( dstTexture ) );
		if ( srcTexture.ImageFormat != dstTexture.ImageFormat ) throw new ArgumentException( "Source and destination texture format must match!" );
		if ( srcTexture.Size != dstTexture.Size ) throw new ArgumentException( "Source and destination texture size must match!" );

		CopyLevel( srcTexture, 0, dstTexture, 0 );
	}

	//Copies one texture level on the device and in CPU-side pixels, so readback matches
	private static void CopyLevel( Texture source, int sourceLevel, Texture destination, int destinationLevel )
	{
		var from = source.core;
		var into = destination.core;

		if ( from is null || into is null ) return;

		var fromDevice = from.Device();
		var intoDevice = into.Device();

		var copied = fromDevice != 0 && intoDevice != 0 &&
			CoreRenderDevice.CopyTexture( fromDevice, sourceLevel, intoDevice, destinationLevel );

		if ( from.Data is null || into.Data is null ) return;

		var bytes = from.MipData( sourceLevel );
		var room = into.MipData( destinationLevel );

		if ( bytes.IsEmpty || bytes.Length != room.Length ) return;

		//Written as a change - and so sent to the device - only where the device did not take the copy itself; otherwise the two already agree.
		if ( copied ) bytes.CopyTo( room );
		else into.Write( bytes, destinationLevel );
	}

	[Obsolete( "Use the CopyTexture overload without 'srcMipLevels' and 'dstMipLevels' parameters instead." )]
	public static void CopyTexture( Texture srcTexture, Texture dstTexture, int srcMipSlice = 0, int srcArraySlice = 0, int srcMipLevels = 1, int dstMipSlice = 0, int dstArraySlice = 0, int dstMipLevels = 1 )
	{
		CopyTexture( srcTexture, dstTexture, srcMipSlice, srcArraySlice, dstMipSlice, dstArraySlice );
	}

	/// <summary>Copies pixel data from one texture to another on the GPU. This does not automatically resize or scale the texture, format and size should be equal. This one lets you copy to/from arrays / specific mips.</summary>
	public static void CopyTexture( Texture srcTexture, Texture dstTexture, int srcMipSlice = 0, int srcArraySlice = 0, int dstMipSlice = 0, int dstArraySlice = 0 )
	{
		if ( srcTexture == null ) throw new ArgumentNullException( nameof( srcTexture ) );
		if ( dstTexture == null ) throw new ArgumentNullException( nameof( dstTexture ) );
		if ( srcTexture.ImageFormat != dstTexture.ImageFormat ) throw new ArgumentException( "Source and destination texture format must match!" );
		if ( srcMipSlice < 0 || srcMipSlice >= srcTexture.Mips ) throw new ArgumentException( $"{nameof( srcMipSlice )} out of bounds" );
		if ( dstMipSlice < 0 || dstMipSlice >= dstTexture.Mips ) throw new ArgumentException( $"{nameof( dstMipSlice )} out of bounds" );

		//A texture of the game's is one picture on the device, however many slices it keeps on this side - see CoreTexture.Device.
		if ( srcArraySlice != 0 || dstArraySlice != 0 )
			throw new ArgumentException( "a texture has one slice on the device here - copy the first, or copy the slices' pixels with GetPixels and Update" );

		CopyLevel( srcTexture, srcMipSlice, dstTexture, dstMipSlice );
	}

	/// <summary>Waits for all submitted GPU work, uploads included; usable outside a render block</summary>
	public static void FlushGPU()
	{
		CoreRenderDevice.WaitIdle();
	}
}
