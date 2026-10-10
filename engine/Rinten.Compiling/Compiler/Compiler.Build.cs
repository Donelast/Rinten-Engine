using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Collections.Immutable;
using System.Threading;

namespace Rinten;

partial class Compiler
{
	CodeArchive _currentArchive;

	//Task completed at the end of BuildAsync, for other compilers to await if they reference this one.
	private TaskCompletionSource<CompilerOutput> _compileTcs;

	/// <summary>Fill this compiler from a code archive</summary>
	public void UpdateFromArchive( CodeArchive a )
	{
		_currentArchive = a;

		CopyReferencesFromArchive( a );
		MarkForRecompile();
	}

	//Fill CompilerOutput from a precompiled assembly, as if it had been built by this compiler.
	internal void UpdateFromAssembly( byte[] bytes )
	{
		using ( var a_stream = new System.IO.MemoryStream( bytes ) )
		{
			MetadataReference = Microsoft.CodeAnalysis.MetadataReference.CreateFromStream( a_stream );
		}

		var version = Interlocked.Increment( ref compileCounter );
		Output = new CompilerOutput( this )
		{
			Successful = true,
			Version = Version.Parse( $"0.0.{version}.0" ),
			MetadataReference = MetadataReference
		};
	}

	//Called by CompileGroup before a build starts. Prepares this compiler to be referenced by other compilers before they build with BuildAsync.
	internal void PreBuild()
	{
		Assert.False( IsBuilding, "This compiler is already building" );

		//We set up the TCS here so other compilers can use it when they BuildReferencesAsync(),
		//avoiding a race condition if we set it up at the start of BuildAsync()

		_compileTcs = new TaskCompletionSource<CompilerOutput>();
	}

	//Build and load the assembly.
	internal async Task BuildAsync()
	{
		Assert.True( IsBuilding, $"{nameof( PreBuild )} must be called first" );

		log.Trace( "Build Start" );

		var output = new CompilerOutput( this );

		Interlocked.Increment( ref compileCounter );

		output.Version = Version.Parse( $"0.0.{compileCounter}.0" );

		try
		{
			//Do the expensive archive building on a worker thread

			var archive = await Task.Run( () => BuildArchive( output ) );

			//Build a list of references, waiting for other compilers to finish if needed

			var refs = await BuildReferencesAsync( archive );

			//Actually compile, again on a worker thread since it's expensive

			await Task.Run( () => BuildInternal( refs, output ) );
		}
		catch ( System.Exception e )
		{
			output.Exception = e;
			log.Warning( e, e.Message );
		}
		finally
		{
			Output = output;

			_compileTcs.SetResult( output );

			log.Trace( "Build Finished" );
		}
	}

	void CopyReferencesFromArchive( CodeArchive a )
	{
		_references.Clear();

		foreach ( var reference in a.References )
		{
			_references.Add( reference );
		}
	}

	CodeArchive BuildArchive( CompilerOutput output )
	{
		if ( _currentArchive is not null )
		{
			output.Archive = _currentArchive;
			return _currentArchive;
		}

		var archive = new CodeArchive();
		archive.CompilerName = Name;
		archive.Configuration = _config;
		output.Archive = archive;

		var parseOptions = _config.GetParseOptions();

		//References
		foreach ( var e in _references )
		{
			archive.References.Add( e );
		}

		//Syntax trees
		GetSyntaxTree( archive, parseOptions );

		if ( GetGeneratedCode( output.Version, parseOptions ) is SyntaxTree generated )
		{
			archive.SyntaxTrees.Add( generated );
		}

		archive.SyntaxTrees.Sort( ( a, b ) => string.CompareOrdinal( a.FilePath, b.FilePath ) );

		return archive;
	}

	void BuildInternal( IReadOnlyList<PortableExecutableReference> refs, CompilerOutput output )
	{
		var archive = output.Archive;
		var releaseMode = archive.Configuration.ReleaseMode == ReleaseMode.Release;
		var conf = archive.Configuration;

		var options = incrementalState.Compilation?.Options;
		options ??= new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary )
							.WithConcurrentBuild( true )
							.WithGeneralDiagnosticOption( ReportDiagnostic.Info )
							.WithPlatform( Microsoft.CodeAnalysis.Platform.AnyCpu );

		options = options
					.WithDeterministic( releaseMode ? true : false )
					.WithOptimizationLevel( releaseMode ? OptimizationLevel.Release : OptimizationLevel.Debug )
					.WithGeneralDiagnosticOption( conf.TreatWarningsAsErrors ? ReportDiagnostic.Error : ReportDiagnostic.Default )
					.WithSpecificDiagnosticOptions( conf.GetReportDiagnostics() )
					.WithNullableContextOptions( conf.Nullables ? NullableContextOptions.Enable : NullableContextOptions.Disable )
					.WithAllowUnsafe( conf.Unsafe );

		CSharpCompilation compiler;

		List<SyntaxTree> inputSyntaxTrees =
		[
			.. archive.SyntaxTrees, //from source files
			.. ProcessRazorFiles(archive, output) //processed razor files
		];

		List<SyntaxTree> modifiedSyntaxTrees;
		if ( incrementalState.HasState )
		{
			compiler = incrementalState.Compilation
							.WithAssemblyName( AssemblyName )
							.WithOptions( options );

			var oldRefs = compiler.References.ToHashSet();

			if ( !oldRefs.SetEquals( refs ) )
			{
				compiler = compiler.WithReferences( refs );
			}

			compiler = ReplaceSyntaxTrees( compiler, inputSyntaxTrees, out modifiedSyntaxTrees );
		}
		else
		{
			compiler = CSharpCompilation.Create( AssemblyName, inputSyntaxTrees, refs, options );
			modifiedSyntaxTrees = compiler.SyntaxTrees.ToList();
		}

		bool ilHotloadSupported;

		{
			var processor = RunGenerators( compiler, modifiedSyntaxTrees, output );

			compiler = processor.Compilation;

			ilHotloadSupported = processor.ILHotloadSupported;

			//If you have any errors in codegen don't bother compiling, developer should sort it out
			if ( processor.Diagnostics.Any( x => x.Severity == DiagnosticSeverity.Error ) )
			{
				return;
			}
		}

		using ( var xmlStream = new System.IO.MemoryStream() )
		using ( var peStream = new System.IO.MemoryStream() )
		{
			var emitOptions = new EmitOptions()
				.WithDebugInformationFormat( DebugInformationFormat.Embedded );

			BuildResult = compiler.Emit( peStream: peStream, xmlDocumentationStream: xmlStream, options: emitOptions );

			if ( BuildResult.Success )
			{
				output.Successful = true;

				peStream.Seek( 0, System.IO.SeekOrigin.Begin );
			}

			output.AssemblyData = peStream.ToArray();
			output.XmlDocumentation = System.Text.Encoding.UTF8.GetString( xmlStream.ToArray() );
		}

		output.Diagnostics.AddRange( BuildResult.Diagnostics );

		if ( !BuildResult.Success )
		{
			return;
		}

		incrementalState.Update( archive, inputSyntaxTrees, compiler );

		using ( var a_stream = new System.IO.MemoryStream( output.AssemblyData ) )
		{
			MetadataReference = output.MetadataReference = Microsoft.CodeAnalysis.MetadataReference.CreateFromStream( a_stream );

			if ( MetadataReference == null )
				throw new System.Exception( "metaRef is null!" );
		}

		if ( !ilHotloadSupported )
		{
			_recentMetadataReferences.Clear();
		}

		_recentMetadataReferences.Add( output.Version, MetadataReference );

		TrimRecentMetadataReferences();
	}
}
