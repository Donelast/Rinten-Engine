using static Rinten.Build.Constants;

namespace Rinten.Build.Steps;

//Pre-bakes engine shaders to a cache via the player's -bakeshaders, for targets without Slang
internal class BuildShaders( bool forced = false )
{
	internal ExitCode Run()
	{
		try
		{
			var rootDir = Directory.GetCurrentDirectory();
			var gameDir = Path.Combine( rootDir, "game" );

			//Spelled out rather than taken from EngineExecutables: this tool does not reference
			//the engine's assemblies, and every other step here names the programs the same way.
			var player = Path.Combine( gameDir,
				OperatingSystem.IsWindows() ? "Rinten-Standalone.exe" : "Rinten-Standalone" );

			if ( !File.Exists( player ) )
			{
				Log.Error( $"Cannot bake shaders: {player} is not there. Build the managed side first." );
				return ExitCode.Failure;
			}

			//Kept in step with CoreShaderCache.FileName by hand, for the same reason as the name above.
			var cache = Path.Combine( gameDir, "core", "shaders", "builtin.shadercache" );

			//The cache is keyed by a hash of each shader's own source, so one that is already right stays right and one that is stale
			//misses and is rebuilt. Forcing throws the file away instead, for when the question is whether it can be built from nothing.
			if ( forced && File.Exists( cache ) )
			{
				Log.Info( "Discarding the shader cache and baking it again." );
				File.Delete( cache );
			}

			Log.Info( "Baking shaders..." );

			var success = Utility.RunProcess(
				player,
				"-bakeshaders -headless",
				gameDir,
				onDataReceived: ( sender, e ) =>
				{
					if ( e.Data != null ) Log.Info( e.Data );
				}
			);

			if ( !success )
			{
				Log.Error( "Shader baking failed." );
				return ExitCode.Failure;
			}

			if ( !File.Exists( cache ) )
			{
				Log.Error( $"Shader baking reported success and {cache} is not there." );
				return ExitCode.Failure;
			}

			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Shader compilation failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}
}
