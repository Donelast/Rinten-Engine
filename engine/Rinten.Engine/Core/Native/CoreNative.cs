using System.IO;
using System.Runtime.InteropServices;

namespace Rinten;

//Checks the native dependency library loaded; lazy and survivable, linked into WASM in a page
internal static unsafe class CoreNative
{
	private static readonly Logger log = new( "Core" );

	//-- loading --------------------------------------------------------------

	//What the probe found. Immutable once made.
	private sealed record Probe( bool Available, string Description );

	//Probe runs once via Lazy, so a second thread waits instead of seeing "missing" mid-load
	private static readonly Lazy<Probe> probe = new( Load, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication );

	//Installs the search path for the core, once. A static constructor rather than the first probe: the runtime allows one resolver per assembly
	//and throws on a second, and two threads asking whether the core is available at the same moment is an ordinary thing for a test host to do.
	static CoreNative()
	{
		//The engine installs one resolver per assembly during startup, and the runtime throws on a second. A host that never starts the engine - a
		//test host - has none, so ask for them here: SetupResolvers remembers what it has already done, and startup asking again later is a no-op.
		DLLImportResolver.SetupResolvers();
	}

	/// <summary>Whether the native library is present; a plain field read after the first probe</summary>
	public static bool Available
	{
		[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining )]
		get => probed ? available : Settle();
	}

	private static volatile bool probed;
	private static bool available;

	private static bool Settle()
	{
		available = probe.Value.Available;
		probed = true;
		return available;
	}

	/// <summary>What loaded, or why nothing did.</summary>
	public static string BuildInfo => probe.Value.Description;

	//Loads the library by asking Box3D its version; a stale copy fails later as EntryPointNotFound
	private static Probe Load()
	{
		try
		{
			var box3d = Native.Box3D.Box3D.b3GetVersion();
			var description = $"box3d {box3d.major}.{box3d.minor}.{box3d.revision}";

			//Box2D came into the same file later, for scenes that simulate in the plane. A copy built before it still has Box3D, so it loads and 3D runs - and the
			//first 2D scene died on a missing entry point with nothing to say which file was stale. Asked here, it is said once, at startup, with the file's name.
			try
			{
				var box2d = Native.Box2D.Box2D.b2GetVersion();
				description += $", box2d {box2d.major}.{box2d.minor}.{box2d.revision}";
			}
			catch ( EntryPointNotFoundException )
			{
				var file = OperatingSystem.IsBrowser()
					? "the rintencore archive linked into this page"
					: Path.GetFullPath( Path.Combine( NetCore.NativeDllPath, DLLImportResolver.FileNameFor( "rintencore", NetCore.Current ) ) );

				log.Error( $"rintencore: {file} was built without Box2D (no b2GetVersion) - it is stale. 2D physics will not work until it is rebuilt with engine/Core/build-*.sh." );
				description += ", box2d MISSING (stale rintencore)";
			}

			return new Probe( true, description );
		}
		catch ( DllNotFoundException )
		{
			return new Probe( false, "rintencore not built for this platform" );
		}
		catch ( EntryPointNotFoundException e )
		{
			var missing = $"rintencore is missing {e.Message}";
			log.Warning( $"rintencore: {missing}" );
			return new Probe( false, missing );
		}
	}
}
