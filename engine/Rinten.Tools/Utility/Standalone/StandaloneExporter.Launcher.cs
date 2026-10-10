using System;
using System.Diagnostics;
using System.IO;

namespace Editor;

//The game's native launcher (a .NET apphost), the one per-platform file; the SDK builds any target
partial class StandaloneExporter
{
	//The launcher for a platform: the program, the assembly it runs and the runtime configuration beside it. Null where it could not be built.
	private sealed record LauncherFiles( string Program, string Assembly, string RuntimeConfig )
	{
		/// <summary>The folder it was published into, where that is what it is - the engine's assemblies compiled for the target are in it beside the program. Null for the one that sits beside the engine, whose folder is the engine's own.</summary>
		public string Published { get; init; }
	}

	//Where the engine's assemblies, compiled ahead of time for the platform being exported for, are to be taken
	//from - see GetManagedFiles. Null where there are none and the build ships the ones the editor runs on.
	private string publishedManaged;

	//Host platform launcher is already built; others are published on demand and cached beside the engine
	private LauncherFiles BuildLauncher( ExportPlatform platform )
	{
		var engineDir = Environment.CurrentDirectory;
		var standalone = EngineExecutables.Standalone;

		//The one built beside the engine is the host's own, and it is the same apphost a publish would write - so exporting for this
		//machine costs nothing. Publishing it instead was half a minute of an export, spent to arrive at the file that was already there.
		if ( platform.Id == ExportPlatform.Host.Id )
		{
			var beside = new LauncherFiles(
				Path.Combine( engineDir, platform.Executable( standalone ) ),
				Path.Combine( engineDir, $"{standalone}.dll" ),
				Path.Combine( engineDir, $"{standalone}.runtimeconfig.json" ) );

			if ( File.Exists( beside.Program ) ) return beside;
		}

		var cached = Path.Combine( engineDir, "launchers", platform.Id );
		var program = Path.Combine( cached, platform.Executable( standalone ) );

		LauncherFiles Published()
		{
			publishedManaged = cached;

			return new LauncherFiles( program,
				Path.Combine( cached, $"{standalone}.dll" ),
				Path.Combine( cached, $"{standalone}.runtimeconfig.json" ) ) { Published = cached };
		}

		//What was published last time, where the engine has not been rebuilt since. The folder holds the engine's assemblies compiled for that platform,
		//so one left over from before an engine build is last week's engine - the apphost alone could be kept, but nothing in the folder could.
		//A release ships these folders and no source to publish them again from, so its own are taken as they are
		if ( File.Exists( program ) && ( !File.Exists( StandaloneProject ) || !Stale( cached, engineDir ) ) ) return Published();

		if ( PublishLauncher( platform, cached ) && File.Exists( program ) ) return Published();

		//Nothing published - no SDK, no source beside the engine, or a platform this machine cannot build for.
		Logger.Warning( $"there is no launcher for {platform.Title}" );

		return null;
	}

	//The game launcher's project, which only an engine with its source has.
	private static string StandaloneProject => Path.GetFullPath( Path.Combine( Environment.CurrentDirectory,
		"..", "engine", "Launcher", "RintenStandalone", $"{EngineExecutables.Standalone}.csproj" ) );

	//Where the engine's own source is, beside the folder the editor runs from.
	private static string EngineSource()
		=> Path.GetFullPath( Path.Combine( Environment.CurrentDirectory, "..", "engine" ) );

	//Engine assemblies AOT-compiled for the platform (cached, rebuilt if stale), or null to ship IL
	private string AheadOfTimeEngine( ExportPlatform platform )
	{
		//A page's assemblies are the browser's to compile, and a phone's are built into the apk by its own publish.
		if ( platform.IsWeb || platform.IsAndroid ) return null;

		var engineDir = Environment.CurrentDirectory;
		var into = Path.Combine( engineDir, "precompiled", platform.Id );

		if ( Directory.Exists( into ) && !Stale( into, engineDir ) ) return into;

		var project = Path.Combine( EngineSource(), "Rinten.Engine", "Rinten.Engine.csproj" );

		if ( !File.Exists( project ) )
		{
			Logger.Info( "the engine's source is not beside it, so its assemblies cannot be compiled ahead of " +
				"time - the game's code is compiled on the player's machine as it runs" );

			return null;
		}

		Logger.Info( $"Compiling the engine ahead of time for {platform.Title}.." );

		//Its own build folder: the project writes to game/bin/managed, which is the folder
		//the editor is running out of, and a publish for another platform must not land in it.
		var building = Path.Combine( into, "build" );

		var arguments = $"publish \"{project}\" -c Release -r {platform.RuntimeIdentifier} " +
			$"--self-contained false -p:PublishReadyToRun=true " +
			$"-p:OutputPath=\"{building}{Path.DirectorySeparatorChar}\" -p:PublishDir=\"{into}{Path.DirectorySeparatorChar}\"";

		try
		{
			using var process = Process.Start( ExportToolchain.Start( arguments ) );

			if ( process is null )
			{
				Logger.Info( "the .NET SDK is not on the path, so the engine cannot be compiled ahead of time - " +
					"the game's code is compiled on the player's machine as it runs" );

				return null;
			}

			var output = process.StandardOutput.ReadToEnd();
			var errors = process.StandardError.ReadToEnd();

			process.WaitForExit();

			if ( process.ExitCode == 0 && File.Exists( Path.Combine( into, "Rinten.Engine.dll" ) ) ) return into;

			//Said rather than thrown: what this buys is a faster start, and a build without it is a build.
			Logger.Warning( $"the engine would not compile ahead of time for {platform.Title} - the game's code is " +
				$"compiled on the player's machine as it runs:\n{output}\n{errors}" );

			return null;
		}
		catch ( Exception e )
		{
			Logger.Warning( $"the engine would not compile ahead of time for {platform.Title} ({e.Message}) - " +
				"the game's code is compiled on the player's machine as it runs" );

			return null;
		}
	}

