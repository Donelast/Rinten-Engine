using System;
using System.Runtime.InteropServices;

namespace EngineTests;

//Native library paths and names per platform, checkable for platforms we can't run here
[TestClass]
public class NativePlatformTest
{
	private static readonly OSPlatform Android = OSPlatform.Create( "ANDROID" );

	[TestMethod]
	public void EveryPlatformHasSomewhereToLookForItsLibraries()
	{
		Assert.AreEqual( "bin/win64/", NetCore.PathFor( OSPlatform.Windows ) );
		Assert.AreEqual( "bin/linuxsteamrt64/", NetCore.PathFor( OSPlatform.Linux ) );
		Assert.AreEqual( "bin/osxarm64/", NetCore.PathFor( OSPlatform.OSX ) );

		//A phone is not a Steam machine, and the runtime's folder is not where its libraries are.
		Assert.AreEqual( "bin/android-arm64/", NetCore.PathFor( Android ) );
		Assert.AreEqual( "bin/wasm32/", NetCore.PathFor( OSPlatform.Create( "BROWSER" ) ) );

		Assert.AreNotEqual( NetCore.PathFor( OSPlatform.Linux ), NetCore.PathFor( Android ) );
	}

	[TestMethod]
	public void WaylandIsTheSessionTheDisplayVariableNames()
	{
		var expected = OperatingSystem.IsLinux()
			&& !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "WAYLAND_DISPLAY" ) );

		Assert.AreEqual( expected, CoreWindow.OnWayland );
	}

	[TestMethod]
	public void SomewhereWeCannotRunSaysSoRatherThanGuessing()
	{
		Assert.ThrowsException<PlatformNotSupportedException>(
			() => NetCore.PathFor( OSPlatform.Create( "SOMETHINGELSE" ) ) );
	}

	[TestMethod]
	public void LibrariesAreNamedTheWayEachPlatformNamesThem()
	{
		Assert.AreEqual( "rintencore.dll", DLLImportResolver.FileNameFor( "rintencore", OSPlatform.Windows ) );
		Assert.AreEqual( "rintencore.dylib", DLLImportResolver.FileNameFor( "rintencore", OSPlatform.OSX ) );
		Assert.AreEqual( "rintencore.so", DLLImportResolver.FileNameFor( "rintencore", OSPlatform.Linux ) );

		//The one that was wrong: a name with no extension at all, which loads nothing and explains nothing.
		Assert.AreEqual( "rintencore.so", DLLImportResolver.FileNameFor( "rintencore", Android ) );
		Assert.AreEqual( "rintencore.wasm", DLLImportResolver.FileNameFor( "rintencore", OSPlatform.Create( "BROWSER" ) ) );
	}

	//And this machine answers as itself.
	[TestMethod]
	public void ThisMachineKnowsWhatItIs()
	{
		Assert.AreEqual( OperatingSystem.IsWindows(), NetCore.Current == OSPlatform.Windows );
		Assert.AreEqual( OperatingSystem.IsMacOS(), NetCore.Current == OSPlatform.OSX );

		Assert.AreEqual( NetCore.PathFor( NetCore.Current ), NetCore.NativeDllPath );
	}
}
