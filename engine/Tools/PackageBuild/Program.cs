using Microsoft.CodeAnalysis;
using Rinten;
using System.Text.Json;

namespace Rinten.PackageBuild;

//Headless package compiler: builds .cll archives against this engine build, prints JSON result
static class Program
{
	public static async Task<int> Main( string[] args )
	{
		try
		{
			return await RunAsync( args );
		}
		catch ( Exception e )
		{
			return Fail( e );
		}
	}

	static async Task<int> RunAsync( string[] args )
	{
		var archivePaths = new List<string>();
		var refDirs = new List<string>();
		string outDir = null;

		for ( var i = 0; i < args.Length; i++ )
		{
			switch ( args[i] )
			{
				case "--archive" or "-a" when i + 1 < args.Length:
					archivePaths.Add( args[++i] );
					break;
				case "--out" or "-o" when i + 1 < args.Length:
					outDir = args[++i];
					break;
				case "--refs" or "-r" when i + 1 < args.Length:
					refDirs.Add( args[++i] );
					break;
				default:
					if ( args[i].EndsWith( ".cll", StringComparison.OrdinalIgnoreCase ) )
						archivePaths.Add( args[i] );
					break;
			}
		}

		if ( archivePaths.Count == 0 || string.IsNullOrEmpty( outDir ) )
			return Fail( "Usage: PackageBuild --archive <file.cll> [--archive ...] --out <dir> [--refs <dir> ...]" );

		//Default references to this tool's own directory — in a shipped build that is
		//game/bin/managed, i.e. the exact engine assemblies + toolchain this build ships.
		if ( refDirs.Count == 0 )
			refDirs.Add( AppContext.BaseDirectory );

		var archives = new List<ArchiveEntry>();

		foreach ( var path in archivePaths )
		{
			if ( !File.Exists( path ) )
				return Fail( $"Archive not found: {path}" );

			var archive = new CodeArchive( await File.ReadAllBytesAsync( path ) );

			//package.base was removed in protocol 29, but archives authored before then still declare it.
			archive.References.Remove( "package.base" );

			archives.Add( new ArchiveEntry { Archive = archive } );
		}

		Console.Error.WriteLine( $"Compiling {archives.Count} archive(s) against {refDirs.Count} reference dir(s)..." );

		var result = await CompileArchivesAsync( archives, refDirs );

		if ( !result.Success )
		{
			foreach ( var error in result.Errors )
				Console.Error.WriteLine( $"  {error}" );

			foreach ( var error in result.InternalErrors )
				Console.Error.WriteLine( $"  {error.AssemblyName}: {error.Exception}" );

			WriteResult( new CompileResult { Success = false, Errors = result.Errors, InternalErrors = result.InternalErrors } );
			return 1;
		}

		Directory.CreateDirectory( outDir );
		var fullOut = Path.GetFullPath( outDir );

		var written = new List<CompiledDll>();

		foreach ( var file in result.Files )
		{
			if ( file.AssemblyData is null )
				continue;

			//AssemblyName comes from the archive's CompilerName (untrusted), so make sure it can't escape --out.
			var dllPath = Path.GetFullPath( Path.Combine( outDir, $"{file.AssemblyName}.dll" ) );
			if ( !dllPath.StartsWith( fullOut + Path.DirectorySeparatorChar, StringComparison.Ordinal ) )
				return Fail( $"Refusing to write outside --out: assembly name '{file.AssemblyName}' resolves to '{dllPath}'." );

			await File.WriteAllBytesAsync( dllPath, file.AssemblyData );

			Console.Error.WriteLine( $"  -> {dllPath} ({file.AssemblyData.Length:N0} bytes)" );
			written.Add( new CompiledDll { AssemblyName = file.AssemblyName, File = dllPath } );
		}

		WriteResult( new CompileResult { Success = true, Dlls = written } );
		return 0;
	}

	static int Fail( string error )
	{
		Console.Error.WriteLine( error );
		WriteResult( new CompileResult { Success = false, Errors = [error] } );
		return 1;
	}

	static int Fail( Exception exception )
	{
		Console.Error.WriteLine( exception );
		WriteResult( new CompileResult
		{
			Success = false,
			InternalErrors =
			[
				new InternalBuildError
				{
					Message = exception.Message,
					Exception = exception.ToString()
				}
			]
		} );
		return 1;
	}

	static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	static void WriteResult( CompileResult result )
		=> Console.Out.WriteLine( JsonSerializer.Serialize( result, JsonOpts ) );

