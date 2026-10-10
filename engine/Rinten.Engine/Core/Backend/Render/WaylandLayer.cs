using System.Runtime.InteropServices;

namespace Rinten;

//Wayland subsurface with its own swapchain and no input, so the world redraws without the window
internal sealed unsafe partial class WaylandLayer : IDisposable
{
	const string Lib = "libwayland-client.so.0";

	/// <summary>The surface, for a Vulkan surface to be made on.</summary>
	public nint Surface { get; private set; }

	/// <summary>The display it is on.</summary>
	public nint Display { get; }

	/// <summary>Whether the compositor will show the layer's picture at a size it was not drawn at - wp_viewporter - so a layer whose panel is being dragged to a new size can hold its own until the size settles, as a window's swapchain does.</summary>
	public bool Stretches => viewport != 0;

	readonly nint queue;
	nint subsurface;
	nint viewport;

	//Where it was last put, in the parent's surface units, and at what size it was last shown.
	(int X, int Y) position = (int.MinValue, int.MinValue);
	(int Width, int Height) shown = (-1, -1);

	bool synchronised;

	WaylandLayer( nint display, nint queue, nint surface, nint subsurface, nint viewport )
	{
		Display = display;
		this.queue = queue;
		Surface = surface;
		this.subsurface = subsurface;
		this.viewport = viewport;
	}

	/// <summary>A layer over this surface of this display, or null - no subcompositor, no libwayland, or not Wayland at all - in which case the caller draws the way it did.</summary>
	public static WaylandLayer Create( nint display, nint parent )
	{
		if ( display == 0 || parent == 0 || !OperatingSystem.IsLinux() ) return null;

		try
		{
			if ( !Globals.Bind( display, out var globals ) ) return null;

			var surface = Request( globals.Compositor, CompositorCreateSurface, Interfaces.Surface, [default] );
			if ( surface == 0 ) return null;

			var subsurface = Request( globals.Subcompositor, SubcompositorGetSubsurface, Interfaces.Subsurface,
				[default, Object( surface ), Object( parent )] );

			if ( subsurface == 0 )
			{
				Destroy( surface, SurfaceDestroy );
				return null;
			}

			//An empty input region: the layer is looked through by the pointer, which lands on the window's own surface under it and is read there as ever.
			var region = Request( globals.Compositor, CompositorCreateRegion, Interfaces.Region, [default] );

			if ( region != 0 )
			{
				Request( surface, SurfaceSetInputRegion, 0, [Object( region )] );
				Destroy( region, RegionDestroy );
			}

			var viewport = globals.Viewporter != 0
				? Request( globals.Viewporter, ViewporterGetViewport, Viewporter.Viewport, [default, Object( surface )] )
				: 0;

			//Its own pace, not the window's: the world presents every frame and the window only when the interface changes.
			Request( subsurface, SubsurfaceSetDesync, 0, [] );

			wl_display_flush( display );

			return new WaylandLayer( display, globals.Queue, surface, subsurface, viewport );
		}
		catch ( Exception e ) when ( e is DllNotFoundException or EntryPointNotFoundException )
		{
			log.Info( $"no layers: {e.Message}" );
			return null;
		}
	}

	/// <summary>Puts the layer at this place on its parent. Takes effect with the parent's next frame, which is why a move is always followed by one.</summary>
	public void Place( int x, int y )
	{
		if ( subsurface == 0 || position == (x, y) ) return;

		position = (x, y);

		Request( subsurface, SubsurfaceSetPosition, 0, [Int( x ), Int( y )] );
	}

	/// <summary>Shows the layer's picture at this size whatever size it was drawn at. True if that is a change, which like a move has to reach the screen with the parent's frame. Nothing, and false, where the compositor cannot - see <see cref="Stretches"/>.</summary>
	public bool Show( int width, int height )
	{
		if ( viewport == 0 || shown == (width, height) ) return false;

		shown = (width, height);

		Request( viewport, ViewportSetDestination, 0, [Int( width ), Int( height )] );
		return true;
	}

