using System.IO;
using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan;

namespace Rinten;

//Loads the Vulkan loader (local first, then system) and fetches entry points like volk did
internal sealed unsafe class VulkanLoader : IDisposable
{
	//The loader, opened once and kept for the life of the process.
	private readonly nint library;

	//The two roots everything else is fetched through.
	private readonly delegate* unmanaged[Cdecl]<nint, byte*, nint> getInstanceProcAddr;
	private readonly delegate* unmanaged[Cdecl]<nint, byte*, nint> getDeviceProcAddr;

	/// <summary>The loader's own vkGetInstanceProcAddr, for a runtime that makes the instance and the device itself - see CoreXr.</summary>
	public nint GetInstanceProcAddrPointer => (nint)getInstanceProcAddr;

	/// <summary>What the next lookups resolve against. Instance-level names come through the instance once there is one; device-level names through the device.</summary>
	public nint Instance;
	public nint Device;

	private VulkanLoader( nint library )
	{
		this.library = library;

		getInstanceProcAddr = (delegate* unmanaged[Cdecl]<nint, byte*, nint>)NativeLibrary.GetExport( library, "vkGetInstanceProcAddr" );

		//Older loaders have no global vkGetDeviceProcAddr and hand it out through the instance instead; the field stays null until an instance says.
		NativeLibrary.TryGetExport( library, "vkGetDeviceProcAddr", out var deviceProc );
		getDeviceProcAddr = (delegate* unmanaged[Cdecl]<nint, byte*, nint>)deviceProc;
	}

	//What the loader is called on this platform, best guess first - the same list volk tries.
	private static IEnumerable<string> LibraryNames()
	{
		if ( OperatingSystem.IsWindows() )
		{
			yield return "vulkan-1.dll";
			yield break;
		}

		if ( OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() )
		{
			yield return "libvulkan.dylib";
			yield return "libvulkan.1.dylib";
			yield return "libMoltenVK.dylib";
			yield break;
		}

		yield return "libvulkan.so.1";
		yield return "libvulkan.so";
	}

	/// <summary>Opens the loader. Null with a reason where there is none - the driver is missing, or too old to export the one function everything starts from.</summary>
	public static VulkanLoader Open( out string failure )
	{
		failure = null;

		foreach ( var name in LibraryNames() )
		{
			//Beside the engine first, then wherever the platform keeps its own. The folder is the one every
			//other native lives in, and a driver put there is a driver somebody meant this build to use.
			var beside = Path.Combine( NetCore.NativeDllPath, name );

			if ( File.Exists( beside ) && NativeLibrary.TryLoad( beside, out var local ) && HasRoot( local ) )
				return new VulkanLoader( local );

			if ( NativeLibrary.TryLoad( name, out var system ) && HasRoot( system ) )
				return new VulkanLoader( system );
		}

		failure = "no Vulkan loader - the driver is missing or too old";
		return null;
	}

	private static bool HasRoot( nint library )
		=> NativeLibrary.TryGetExport( library, "vkGetInstanceProcAddr", out _ );

	/// <summary>An instance has been made: the device-level root can be fetched through it where the library had none of its own.</summary>
	public void OnInstance( nint instance )
	{
		Instance = instance;
		Device = 0;
	}

	public void OnDevice( nint device )
	{
		Device = device;
	}

	/// <summary>The address of a Vulkan function, resolved the way volk resolves it: the device's own where there is a device, the instance's where there is an instance, and the loader's export before either. What Silk resolves its entry points through: this loader, as a context.</summary>
	public INativeContext Context() => new LamdaNativeContext( proc =>
	{
		TryGetProcAddress( proc, out var address );
		return address;
	} );

	public nint GetProcAddress( string proc, int? slot = default )
	{
		TryGetProcAddress( proc, out var address, slot );
		return address;
	}

	public bool TryGetProcAddress( string proc, out nint address, int? slot = default )
	{
		address = 0;

		//The two roots are always the library's own: asking an instance for
		//vkGetInstanceProcAddr is legal but pointless, and asking a device for either is not.
		if ( proc == "vkGetInstanceProcAddr" )
		{
			address = (nint)getInstanceProcAddr;
			return address != 0;
		}

		var bytes = System.Text.Encoding.UTF8.GetByteCount( proc );
		var name = stackalloc byte[bytes + 1];

		fixed ( char* chars = proc )
		{
			System.Text.Encoding.UTF8.GetBytes( chars, proc.Length, name, bytes );
		}

		name[bytes] = 0;

		if ( proc == "vkGetDeviceProcAddr" )
		{
			address = getDeviceProcAddr != null
				? (nint)getDeviceProcAddr
				: Instance != 0 ? getInstanceProcAddr( Instance, name ) : 0;

			return address != 0;
		}

		if ( Device != 0 && !IsInstanceLevel( proc ) )
		{
			var deviceProc = getDeviceProcAddr != null
				? getDeviceProcAddr
				: (delegate* unmanaged[Cdecl]<nint, byte*, nint>)getInstanceProcAddr( Instance, "vkGetDeviceProcAddr"u8.AsPointer() );

			if ( deviceProc != null )
			{
				address = deviceProc( Device, name );
				if ( address != 0 ) return true;
			}
		}

		if ( Instance != 0 )
		{
			address = getInstanceProcAddr( Instance, name );
			if ( address != 0 ) return true;
		}

		//Before any instance: the handful of global functions, which the loader answers for with a null instance
		//- and the library's own exports for a loader old enough to have no vkEnumerateInstanceVersion at all.
		address = getInstanceProcAddr( 0, name );
		if ( address != 0 ) return true;

		return NativeLibrary.TryGetExport( library, proc, out address );
	}

	//Whether a function is instance or physical-device level, which vkGetDeviceProcAddr must not be asked
	private static bool IsInstanceLevel( string proc )
	{
		if ( proc.StartsWith( "vkGetPhysicalDevice", StringComparison.Ordinal ) ||
			proc.StartsWith( "vkEnumeratePhysicalDevice", StringComparison.Ordinal ) ||
			proc.StartsWith( "vkEnumerateDevice", StringComparison.Ordinal ) ||
			proc.StartsWith( "vkEnumerateInstance", StringComparison.Ordinal ) ||
			proc.StartsWith( "vkGetDisplay", StringComparison.Ordinal ) ||
			proc.StartsWith( "vkCreateDisplay", StringComparison.Ordinal ) ||
			proc.Contains( "DebugUtilsMessenger", StringComparison.Ordinal ) ||
			proc.Contains( "DebugReport", StringComparison.Ordinal ) )
		{
			return true;
		}

		if ( proc.StartsWith( "vkCreate", StringComparison.Ordinal ) && proc.Contains( "Surface", StringComparison.Ordinal ) )
			return true;

		return proc is "vkCreateDevice" or "vkDestroyInstance" or "vkDestroySurfaceKHR" or "vkSubmitDebugUtilsMessageEXT";
	}

	public void Dispose()
	{
		//The library stays open: a second device in the same process wants the same loader,
		//and a loader unloaded under a driver is not a thing worth finding out about.
	}
}

internal static class Utf8Pointer
{
	/// <summary>A pointer to a UTF-8 literal, which the compiler keeps in static data.</summary>
	public static unsafe byte* AsPointer( this ReadOnlySpan<byte> literal )
		=> (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer( ref System.Runtime.InteropServices.MemoryMarshal.GetReference( literal ) );
}
