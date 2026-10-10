using static Rinten.Build.Constants;

namespace Rinten.Build.Steps;

//Builds the engine's own models beside their definitions via the player's -bakemodels, so a release ships them built
internal class BuildModels
{
	internal ExitCode Run()
	{
		var gameDir = Path.Combine( Directory.GetCurrentDirectory(), "game" );
		var player = Path.Combine( gameDir,
			OperatingSystem.IsWindows() ? "Rinten-Standalone.exe" : "Rinten-Standalone" );

		if ( !File.Exists( player ) )
		{
			Log.Error( $"Cannot build models: {player} is not there. Build the managed side first." );
			return ExitCode.Failure;
		}

		Log.Info( "Building the engine's models..." );

		var success = Utility.RunProcess( player, "-bakemodels -headless", gameDir,
			onDataReceived: ( sender, e ) =>
			{
				if ( e.Data != null ) Log.Info( e.Data );
			} );

		if ( !success )
		{
			Log.Error( "Building the engine's models failed." );
			return ExitCode.Failure;
		}

		return ExitCode.Success;
	}
}
