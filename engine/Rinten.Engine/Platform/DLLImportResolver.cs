using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;

namespace Rinten;

internal static class DLLImportResolver
{
	//Assemblies with a resolver; locked because assemblies load on many threads (background compiles)
	private static readonly HashSet<string> registeredAssemblies = new();

	private static readonly object resolverLock = new();

	private static bool watchingForNewAssemblies;

	//Installed on module load so the resolver exists before any native library is called
	[System.Runtime.CompilerServices.ModuleInitializer]
	internal static void Install() => SetupResolvers();

	internal static void SetupResolvers()
	{
		//Register for all currently loaded assemblies
		foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
			TryRegister( assembly );

		lock ( resolverLock )
		{
			//More than one caller asks for this - engine startup, and any host that never starts the engine - so only the first one subscribes.
			if ( watchingForNewAssemblies ) return;
			watchingForNewAssemblies = true;
		}

		//Register for any assemblies loaded in the future
		AppDomain.CurrentDomain.AssemblyLoad += ( _, args ) => TryRegister( args.LoadedAssembly );
	}

	private static void TryRegister( Assembly assembly )
	{
		if ( assembly.IsDynamic )
			return;

		//SetDllImportResolver throws if called twice for the same assembly. Under the lock,
		//because this runs on whoever's thread loaded the assembly - see registeredAssemblies.
		lock ( resolverLock )
		{
			if ( !registeredAssemblies.Add( assembly.FullName ) )
				return;
		}

		try
		{
			NativeLibrary.SetDllImportResolver( assembly, ResolveFromNativePath );
		}
		catch ( InvalidOperationException )
		{
			//Already set - by another copy of this class, in another load context, for the same assembly. The set above is per copy and the
			//resolver is per assembly, so the second copy cannot know. Either resolver finds the same libraries, so this is nothing to report.
		}
	}

	private static IntPtr ResolveFromNativePath( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
	{
		//Linked into the WASM module; a path search would look for a .so that is not there and hide the symbols the linker already kept.
		if ( OperatingSystem.IsBrowser() ) return IntPtr.Zero;

		foreach ( var nativeName in FileNamesFor( libraryName, NetCore.Current ) )
		{
			if ( NativeLibrary.TryLoad( Path.Combine( NetCore.NativeDllPath, nativeName ), out var handle ) )
			{
				return handle;
			}

			//Android refuses to open a library out of a folder the app can write to, so the path above
			//can never find one there. What is in the package is opened by its name and by nothing else.
			if ( OperatingSystem.IsAndroid() && NativeLibrary.TryLoad( nativeName, out handle ) )
			{
				return handle;
			}

			var fromRepository = ResolveFromRepository( nativeName, assembly );

			if ( fromRepository != IntPtr.Zero ) return fromRepository;
		}

		return IntPtr.Zero;
	}

	//Candidate file names, best guess first: bare name (ours), then Unix lib-prefixed (e.g. libSDL3.so)
	internal static IEnumerable<string> FileNamesFor( string libraryName, OSPlatform platform )
	{
		yield return FileNameFor( libraryName, platform );

		if ( platform == OSPlatform.Windows ) yield break;
		if ( platform == NetCore.BrowserPlatform ) yield break;
		if ( libraryName.StartsWith( "lib", StringComparison.Ordinal ) ) yield break;

		yield return FileNameFor( $"lib{libraryName}", platform );
	}

	//What a library is called as a file on this platform. Android is a Linux and its libraries are named like one's, but it is asked about
	//separately because .NET answers no to IsLinux there - and a name left without its extension is a library that is simply not found.
	internal static string FileNameFor( string libraryName, OSPlatform platform )
	{
		if ( platform == OSPlatform.Windows ) return $"{libraryName}.dll";
		if ( platform == OSPlatform.OSX ) return $"{libraryName}.dylib";
		if ( platform == NetCore.BrowserPlatform ) return $"{libraryName}.wasm";

		//Linux and Android both.
		return $"{libraryName}.so";
	}

	//Last resort: search up from the calling assembly, for hosts (tests) not run from the game folder
	private static IntPtr ResolveFromRepository( string nativeName, Assembly assembly )
	{
		var directory = Path.GetDirectoryName( assembly.Location );

		while ( !string.IsNullOrEmpty( directory ) )
		{
			var candidate = Path.Combine( directory, "game", NetCore.NativeDllPath, nativeName );

			if ( File.Exists( candidate ) && NativeLibrary.TryLoad( candidate, out var handle ) )
				return handle;

			directory = Path.GetDirectoryName( directory );
		}

		return IntPtr.Zero;
	}
}
