using static Rinten.Build.Constants;

namespace Rinten.Build.Steps;

internal class BuildManaged( bool clean = false )
{
	internal ExitCode Run()
	{
		string engineDir = Path.Combine( Directory.GetCurrentDirectory(), "engine" );
		string rootDir = Directory.GetCurrentDirectory();

		try
		{
			Log.Info( "Step 1: Dotnet Clean" );
			if ( clean )
			{
				if ( !Utility.RunDotnetCommand( engineDir, "clean" ) )
					return ExitCode.Failure;
			}
			else
			{
				Log.Info( "Skipping dotnet clean as cleanBuild is false." );
			}

			//Named rather than left to the folder: a bare restore takes whatever solution it finds there, and step 5 has to build the same one.
			Log.Info( "Step 2: Dotnet Restore" );
			if ( !Utility.RunDotnetCommand( engineDir, "restore Rinten-Engine.slnx" ) )
				return ExitCode.Failure;

			Log.Info( "Step 3: Build CodeGen.exe" );
			if ( !Utility.RunDotnetCommand( engineDir, "build Tools/CodeGen/ -o Tools/CodeGen/bin" ) )
				return ExitCode.Failure;

			Log.Info( "Step 3a: Build CreateGameCache.exe" );
			if ( !Utility.RunDotnetCommand( engineDir, "build Tools/CreateGameCache/ -o Tools/CreateGameCache/bin" ) )
				return ExitCode.Failure;

			Log.Info( "Step 4: Clear managed folder" );
			string managedDir = Path.Combine( rootDir, "game", "bin", "managed" );
			if ( !clean )
			{
				Log.Info( "Skipping managed folder cleanup for incremental build." );
			}
			else if ( Directory.Exists( managedDir ) )
			{
				try
				{
					Directory.Delete( managedDir, true );
					Directory.CreateDirectory( managedDir ); //Recreate the empty directory
					Log.Info( $"Successfully cleared directory: {managedDir}" );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"Warning: Failed to clear directory: {managedDir}. Error: {ex.Message}" );
					//Continue execution since this is a warning in the original script
				}
			}
			else
			{
				Log.Info( $"Directory does not exist, creating: {managedDir}" );
				Directory.CreateDirectory( managedDir );
			}

			Log.Info( "Step 5: Build Managed" );
			if ( !Utility.RunDotnetCommand( engineDir, "build -c Release Rinten-Engine.slnx -p:TreatWarningsAsErrors=true" ) )
				return ExitCode.Failure;

			Log.Info( "Step 6: Install the restored native assets" );
			InstallRestoredNatives( rootDir );

			Log.Info( "Build completed successfully!" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Build failed with error: {ex}" );
			return ExitCode.Failure;
		}
	}

	//Copies NuGet native libraries (Skia, HarfBuzz) from runtimes/<rid>/native next to engine libs
	private static void InstallRestoredNatives( string rootDir )
	{
		var runtimesDir = Path.Combine( rootDir, "game", "bin", "managed", "runtimes" );

		if ( !Directory.Exists( runtimesDir ) )
			return;

		var host = Platform.Create().PlatformID;

		foreach ( var (bin, rids) in NativeTargets )
		{
			var nativeDir = Path.Combine( rootDir, "game", "bin", bin );

			//The machine's own always gets one. The others only where a core for that platform is already there: an otherwise empty bin folder tells
			//the exporter it has native libraries when what it has is Skia and no engine, and that swallows the warning that would have said so.
			if ( bin == host )
				Directory.CreateDirectory( nativeDir );
			else if ( !Directory.Exists( nativeDir ) )
				continue;

			//Most specific identifier first, and once a name has been taken the broader folders do not get to overwrite it.
			var taken = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

			foreach ( var rid in rids )
			{
				var from = Path.Combine( runtimesDir, rid, "native" );

				if ( !Directory.Exists( from ) )
					continue;

				foreach ( var file in Directory.GetFiles( from ) )
				{
					var name = Path.GetFileName( file );

					if ( !taken.Add( name ) )
						continue;

					try
					{
						var into = Path.Combine( nativeDir, name );

						//The same library under a second name is a link to the first, not another copy of it. SkiaSharp's package carries
						//libSkiaSharp.so and libSkiaSharp.so.116.0.0, the same nine megabytes twice, and every export carried both.
						if ( Twin( file, from, taken ) is { } first && !OperatingSystem.IsWindows() )
						{
							if ( new FileInfo( into ) is { Exists: true } or { LinkTarget: not null } )
								File.Delete( into );

							File.CreateSymbolicLink( into, first );
							Log.Info( $"  {name} -> game/bin/{bin} (a link to {first})" );
							continue;
						}

						File.Copy( file, into, overwrite: true );
					}
					catch ( IOException )
					{
						//Held open by something that is running - an editor, a launcher. The file it is holding is the same file
						//this would have written, so saying so is the whole of what wants doing; failing the build over it is not.
						Log.Warning( $"  {name} is in use - keeping the one already in game/bin/{bin}" );
						continue;
					}

					Log.Info( $"  {name} -> game/bin/{bin}" );
				}
			}
		}
	}

	//Name an identical library was already copied under (by content), or null if it's unique
	private static string Twin( string file, string folder, HashSet<string> taken )
	{
		var length = new FileInfo( file ).Length;

		foreach ( var name in taken )
		{
			if ( string.Equals( name, Path.GetFileName( file ), StringComparison.Ordinal ) ) continue;

			var other = Path.Combine( folder, name );

			if ( !File.Exists( other ) || new FileInfo( other ).Length != length ) continue;
			if ( !File.ReadAllBytes( other ).AsSpan().SequenceEqual( File.ReadAllBytes( file ) ) ) continue;

			return name;
		}

		return null;
	}

	//Every exportable platform's bin folder and its RIDs, so cross-exported games still get Skia
	private static readonly (string Bin, string[] Rids)[] NativeTargets =
	[
		( "win64", [ "win-x64", "win" ] ),
		( "linuxsteamrt64", [ "linux-x64", "linux" ] ),
		( "linuxsteamrtarm64", [ "linux-arm64" ] ),
		( "osxarm64", [ "osx-arm64", "osx" ] ),
	];
}
