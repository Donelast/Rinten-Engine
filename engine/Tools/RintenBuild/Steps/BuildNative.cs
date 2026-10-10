using static Rinten.Build.Constants;

namespace Rinten.Build.Steps;

//Builds the native core (engine/Core) via its CMake/Ninja platform script and installs it
internal class BuildNative( bool clean = false )
{
	private readonly Platform platform = Platform.Create();

	internal ExitCode Run()
	{
		const string coreDir = "engine/Core";

		var root = Directory.GetCurrentDirectory();

		if ( !File.Exists( Path.Combine( root, "engine", "Core", "CMakeLists.txt" ) ) )
		{
			//A checkout without the core is still a checkout that runs: game/bin carries a built rintencore for the platforms it was built for.
			Log.Warning( "No engine/Core in this checkout - keeping the rintencore already in game/bin." );
			return ExitCode.Success;
		}

		var script = platform.NativeBuildScript;
		if ( !File.Exists( Path.Combine( root, "engine", "Core", script ) ) )
		{
			Log.Error( $"No native build script for {platform.PlatformID} at {coreDir}/{script}." );
			return ExitCode.Failure;
		}

		//"fresh" is what those scripts call deleting the build directory first.
		var arguments = clean ? "fresh" : "";

		Log.Info( $"Building rintencore for {platform.PlatformID}..." );

		//Run through a shell (.cmd needs cmd.exe, .sh may lack +x); ".\" is needed on some Windows setups
		bool built = OperatingSystem.IsWindows()
			? Utility.RunProcess( "cmd.exe", $@"/c .\{script} {arguments}".TrimEnd(), coreDir )
			: Utility.RunProcess( "bash", $"{script} {arguments}".TrimEnd(), coreDir );

		if ( !built )
		{
			Log.Error( "Native build failed. Pass --skip-native to build the managed engine against the rintencore already in game/bin." );
			return ExitCode.Failure;
		}

		return ExitCode.Success;
	}
}
