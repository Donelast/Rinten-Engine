using static Rinten.Build.Constants;

namespace Rinten.Build.Steps;

internal class NvPatch
{
	internal ExitCode Run()
	{
		string rootDir = Directory.GetCurrentDirectory();
		string nvpatchPath = Path.Combine( rootDir, "engine", "ThirdParty", "nvpatch", "nvpatch.exe" );

		try
		{
			if ( !File.Exists( nvpatchPath ) )
			{
				Log.Error( $"Error: NVPatch executable not found at {nvpatchPath}" );
				return ExitCode.Failure;
			}

			//One executable, patched once. There were two here, both the same path - from when the editor and the game were separate programs.
			Log.Info( "Running nvpatch on Rinten.exe" );
			string editorPath = Path.Combine( rootDir, "game", "Rinten.exe" );
			if ( !RunNvPatch( nvpatchPath, editorPath ) )
				return ExitCode.Failure;

			Console.WriteLine( "NVPatch operations completed successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"NVPatch operations failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	static bool RunNvPatch( string nvpatchPath, string targetExePath )
	{
		if ( !File.Exists( targetExePath ) )
		{
			Log.Warning( $"Warning: Target executable not found at {targetExePath}" );
			Log.Warning( "Skipping this nvpatch operation." );
			return true;
		}

		return Utility.RunProcess( nvpatchPath, $"--enable \"{targetExePath}\"", null );
	}
}