	//Whether what was published into folder is older than the engine it was published from.
	private static bool Stale( string folder, string engineDir )
	{
		var engine = Path.Combine( engineDir, "bin", "managed", "Rinten.Engine.dll" );
		var published = Path.Combine( folder, "Rinten.Engine.dll" );

		try
		{
			if ( !File.Exists( engine ) || !File.Exists( published ) ) return true;

			return File.GetLastWriteTimeUtc( published ) < File.GetLastWriteTimeUtc( engine );
		}
		catch ( IOException )
		{
			return true;
		}
	}

	//Publishes a launcher for another platform: framework-dependent, unless the platform ships its runtime
	private bool PublishLauncher( ExportPlatform platform, string into )
	{
		var project = StandaloneProject;

		if ( !File.Exists( project ) )
		{
			Logger.Warning( $"cannot build a {platform.Title} launcher: {project} is not there. " +
				"An engine installed without its source can only export for the machine it is on." );
			return false;
		}

		Logger.Info( $"Building a {platform.Title} launcher.." );

		//SolutionDir must be set or the publish writes to /bin/obj; deps file only when self-contained
		var selfContained = platform.ShipsRuntime ? "true -p:GenerateDependencyFile=true" : "false";

		//Build in its own folder so it can't overwrite the host launcher's files in game/
		var building = Path.Combine( into, "build" );

		var arguments = $"publish \"{project}\" -c Release -r {platform.RuntimeIdentifier} " +
			$"--self-contained {selfContained} -p:PublishSingleFile=false " +
			$"-p:OutputPath=\"{building}{Path.DirectorySeparatorChar}\" " +
			$"-p:SolutionDir=\"{EngineSource()}{Path.DirectorySeparatorChar}\" -o \"{into}\"";

		try
		{
			using var process = Process.Start( ExportToolchain.Start( arguments ) );

			if ( process is null )
			{
				Logger.Warning( "cannot build a launcher: the .NET SDK is not on the path" );
				return false;
			}

			var output = process.StandardOutput.ReadToEnd();
			var errors = process.StandardError.ReadToEnd();

			process.WaitForExit();

			if ( process.ExitCode == 0 ) return true;

			//The publish output rather than a summary: what went wrong is in it, and there is nowhere else to read it from.
			Logger.Warning( $"could not build a {platform.Title} launcher:\n{output}\n{errors}" );
			return false;
		}
		catch ( Exception e )
		{
			Logger.Warning( $"could not build a {platform.Title} launcher: {e.Message}" );
			return false;
		}
	}

	//Runtime pack files of a self-contained launcher, read from its deps file (which is not shipped)
	private static IEnumerable<string> RuntimeFiles( ExportPlatform platform, LauncherFiles launcher )
	{
		if ( !platform.ShipsRuntime || launcher?.Published is not { } folder ) return [];

		var deps = Path.Combine( folder, $"{EngineExecutables.Standalone}.deps.json" );

		if ( !File.Exists( deps ) )
		{
			Logger.Warning( $"the {platform.Title} launcher says nothing of its runtime ({deps} is not there) - " +
				"the build has no .NET and will not start" );

			return [];
		}

		var names = new HashSet<string>( StringComparer.Ordinal );

		try
		{
			using var document = System.Text.Json.JsonDocument.Parse( File.ReadAllText( deps ) );

			foreach ( var target in document.RootElement.GetProperty( "targets" ).EnumerateObject() )
			{
				foreach ( var library in target.Value.EnumerateObject() )
				{
					if ( !library.Name.StartsWith( "runtimepack.", StringComparison.Ordinal ) ) continue;

					foreach ( var kind in new[] { "runtime", "native" } )
					{
						if ( !library.Value.TryGetProperty( kind, out var files ) ) continue;

						foreach ( var file in files.EnumerateObject() )
							names.Add( Path.GetFileName( file.Name ) );
					}
				}
			}
		}
		catch ( Exception e ) when ( e is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException )
		{
			Logger.Warning( $"the {platform.Title} launcher's {Path.GetFileName( deps )} would not read ({e.Message}) - " +
				"the build has no .NET and will not start" );

			return [];
		}

		return names
			.Select( name => Path.Combine( folder, name ) )
			.Where( File.Exists )
			.ToList();
	}
}
