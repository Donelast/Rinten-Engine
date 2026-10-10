using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace Rinten;

/// <summary>Given a folder of .cs files, this will produce (and load) an assembly</summary>
[SkipHotload]
public sealed partial class Compiler : IDisposable
{
	private Logger log { get; set; }

	/// <summary>Each compiler must belong to a compile group</summary>
	public CompileGroup Group { get; private set; }

	/// <summary>The output from the previous build</summary>
	public CompilerOutput Output { get; set; }

	/// <summary>Is this compiler currently building?</summary>
	public bool IsBuilding => _compileTcs?.Task is { IsCompleted: false };

	/// <summary>Returns true if this compiler is pending a build, or currently building.</summary>
	public bool NeedsBuild => IsBuilding || Group.CompilerNeedsBuild( this );

	/// <summary>Name of the project this compiler was created for. This could be something like "base" or "org.ident".</summary>
	public string Name { get; }

	/// <summary>During development we use absolute source paths so that debugging works better. In a packed/release build it's good to use relative paths instead, just to avoid exposing the builder's file system.</summary>
	public bool UseAbsoluteSourcePaths { get; set; } = true;

	/// <summary>A list of warnings and errors created by the last build</summary>
	public Microsoft.CodeAnalysis.Diagnostic[] Diagnostics => Output?.Diagnostics.ToArray() ?? Array.Empty<Diagnostic>();

	/// <summary>Generated assembly name, without an extension. This will be "package.{Name}".</summary>
	public string AssemblyName { get; }

	/// <summary>Global namespaces</summary>
	public StringBuilder GeneratedCode { get; set; } = new();

	//Directories to search for code
	internal List<BaseFileSystem> SourceLocations { get; } = new();

	/// <summary>An aggregate of all the filesystem this compiler has</summary>
	public BaseFileSystem FileSystem { get; } = new AggregateFileSystem();

	//After compile is completed successfully this will be non null.
	internal PortableExecutableReference MetadataReference;

	//Recent metadata refs per version for fast-hotload revocation; bounded since each is a PE image
	private readonly Dictionary<Version, PortableExecutableReference> _recentMetadataReferences = new();

	//How many built versions to keep the metadata of. Only the versions still waiting to be fast hotloaded
	//can ever be asked for, and that is the one or two most recent; the rest is a held image nobody will read.
	private const int RecentMetadataReferenceLimit = 4;

	//Drop the oldest images once there are more than we can be asked for.
	private void TrimRecentMetadataReferences()
	{
		while ( _recentMetadataReferences.Count > RecentMetadataReferenceLimit )
		{
			var oldest = _recentMetadataReferences.Keys.Min();

			//Never drop the one other compilers are currently referencing.
			if ( _recentMetadataReferences[oldest] == MetadataReference ) break;

			_recentMetadataReferences.Remove( oldest );
		}
	}

	internal IncrementalCompileState incrementalState = new IncrementalCompileState();

	//The compiler's settings.
	private Compiler.Configuration _config = new();

	//Should only ever get called from CompileGroup.
	internal Compiler( CompileGroup group, string name, string fullPath, Compiler.Configuration settings )
	{
		Group = group;
		Name = name;
		log = new Logger( $"Compiler/{name}" );
		AssemblyName = $"package.{name}";

		if ( fullPath is not null )
		{
			AddSourcePath( fullPath );
		}

		SetConfiguration( settings );
	}

	//Should only ever get called from CompileGroup.
	internal Compiler( CompileGroup group, string name )
	{
		Group = group;
		Name = name;
		log = new Logger( $"Compiler/{name}" );
		AssemblyName = $"package.{name}";
	}

	/// <summary>Add an extra source path. Useful for situations where you want to combine multiple addons into one.</summary>
	public void AddSourcePath( string fullPath )
	{
		AddSourceLocation( new LocalFileSystem( fullPath ) );
	}

	internal void AddSourceLocation( BaseFileSystem fileSystem )
	{
		fileSystem.TraceChanges = true;

		SourceLocations.Add( fileSystem );
		FileSystem.Mount( fileSystem );
	}

	public void SetConfiguration( Compiler.Configuration newConfig )
	{
		_config = newConfig;
		incrementalState.Reset();
	}

	public Configuration GetConfiguration()
	{
		return _config;
	}

