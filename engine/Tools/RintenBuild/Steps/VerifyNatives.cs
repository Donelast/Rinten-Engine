using static Rinten.Build.Constants;

namespace Rinten.Build.Steps;

//Checks every native library the engine opens by name exists in game/bin/<platform>
internal class VerifyNatives( string platformOverride = null )
{
	private readonly Platform platform = Platform.Create();

	//Required DllImport names per platform; Android and wasm32 only carry rintencore
	private static readonly Dictionary<string, string[]> Required = new()
	{
		["win64"] = ["rintencore", "SDL3", "slang"],
		["linuxsteamrt64"] = ["rintencore", "SDL3", "slang"],
		//No Slang: nothing on a headset compiles a shader - an editor is not built for
		//it, and a game ships its stages baked. See StandaloneExporter.GetDllFiles.
		["linuxsteamrtarm64"] = ["rintencore", "SDL3", "openxr_loader"],
		["osxarm64"] = ["rintencore", "SDL3", "slang"],
	};

	//File names a library may have: bare, lib-prefixed, or a versioned soname
	private static IEnumerable<string> Candidates( string name, string platformId )
	{
		if ( platformId == "win64" )
		{
			yield return $"{name}.dll";
			yield break;
		}

		var extension = platformId == "osxarm64" ? "dylib" : "so";

		yield return $"{name}.{extension}";
		yield return $"lib{name}.{extension}";
	}

	public ExitCode Run()
	{
		var platformId = platformOverride ?? platform.PlatformID;

		if ( !Required.TryGetValue( platformId, out var wanted ) )
		{
			Log.Info( $"Nothing to verify for {platformId}." );
			return ExitCode.Success;
		}

		var root = Directory.GetCurrentDirectory();
		var binDir = Path.Combine( root, "game", "bin", platformId );

		if ( !Directory.Exists( binDir ) )
		{
			Log.Error( $"{binDir} is not there. Build the core and run fetch-natives." );
			return ExitCode.Failure;
		}

		var present = Directory.GetFiles( binDir ).Select( Path.GetFileName ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		var missing = new List<string>();

		foreach ( var name in wanted )
		{
			var found = Candidates( name, platformId )
				.Any( candidate => present.Any( file => file.StartsWith( candidate, StringComparison.OrdinalIgnoreCase ) ) );

			if ( !found ) missing.Add( name );
		}

		if ( missing.Count > 0 )
		{
			Log.Error( $"{platformId} is missing {string.Join( ", ", missing )} in game/bin/{platformId}." );
			Log.Error( "The engine opens these by name at runtime, so this is a program that starts and then cannot." );

			return ExitCode.Failure;
		}

		Log.Info( $"{platformId}: all {wanted.Length} of its natives are there." );

		return ExitCode.Success;
	}
}