	/// <summary>Whether the layer's frames wait for the parent's. Held while the two have to change together - a move or a new size - and let go of once they have, so the layer's picture and the place it fills never disagree on the screen.</summary>
	public void Synchronise( bool together )
	{
		if ( subsurface == 0 || synchronised == together ) return;

		synchronised = together;

		Request( subsurface, together ? SubsurfaceSetSync : SubsurfaceSetDesync, 0, [] );
	}

	/// <summary>Lets go of what the compositor said to the layer's objects that nobody here listens to - which output the surface is on, mostly. It would otherwise stand on the queue for as long as the layer lives.</summary>
	public void Drain() => wl_display_dispatch_queue_pending( Display, queue );

	/// <summary>Takes the layer off the screen. Whatever was made on <see cref="Surface"/> - the swapchain and its Vulkan surface - has to be gone first.</summary>
	public void Dispose()
	{
		if ( viewport != 0 ) Destroy( viewport, ViewportDestroy );
		if ( subsurface != 0 ) Destroy( subsurface, SubsurfaceDestroy );
		if ( Surface != 0 ) Destroy( Surface, SurfaceDestroy );

		viewport = 0;
		subsurface = 0;
		Surface = 0;

		wl_display_flush( Display );
	}

	//-- the protocol, as much of it as this needs ---------------------------

	const uint DisplayGetRegistry = 1;
	const uint RegistryBind = 0;
	const uint CompositorCreateSurface = 0;
	const uint CompositorCreateRegion = 1;
	const uint SubcompositorGetSubsurface = 1;
	const uint SubsurfaceDestroy = 0;
	const uint SubsurfaceSetPosition = 1;
	const uint SubsurfaceSetSync = 4;
	const uint SubsurfaceSetDesync = 5;
	const uint SurfaceDestroy = 0;
	const uint SurfaceSetInputRegion = 5;
	const uint RegionDestroy = 0;
	const uint ViewporterGetViewport = 1;
	const uint ViewportDestroy = 0;
	const uint ViewportSetDestination = 2;

	const uint MarshalDestroy = 1;

	//One argument of a request, as libwayland takes them: eight bytes, whatever it is.
	[StructLayout( LayoutKind.Explicit, Size = 8 )]
	struct Argument
	{
		[FieldOffset( 0 )] public int I;
		[FieldOffset( 0 )] public uint U;
		[FieldOffset( 0 )] public nint O;
	}

	static Argument Int( int value ) => new() { I = value };
	static Argument Uint( uint value ) => new() { U = value };
	static Argument Object( nint value ) => new() { O = value };

	//Sends a request on a proxy. One with a new object in it hands that object back; its slot in the arguments is filled in by libwayland.
	static nint Request( nint proxy, uint opcode, nint made, ReadOnlySpan<Argument> arguments, uint flags = 0, uint version = 0 )
	{
		if ( version == 0 ) version = wl_proxy_get_version( proxy );

		var copy = stackalloc Argument[Math.Max( 1, arguments.Length )];
		arguments.CopyTo( new Span<Argument>( copy, arguments.Length ) );

		return wl_proxy_marshal_array_flags( proxy, opcode, made, version, flags, copy );
	}

	static void Destroy( nint proxy, uint opcode ) => Request( proxy, opcode, 0, [], MarshalDestroy );

	//libwayland interface descriptions, loaded lazily so missing libwayland doesn't crash startup
	static class Interfaces
	{
		static readonly nint library = NativeLibrary.TryLoad( Lib, out var found ) ? found : 0;

		static nint Export( string name )
			=> library != 0 && NativeLibrary.TryGetExport( library, name, out var at ) ? at : 0;

		public static readonly nint Registry = Export( "wl_registry_interface" );
		public static readonly nint Compositor = Export( "wl_compositor_interface" );
		public static readonly nint Subcompositor = Export( "wl_subcompositor_interface" );
		public static readonly nint Surface = Export( "wl_surface_interface" );
		public static readonly nint Subsurface = Export( "wl_subsurface_interface" );
		public static readonly nint Region = Export( "wl_region_interface" );

		/// <summary>The name an interface description carries first, which binding says out loud.</summary>
		public static nint NameOf( nint description ) => *(nint*)description;
	}

