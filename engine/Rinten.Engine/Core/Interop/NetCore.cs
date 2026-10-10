using System.Runtime.InteropServices;

﻿internal static class NetCore
{
	//Per-platform native library folders; Android checked before Linux, browser natives are linked in
	internal static readonly OSPlatform AndroidPlatform = OSPlatform.Create( "ANDROID" );
	internal static readonly OSPlatform BrowserPlatform = OSPlatform.Create( "BROWSER" );

	internal static string PathFor( OSPlatform platform )
	{
		if ( platform == OSPlatform.Windows ) return "bin/win64/";
		if ( platform == OSPlatform.OSX ) return "bin/osxarm64/";
		if ( platform == AndroidPlatform ) return "bin/android-arm64/";
		if ( platform == BrowserPlatform ) return "bin/wasm32/";
		if ( platform == OSPlatform.Linux )
		{
			//An arm64 Linux is a Steam Frame's SteamOS - the folder the launcher's own PlatformName arrives at the same way.
			return RuntimeInformation.OSArchitecture == Architecture.Arm64
				? "bin/linuxsteamrtarm64/"
				: "bin/linuxsteamrt64/";
		}

		throw new PlatformNotSupportedException( $"no native libraries for {platform}" );
	}

	static string DefaultNativeDllPath => PathFor( Current );

	//What we are running on.
	internal static OSPlatform Current => true switch
	{
		_ when OperatingSystem.IsWindows() => OSPlatform.Windows,
		_ when OperatingSystem.IsMacOS() => OSPlatform.OSX,
		_ when OperatingSystem.IsAndroid() => AndroidPlatform,
		_ when OperatingSystem.IsBrowser() => BrowserPlatform,
		_ when OperatingSystem.IsLinux() => OSPlatform.Linux,
		_ => throw new PlatformNotSupportedException()
	};

	//Interop will try to load dlls from this path, e.g bin/win64/
	internal static string NativeDllPath { get; set; } = DefaultNativeDllPath;

	//From here we'll open the native dlls and inject our function pointers into them, and retrieve function pointers from them.
	internal static void InitializeInterop( string gameFolder )
	{
		//make sure currentdir to the game folder. This is just to setr a baseline for the rest of the managed system to work with
		//- since they can all assume CurrentDirectory is where you would expect it to be instead of in the fucking bin folder.
		System.Environment.CurrentDirectory = gameFolder;
	}
}