	//--- compile core (low-level CompileGroup path — no engine boot) ---

	static async Task<CompilationOutput> CompileArchivesAsync( List<ArchiveEntry> archives, List<string> refDirs )
	{
		var result = new CompilationOutput();

		using var group = new CompileGroup( "package-build" );
		group.ReferenceProvider = new EngineReferenceProvider( refDirs );
		group.PrintErrorsInConsole = false;
		CompileGroup.SuppressBuildNotifications = false;

		foreach ( var entry in archives )
			group.GetOrCreateCompiler( entry.Archive.CompilerName ).UpdateFromArchive( entry.Archive );

		//BuildAsync handles dependency ordering, parallel compilation, Razor, source generators, blacklist walker, Emit.
		if ( !await group.BuildAsync() )
		{
			result.Errors = group.BuildResult.Diagnostics
				.Where( d => d.Severity >= DiagnosticSeverity.Error )
				.Select( FormatDiagnostic )
				.ToList();

			result.InternalErrors = group.Compilers
				.Where( x => x.Output?.Exception is not null )
				.Select( x => new InternalBuildError
				{
					AssemblyName = x.AssemblyName,
					Message = x.Output.Exception.Message,
					Exception = x.Output.Exception.ToString()
				} )
				.ToList();

			if ( result.Errors.Count == 0 && result.InternalErrors.Count == 0 )
				result.InternalErrors.Add( new InternalBuildError
				{
					Message = "Build failed without diagnostics or a captured compiler exception"
				} );

			return result;
		}

		foreach ( var output in group.BuildResult.Output )
		{
			result.Files.Add( new CompiledFile
			{
				AssemblyName = output.Compiler.AssemblyName,
				AssemblyData = output.AssemblyData
			} );
		}

		return result;
	}

	static string FormatDiagnostic( Diagnostic d )
	{
		var location = d.Location?.SourceTree?.FilePath;
		var line = d.Location?.GetLineSpan().StartLinePosition;
		var loc = location is not null ? $" - {location}:{line}" : "";
		return $"{d.GetMessage()}{loc}";
	}
}

//Resolves metadata references from directories of managed DLLs. Enumerates paths up front (cheap) and only builds a MetadataReference for
//names the compiler actually looks up (each tool run is one job, so lazy loading avoids parsing hundreds of unused engine DLLs per compile).
class EngineReferenceProvider : ICompileReferenceProvider
{
	readonly Dictionary<string, string> _paths = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, PortableExecutableReference> _cache = new( StringComparer.OrdinalIgnoreCase );

	public EngineReferenceProvider( IEnumerable<string> dirs )
	{
		foreach ( var dir in dirs )
		{
			if ( !Directory.Exists( dir ) )
				continue;

			//Recurse (bin/managed lays some DLLs out in subfolders); first name wins.
			foreach ( var dll in Directory.GetFiles( dir, "*.dll", SearchOption.AllDirectories ) )
				_paths.TryAdd( Path.GetFileNameWithoutExtension( dll ), dll );
		}
	}

	public PortableExecutableReference Lookup( string reference )
	{
		if ( _cache.TryGetValue( reference, out var cached ) )
			return cached;

		if ( !_paths.TryGetValue( reference, out var path ) )
			return null;

		try { return _cache[reference] = MetadataReference.CreateFromFile( path ); }
		catch { return _cache[reference] = null; } //skip unreadable / native DLLs
	}
}

class ArchiveEntry
{
	public CodeArchive Archive { get; set; }
}

class CompilationOutput
{
	public List<CompiledFile> Files { get; set; } = [];
	public List<string> Errors { get; set; } = [];
	public List<InternalBuildError> InternalErrors { get; set; } = [];
	public bool Success => Errors.Count == 0 && InternalErrors.Count == 0;
}

class CompiledFile
{
	public string AssemblyName { get; set; }
	public byte[] AssemblyData { get; set; }
}

class CompileResult
{
	public bool Success { get; set; }
	public List<CompiledDll> Dlls { get; set; } = [];
	public List<string> Errors { get; set; } = [];
	public List<InternalBuildError> InternalErrors { get; set; } = [];
}

class InternalBuildError
{
	public string AssemblyName { get; set; }
	public string Message { get; set; }
	public string Exception { get; set; }
}

class CompiledDll
{
	public string AssemblyName { get; set; }
	public string File { get; set; }
}
