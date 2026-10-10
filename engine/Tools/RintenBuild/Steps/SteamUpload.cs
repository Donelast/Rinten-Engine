using static Rinten.Build.Constants;

namespace Rinten.Build.Steps;

//Stages the same layout PackageRelease makes and pushes it to a Steam depot with steamcmd, so a friend on the branch gets the engine from Steam.
internal class SteamUpload
{
	internal const string DefaultAppId = "3302940";
	internal const string DefaultDepotId = "3302941";

	readonly string appId;
	readonly string depotId;
	readonly string branch;
	readonly string user;

	internal SteamUpload( string appId, string depotId, string branch, string user )
	{
		this.appId = appId;
		this.depotId = depotId;
		this.branch = branch ?? Environment.GetEnvironmentVariable( "RINTEN_STEAM_BRANCH" );
		this.user = user ?? Environment.GetEnvironmentVariable( "RINTEN_STEAM_USER" );
	}

	internal ExitCode Run()
	{
		if ( string.IsNullOrWhiteSpace( user ) )
		{
			Log.Error( "No Steam build account. Set RINTEN_STEAM_USER or pass --user." );
			return ExitCode.Failure;
		}

		var rootDir = Directory.GetCurrentDirectory();
		var commit = GetCommit() ?? "dev";
		var shortCommit = commit.Length > 8 ? commit[..8] : commit;

		//Staged under dist/steam/ rather than dist/ so a release zip's folder is left alone.
		var output = Path.Combine( "dist", "steam" );
		var version = $"steam-{shortCommit}";

		if ( new PackageRelease().Run( version, output, zip: false ) != ExitCode.Success )
			return ExitCode.Failure;

		var stageDir = Path.Combine( rootDir, output, $"rinten-{version}-{Platform.Create().PlatformID}" );
		var buildOutput = Path.Combine( rootDir, output, "steamcmd_output" );
		var scriptDir = Path.Combine( rootDir, output, "scripts" );

		Directory.CreateDirectory( buildOutput );
		Directory.CreateDirectory( scriptDir );

		var depotScript = Path.Combine( scriptDir, $"depot_build_{depotId}.vdf" );
		var appScript = Path.Combine( scriptDir, $"app_build_{appId}.vdf" );

		//An explicit file list per depot, rather than the app script's inline mapping,
		//so "." is the install root and the launcher ends up beside the engine/ folder.
		File.WriteAllText( depotScript, $$"""
			"DepotBuildConfig"
			{
				"DepotID" "{{depotId}}"
				"ContentRoot" "{{stageDir}}"
				"FileMapping"
				{
					"LocalPath" "*"
					"DepotPath" "."
					"Recursive" "1"
				}
			}
			""" );

		var setLive = string.IsNullOrWhiteSpace( branch ) ? "" : $"\t\"SetLive\" \"{branch}\"\n";

		File.WriteAllText( appScript, $$"""
			"AppBuild"
			{
				"AppID" "{{appId}}"
				"Desc" "{{shortCommit}}"
				"BuildOutput" "{{buildOutput}}"
			{{setLive}}	"Depots"
				{
					"{{depotId}}" "{{depotScript}}"
				}
			}
			""" );

		if ( string.IsNullOrWhiteSpace( branch ) )
			Log.Warning( "No branch given (RINTEN_STEAM_BRANCH / --branch): the build is uploaded but not set live. Set it live from the Builds page." );

		Log.Info( $"Uploading {shortCommit} to app {appId}, depot {depotId}" );

		//Steam Guard: log in once by hand (steamcmd +login <user> +quit) so the sentry is cached.
		if ( !Utility.RunProcess( "steamcmd", $"+login {user} +run_app_build \"{appScript}\" +quit" ) )
		{
			Log.Error( "steamcmd failed. Is it installed and logged in as that account?" );
			return ExitCode.Failure;
		}

		Log.Info( "Steam upload finished" );
		return ExitCode.Success;
	}

	static string GetCommit()
	{
		string head = null;
		Utility.RunProcess( "git", "rev-parse HEAD", onDataReceived: ( _, e ) =>
		{
			if ( !string.IsNullOrWhiteSpace( e.Data ) )
				head ??= e.Data.Trim();
		} );

		return head;
	}
}
