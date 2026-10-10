using System.IO.Compression;
using static Rinten.Build.Constants;

namespace Rinten.Build.Steps;

//Assembles a release: binaries on top, internals under engine/, no dev debris
internal class PackageRelease
{
	//Folders that move under engine/. Anything not listed doesn't ship.
	static readonly string[] ContentFolders =
	[
		"bin",
		"core",
		"addons",
		"editor",
		"config",
		"mount",
		"net",
		"launchers",
		"templates",
		"samples",
		"thirdpartylegalnotices",
		"_redist",
	];

	//Dev and runtime debris that must not leak into a release even though it lives inside folders that do ship.
	static readonly string[] ExcludedDirectoryNames = [ "obj", ".rinten", "logs" ];

	static readonly string[] ExcludedExtensions = [ ".pdb", ".log" ];

	internal ExitCode Run( string version, string output, bool zip = true )
	{
		var rootDir = Directory.GetCurrentDirectory();
		var gameDir = Path.Combine( rootDir, "game" );
		var launcherName = OperatingSystem.IsWindows() ? "Rinten.exe" : "Rinten";

		if ( !File.Exists( Path.Combine( gameDir, launcherName ) ) )
		{
			Log.Error( $"No {launcherName} under {gameDir} - build the engine first, and run this from the repo root." );
			return ExitCode.Failure;
		}

		var name = $"rinten-{version}-{Platform.Create().PlatformID}";
		var stageDir = Path.Combine( rootDir, output, name );
		var zipPath = Path.Combine( rootDir, output, $"{name}.zip" );

		try
		{
			if ( Directory.Exists( stageDir ) )
				Directory.Delete( stageDir, true );

			Directory.CreateDirectory( stageDir );

			//Binaries at the top - the exes, their dlls and runtime configs. This is everything a person sees when they open the folder.
			Log.Info( "Copying binaries" );

			foreach ( var file in Directory.GetFiles( gameDir ) )
			{
				var fileName = Path.GetFileName( file );

				if ( !fileName.StartsWith( "Rinten", StringComparison.OrdinalIgnoreCase ) )
					continue;

				File.Copy( file, Path.Combine( stageDir, fileName ) );
			}

			//Everything else in one folder
			var engineDir = Path.Combine( stageDir, "engine" );

			foreach ( var folder in ContentFolders )
			{
				var source = Path.Combine( gameDir, folder );

				if ( !Directory.Exists( source ) )
				{
					Log.Info( $"  engine/{folder} - not in this tree, skipped" );
					continue;
				}

				Log.Info( $"  engine/{folder}" );
				CopyTree( source, Path.Combine( engineDir, folder ) );
			}

			//The web and Android hosts, compiled (build-export-kits): where the exporter looks for the engine's own projects
			var kits = Path.Combine( rootDir, "dist", "kits" );

			if ( Directory.Exists( kits ) )
			{
				Log.Info( "  engine/Launcher - export kits" );
				CopyTree( kits, Path.Combine( engineDir, "Launcher" ) );
			}
			else
			{
				Log.Warning( "  no export kits in dist/kits - this release cannot export to the web or Android" );
			}

			ShareManaged( engineDir );

			//Linux only: a Windows zip is unpacked where a link is a text file
			if ( OperatingSystem.IsLinux() )
				LinkDuplicateLibraries( engineDir );

			//Desktop integration, for whoever unpacks this on Linux. The script goes at the top beside the binaries it will point a menu entry at, and
			//the icons under engine/ with the rest of the content - which is the second of the two layouts install-desktop.sh knows how to find itself in.
			var desktopScript = Path.Combine( rootDir, "install-desktop.sh" );
			var linuxBrand = Path.Combine( rootDir, "brand", "linux" );

			if ( File.Exists( desktopScript ) && Directory.Exists( linuxBrand ) )
			{
				Log.Info( "  install-desktop.sh" );
				File.Copy( desktopScript, Path.Combine( stageDir, "install-desktop.sh" ) );
				CopyTree( linuxBrand, Path.Combine( engineDir, "brand", "linux" ) );
			}
			else
			{
				Log.Info( "  install-desktop.sh - not in this tree, skipped" );
			}

			//Read by Application.TryLoadVersionInfo: the version on line one, the date on line five
			var date = DateTime.UtcNow.ToString( "dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture );
			File.WriteAllText( Path.Combine( engineDir, ".version" ), $"{version}\n\n\n\n{date}\n" );

			if ( !zip )
			{
				Log.Info( $"Staged {stageDir}" );
				return ExitCode.Success;
			}

			Log.Info( $"Zipping {name}" );

			File.Delete( zipPath );
			Zip( stageDir, zipPath );

			var size = new FileInfo( zipPath ).Length;
			Log.Info( $"Packaged {zipPath} ({size / (1024.0 * 1024.0):0.0} MB)" );

			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Packaging failed: {ex}" );
			return ExitCode.Failure;
		}
	}

	static void CopyTree( string source, string destination )
	{
		Directory.CreateDirectory( destination );

		foreach ( var file in Directory.GetFiles( source ) )
		{
			if ( ExcludedExtensions.Contains( Path.GetExtension( file ), StringComparer.OrdinalIgnoreCase ) )
				continue;

			if ( IsPrecompressedCopy( file ) )
				continue;

			File.Copy( file, Path.Combine( destination, Path.GetFileName( file ) ) );
		}

		foreach ( var dir in Directory.GetDirectories( source ) )
		{
			var dirName = Path.GetFileName( dir );

			if ( ExcludedDirectoryNames.Contains( dirName, StringComparer.OrdinalIgnoreCase ) )
				continue;

			if ( IsRestoredNativeFolder( dir ) )
				continue;

			CopyTree( dir, Path.Combine( destination, dirName ) );
		}
	}

	//Drops byte-identical copies of bin/managed from the game launchers and the export kits. The exporter
	//falls back to bin/managed for a launcher; a kit lists what it left out in prebuilt/Shared.props.
	static void ShareManaged( string engineDir )
	{
		var managed = Path.Combine( engineDir, "bin", "managed" );
		long saved = 0;

		bool SameAsManaged( string file )
		{
			var twin = Path.Combine( managed, Path.GetFileName( file ) );
			if ( !File.Exists( twin ) || new FileInfo( twin ).Length != new FileInfo( file ).Length )
				return false;

			if ( !File.ReadAllBytes( twin ).AsSpan().SequenceEqual( File.ReadAllBytes( file ) ) )
				return false;

			saved += new FileInfo( file ).Length;
			return true;
		}

		var launchers = Path.Combine( engineDir, "launchers" );
		if ( Directory.Exists( launchers ) )
		{
			foreach ( var file in Directory.GetDirectories( launchers ).SelectMany( d => Directory.GetFiles( d, "*.dll" ) ) )
			{
				if ( SameAsManaged( file ) )
					File.Delete( file );
			}
		}

		var kits = Path.Combine( engineDir, "Launcher" );
		if ( Directory.Exists( kits ) )
		{
			foreach ( var prebuilt in Directory.GetDirectories( kits ).Select( d => Path.Combine( d, "prebuilt" ) ).Where( Directory.Exists ) )
			{
				var shared = Directory.GetFiles( prebuilt, "*.dll" ).Where( SameAsManaged ).ToList();
				if ( shared.Count == 0 )
					continue;

				var props = new System.Text.StringBuilder( "<Project>\n\t<ItemGroup>\n" );
				foreach ( var file in shared.OrderBy( f => f, StringComparer.Ordinal ) )
				{
					props.Append( $"\t\t<Reference Include=\"$(MSBuildThisFileDirectory)../../../bin/managed/{Path.GetFileName( file )}\" Private=\"true\" />\n" );
					File.Delete( file );
				}

				props.Append( "\t</ItemGroup>\n</Project>\n" );
				File.WriteAllText( Path.Combine( prebuilt, "Shared.props" ), props.ToString() );
			}
		}

		Log.Info( $"  {saved / (1024.0 * 1024.0):0.0} MB of bin/managed copies left out" );
	}

	//Byte-identical .so files in a Linux bin folder (links that an artifact upload turned into copies)
	//become links again, to the longest name: the versioned file the others pointed at
	static void LinkDuplicateLibraries( string engineDir )
	{
		var bin = Path.Combine( engineDir, "bin" );
		if ( !Directory.Exists( bin ) )
			return;

		long saved = 0;

		foreach ( var dir in Directory.GetDirectories( bin, "linux*" ) )
		{
			var libraries = Directory.GetFiles( dir )
				.Where( f => Path.GetFileName( f ).Contains( ".so" ) && new FileInfo( f ).LinkTarget is null )
				.GroupBy( f => new FileInfo( f ).Length );

			foreach ( var sameSize in libraries.Where( g => g.Count() > 1 ) )
			{
				var groups = new List<(byte[] Bytes, List<string> Files)>();

				foreach ( var file in sameSize )
				{
					var bytes = File.ReadAllBytes( file );
					var group = groups.FirstOrDefault( g => g.Bytes.AsSpan().SequenceEqual( bytes ) );

					if ( group.Files is null )
						groups.Add( (bytes, [file]) );
					else
						group.Files.Add( file );
				}

				foreach ( var (_, files) in groups.Where( g => g.Files.Count > 1 ) )
				{
					var target = files.OrderByDescending( f => Path.GetFileName( f ).Length ).ThenBy( f => f, StringComparer.Ordinal ).First();

					foreach ( var file in files.Where( f => f != target ) )
					{
						saved += new FileInfo( file ).Length;
						File.Delete( file );
						File.CreateSymbolicLink( file, Path.GetFileName( target ) );
					}
				}
			}
		}

		Log.Info( $"  {saved / (1024.0 * 1024.0):0.0} MB of duplicate libraries linked" );
	}

	//Like ZipFile.CreateFromDirectory, but a symbolic link is stored as one rather than as a copy of its target
	static void Zip( string stageDir, string zipPath )
	{
		using var archive = ZipFile.Open( zipPath, ZipArchiveMode.Create );

		foreach ( var file in Directory.EnumerateFiles( stageDir, "*", SearchOption.AllDirectories ) )
		{
			var name = Path.GetRelativePath( stageDir, file ).Replace( '\\', '/' );
			var link = new FileInfo( file ).LinkTarget;

			if ( link is null )
			{
				archive.CreateEntryFromFile( file, name, CompressionLevel.Optimal );
				continue;
			}

			//S_IFLNK | 0777 in the upper half of the external attributes; the entry's content is the target
			var entry = archive.CreateEntry( name, CompressionLevel.NoCompression );
			entry.ExternalAttributes = unchecked((int)(0xA1FFu << 16));

			using var stream = entry.Open();
			stream.Write( System.Text.Encoding.UTF8.GetBytes( link ) );
		}
	}

	//runtimes/<rid>/native duplicates bin/<platform> and is never loaded; runtimes/<rid>/lib must stay
	static bool IsRestoredNativeFolder( string directory )
	{
		if ( !string.Equals( Path.GetFileName( directory ), "native", StringComparison.OrdinalIgnoreCase ) )
			return false;

		var rid = Path.GetDirectoryName( directory );

		if ( string.IsNullOrEmpty( rid ) )
			return false;

		var runtimes = Path.GetDirectoryName( rid );

		return runtimes is not null
			&& string.Equals( Path.GetFileName( runtimes ), "runtimes", StringComparison.OrdinalIgnoreCase );
	}

	//Skips the .gz/.br copies of web assemblies; nothing in a release reads them
	static bool IsPrecompressedCopy( string file )
	{
		var extension = Path.GetExtension( file );

		if ( !string.Equals( extension, ".gz", StringComparison.OrdinalIgnoreCase )
			&& !string.Equals( extension, ".br", StringComparison.OrdinalIgnoreCase ) )
			return false;

		return File.Exists( Path.Combine( Path.GetDirectoryName( file ) ?? ".",
			Path.GetFileNameWithoutExtension( file ) ) );
	}
}
