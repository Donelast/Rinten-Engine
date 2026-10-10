using static Rinten.Build.Constants;

namespace Rinten.Build.Steps;

internal class GameCache
{
	internal ExitCode Run()
	{
		string rootDir = Directory.GetCurrentDirectory();
		string executable = OperatingSystem.IsWindows() ? "CreateGameCache.exe" : "CreateGameCache";
		string exePath = Path.Combine( rootDir, "engine", "Tools", "CreateGameCache", "bin", executable );

		try
		{
			Utility.RunProcess( exePath, "--quiet", null );
			Console.WriteLine( "GameCache operations completed successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"GameCache operations failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}
}