	//wp_viewporter and wp_viewport, which are not libwayland's to export - they are a protocol of their own, and the descriptions
	//its scanner would generate are written out here instead: a name, a version, and the requests with their signatures.
	static class Viewporter
	{
		[StructLayout( LayoutKind.Sequential )]
		struct Message
		{
			public nint Name, Signature, Types;
		}

		[StructLayout( LayoutKind.Sequential )]
		struct Interface
		{
			public nint Name;
			public int Version, MethodCount;
			public nint Methods;
			public int EventCount;
			public nint Events;
		}

		public static readonly nint Manager;
		public static readonly nint Viewport;

		static Viewporter()
		{
			var manager = (Interface*)NativeMemory.AllocZeroed( (nuint)sizeof( Interface ) );
			var viewport = (Interface*)NativeMemory.AllocZeroed( (nuint)sizeof( Interface ) );

			//Nothing here passes an object but get_viewport, so one table of nothings does for the rest.
			var none = (nint*)NativeMemory.AllocZeroed( (nuint)( 4 * sizeof( nint ) ) );

			var made = (nint*)NativeMemory.Alloc( (nuint)( 2 * sizeof( nint ) ) );
			made[0] = (nint)viewport;
			made[1] = Interfaces.Surface;

			var managerRequests = (Message*)NativeMemory.Alloc( (nuint)( 2 * sizeof( Message ) ) );
			managerRequests[0] = new Message { Name = Utf8( "destroy" ), Signature = Utf8( "" ), Types = (nint)none };
			managerRequests[1] = new Message { Name = Utf8( "get_viewport" ), Signature = Utf8( "no" ), Types = (nint)made };

			var viewportRequests = (Message*)NativeMemory.Alloc( (nuint)( 3 * sizeof( Message ) ) );
			viewportRequests[0] = new Message { Name = Utf8( "destroy" ), Signature = Utf8( "" ), Types = (nint)none };
			viewportRequests[1] = new Message { Name = Utf8( "set_source" ), Signature = Utf8( "ffff" ), Types = (nint)none };
			viewportRequests[2] = new Message { Name = Utf8( "set_destination" ), Signature = Utf8( "ii" ), Types = (nint)none };

			*manager = new Interface { Name = Utf8( "wp_viewporter" ), Version = 1, MethodCount = 2, Methods = (nint)managerRequests };
			*viewport = new Interface { Name = Utf8( "wp_viewport" ), Version = 1, MethodCount = 3, Methods = (nint)viewportRequests };

			Manager = (nint)manager;
			Viewport = (nint)viewport;
		}

		//A string for the life of the process, which is how long the descriptions are wanted.
		static nint Utf8( string text ) => Marshal.StringToCoTaskMemUTF8( text );
	}

	//The globals of a display the layers are made from, bound once, on a queue of our own. The editor has one display; another one binds again.
	sealed class Globals
	{
		public nint Display;
		public nint Queue;
		public nint Compositor;
		public nint Subcompositor;
		public nint Viewporter;

		uint compositorName, compositorVersion, subcompositorName, viewporterName;

		static Globals bound;

