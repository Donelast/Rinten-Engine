using System;
using System.IO;
using static Rinten.Build.Constants;

namespace Rinten.Build.Steps;

internal class BuildAddons
{
	internal ExitCode Run()
	{
		try
		{
			string rootDir = Directory.GetCurrentDirectory();
			string gameDir = Path.Combine( rootDir, "game" );

			Log.Info( "Step 1: Generate solution" );

			string launcher = OperatingSystem.IsWindows() ? "Rinten.exe" : "Rinten";
			string rintenDevPath = Path.Combine( gameDir, launcher );
			if ( !File.Exists( rintenDevPath ) )
			{
				Log.Error( $"Error: {launcher} not found at {rintenDevPath}" );
				return ExitCode.Failure;
			}

			bool gameTestSuccess = Utility.RunProcess(
				rintenDevPath,
				"-generatesolution",
				gameDir
			);

			if ( !gameTestSuccess )
			{
				Log.Error( "Solution generation failed!" );
				return ExitCode.Failure;
			}

			Log.Info( "Step 2: Building Addons" );

			bool addonsSuccess = Utility.RunDotnetCommand(
				gameDir,
				"build \"rinten.slnx\""
			);

			if ( !addonsSuccess )
			{
				Log.Error( "Addons build failed!" );
				return ExitCode.Failure;
			}

			Log.Info( "Step 3: Building Menu" );

			string menuBuildPath = Path.Combine( gameDir, "bin", "managed", "MenuBuild.exe" );
			if ( !File.Exists( menuBuildPath ) )
			{
				Log.Error( $"Error: MenuBuild.exe not found at {menuBuildPath}" );
				return ExitCode.Failure;
			}

			bool menuSuccess = Utility.RunProcess(
				menuBuildPath,
				"",
				gameDir
			);

			if ( !menuSuccess )
			{
				Log.Error( "Menu build failed!" );
				return ExitCode.Failure;
			}

			Log.Info( "Addons and Menu built successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Build operations failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}
}