	/// <summary>Results for the assembly build. This can contain warnings or errors.</summary>
	public EmitResult BuildResult { get; private set; }

	/// <summary>Accesses Output.Successful</summary>
	public bool BuildSuccess => Output?.Successful ?? false;

	//Keep tabs of how many times we've compiled
	static int compileCounter = 100;

	public void NotifyFastHotload( Version fastHotloadedVersion )
	{
		log.Trace( $"{Name}@{fastHotloadedVersion} was fast hotloaded" );

		if ( !_recentMetadataReferences.Remove( fastHotloadedVersion, out var reference ) )
		{
			log.Trace( $"  Not found!!" );
			return;
		}

		//MetadataReference shouldn't be a fast hotloaded version, otherwise other compilers that reference this compiler can't be loaded properly!

		if ( reference == MetadataReference )
		{
			var mostRecent = _recentMetadataReferences
				.MaxBy( x => x.Key );

			MetadataReference = mostRecent.Value;

			log.Trace( $"  Now using {Name}@{mostRecent.Key}" );
		}
	}

	//Read text from a file while dealing with the fact that it might be being saved right when we're loading it so it's likely to throw IOExceptions.
	private string ReadTextForgiving( string file, int retryCount = 10, int millisecondsBetweenChanges = 5 )
	{
		for ( var i = 0; i < retryCount; i++ )
		{
			try
			{
				return System.IO.File.ReadAllText( file );
			}
			catch ( System.IO.IOException )
			{
				System.Threading.Thread.Sleep( millisecondsBetweenChanges );
			}
		}

		return null;
	}

	internal async Task<IReadOnlyList<PortableExecutableReference>> BuildReferencesAsync( CodeArchive archive )
	{
		var output = new List<PortableExecutableReference>( FrameworkReferences.All.Values );
		var foundHash = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var name in archive.References )
		{
			//We already got it from a package reference this is cool for when referencing something that includes package.base.dll
			if ( foundHash.Contains( name ) )
				continue;

			//FindReferenceAsync throws if not found

			if ( await Group.FindReferenceAsync( name, this ) is { } mr )
			{
				log.Trace( $"Found reference: {name}" );
				output.Add( mr );
			}
		}

		return output;
	}

	//Fetch the build result, awaitable if we're still building.
	internal Task<CompilerOutput> GetCompileOutputAsync()
	{
		if ( _compileTcs is null )
		{
			return Task.FromResult( Output );
		}

		return _compileTcs.Task;
	}

	//Return this compiler and all child compilers
	internal IEnumerable<Compiler> GetReferencedCompilers()
	{
		var referenced = new HashSet<Compiler>();
		var queue = new Queue<Compiler>();

		referenced.Add( this );
		queue.Enqueue( this );

		while ( queue.TryDequeue( out var next ) )
		{
			foreach ( var reference in next._references )
			{
				if ( Group.FindCompilerByAssemblyName( reference ) is not { } otherCompiler ) continue;
				if ( !referenced.Add( otherCompiler ) ) continue;

				queue.Enqueue( otherCompiler );
			}
		}

		return referenced;
	}

	~Compiler()
	{
		Dispose( false );
	}

	public void Dispose()
	{
		Dispose( true );
		GC.SuppressFinalize( this );
	}

	private void Dispose( bool disposing )
	{
		if ( disposing )
		{
			Group?.OnCompilerDisposed( this );
			Group = null;
		}

		foreach ( var watcher in sourceWatchers ) watcher.Dispose();
		sourceWatchers.Clear();

		FileSystem?.Dispose();

		foreach ( var fs in SourceLocations ) fs.Dispose();

		SourceLocations.Clear();
	}

	public int DependencyIndex( int depth = 0 )
	{
		int index = 0;
		depth++;

		if ( depth > 10 )
			throw new System.Exception( "Cyclic references detected - aborting." );

		foreach ( var r in _references )
		{
			var g = Group.FindCompilerByAssemblyName( r );
			if ( g == null ) continue; //this is allowed - it might be Rinten.Game or something
			if ( g == this ) continue;

			index = Math.Max( index, g.DependencyIndex( depth ) );
		}

		return index + 1;
	}

	/// <summary>Recompile this as soon as is appropriate</summary>
	public void MarkForRecompile()
	{
		Group.MarkForRecompile( this );
	}
}