		public static bool Bind( nint display, out Globals globals )
		{
			globals = bound;
			if ( globals is not null && globals.Display == display ) return globals.Compositor != 0 && globals.Subcompositor != 0;

			globals = bound = new Globals { Display = display };

			globals.Queue = wl_display_create_queue( display );
			if ( globals.Queue == 0 ) return false;

			//The display's registry, asked for through a wrapper that is on our queue, so what it says is dispatched by our roundtrip and never by SDL's.
			var wrapper = wl_proxy_create_wrapper( display );
			if ( wrapper == 0 ) return false;

			wl_proxy_set_queue( wrapper, globals.Queue );

			var registry = Request( wrapper, DisplayGetRegistry, Interfaces.Registry, [default], 0, 1 );
			wl_proxy_wrapper_destroy( wrapper );

			if ( registry == 0 ) return false;

			var handle = GCHandle.Alloc( globals );

			try
			{
				wl_proxy_add_listener( registry, (nint)RegistryListener, GCHandle.ToIntPtr( handle ) );

				if ( wl_display_roundtrip_queue( display, globals.Queue ) < 0 ) return false;

				if ( globals.compositorName != 0 )
					globals.Compositor = BindGlobal( registry, globals.compositorName, Interfaces.Compositor, Math.Min( globals.compositorVersion, 4u ) );

				if ( globals.subcompositorName != 0 )
					globals.Subcompositor = BindGlobal( registry, globals.subcompositorName, Interfaces.Subcompositor, 1 );

				if ( globals.viewporterName != 0 )
					globals.Viewporter = BindGlobal( registry, globals.viewporterName, WaylandLayer.Viewporter.Manager, 1 );
			}
			finally
			{
				//Nothing more is wanted of the registry: what was bound outlives it, and a registry nobody dispatches would keep the
				//compositor's later news on our queue for ever. What it says after this goes to a proxy that is gone, which libwayland drops.
				wl_proxy_destroy( registry );
				handle.Free();
			}

			wl_display_flush( display );

			if ( globals.Subcompositor == 0 )
				log.Info( "no layers: the compositor has no subcompositor" );
			else if ( globals.Viewporter == 0 )
				log.Info( "layers are made a new size with their panel: the compositor has no viewporter to stretch one meanwhile" );

			return globals.Compositor != 0 && globals.Subcompositor != 0;
		}

		static nint BindGlobal( nint registry, uint name, nint description, uint version )
			=> Request( registry, RegistryBind, description,
				[Uint( name ), Object( Interfaces.NameOf( description ) ), Uint( version ), default], 0, version );

		void Seen( string name, uint id, uint version )
		{
			switch ( name )
			{
				case "wl_compositor": (compositorName, compositorVersion) = (id, version); break;
				case "wl_subcompositor": subcompositorName = id; break;
				case "wp_viewporter": viewporterName = id; break;
			}
		}

		[UnmanagedCallersOnly]
		static void Global( nint data, nint registry, uint name, byte* named, uint version )
		{
			if ( GCHandle.FromIntPtr( data ).Target is Globals globals )
				globals.Seen( Marshal.PtrToStringUTF8( (nint)named ), name, version );
		}

		[UnmanagedCallersOnly]
		static void GlobalRemove( nint data, nint registry, uint name ) { }

		//The registry's two events, in their order - for the life of the process, which is how long a listener has to outlive what it listens to.
		static readonly nint* RegistryListener = MakeListener();

		static nint* MakeListener()
		{
			var table = (nint*)NativeMemory.Alloc( (nuint)( 2 * sizeof( nint ) ) );
			table[0] = (nint)(delegate* unmanaged<nint, nint, uint, byte*, uint, void>)&Global;
			table[1] = (nint)(delegate* unmanaged<nint, nint, uint, void>)&GlobalRemove;
			return table;
		}
	}

	[LibraryImport( Lib )] private static partial nint wl_display_create_queue( nint display );
	[LibraryImport( Lib )] private static partial int wl_display_roundtrip_queue( nint display, nint queue );
	[LibraryImport( Lib )] private static partial int wl_display_dispatch_queue_pending( nint display, nint queue );
	[LibraryImport( Lib )] private static partial int wl_display_flush( nint display );
	[LibraryImport( Lib )] private static partial nint wl_proxy_create_wrapper( nint proxy );
	[LibraryImport( Lib )] private static partial void wl_proxy_wrapper_destroy( nint wrapper );
	[LibraryImport( Lib )] private static partial void wl_proxy_set_queue( nint proxy, nint queue );
	[LibraryImport( Lib )] private static partial int wl_proxy_add_listener( nint proxy, nint implementation, nint data );
	[LibraryImport( Lib )] private static partial uint wl_proxy_get_version( nint proxy );
	[LibraryImport( Lib )] private static partial void wl_proxy_destroy( nint proxy );
	[LibraryImport( Lib )] private static partial nint wl_proxy_marshal_array_flags( nint proxy, uint opcode, nint made, uint version, uint flags, Argument* arguments );

	private static readonly Rinten.Diagnostics.Logger log = new( "Ui" );
}
