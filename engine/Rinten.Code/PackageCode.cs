using Microsoft.CodeAnalysis;
using Rinten.Engine;

namespace Rinten;

//Code a package brought with it rather than assemblies: compiled here, where this process has a compiler - see ProjectCompiling.CompileCodeArchives.
internal static class PackageCode
{
	//Compile group for package code; sets the generator's asset resolver lazily so Roslyn isn't loaded
	internal static CompileGroup NewCompileGroup( string name )
	{
		Rinten.Generator.Processor.DefaultPackageAssetResolver ??= PrimaryAssetOf;

		return new CompileGroup( name );
	}

	//Where a package's primary asset is: what the generator asks when code names a package's asset.
	//The game's package already holds that package's content, so this only says where in it to point.
	private static string PrimaryAssetOf( string packageName )
		=> Package.FetchAsync( packageName, false ).Result?.PrimaryAsset;

	//Fallback for remote packages that ship code archives (.cll) but no precompiled dlls. Compiles the archives locally and mounts the resulting assemblies
	//onto its assembly filesystem, matching how ActivePackage.DownloadBinDllsAsync exposes manifest dlls, so the normal load path picks them up.
	internal static async Task<bool> CompileArchives( PackageManager.ActivePackage package )
	{
		//get all the code archives
		var codeArchives = package.FileSystem.FindFile( "/", "*.cll", true ).ToArray();

		//It's okay for packages not to have code archives, but return as a fail
		if ( codeArchives.Length == 0 )
			return false;

		var analytic = new Api.Events.EventRecord( "package.compile" );
		analytic.SetValue( "package", package.Package.FullIdent );
		analytic.SetValue( "version", package.Package.Revision?.VersionId );
		analytic.SetValue( "archives", codeArchives );

		using var group = NewCompileGroup( package.Package.Ident );
		group.ReferenceProvider = new ReferenceLookup( ReferenceFinder( package ) );

		using ( analytic.ScopeTimer( "LoadArchives" ) )
		{
			foreach ( var file in codeArchives )
			{
				var bytes = await package.FileSystem.ReadAllBytesAsync( file );
				if ( bytes is null || bytes.Length <= 1 )
					throw new System.Exception( "Couldn't load code archive - error opening" );
				//Deserialize to a code archive
				var archive = new CodeArchive( bytes );
				//Create a compiler for it
				var compiler = group.GetOrCreateCompiler( archive.CompilerName );
				compiler.UpdateFromArchive( archive );
				LoadingScreen.Subtitle = System.IO.Path.GetFileName( file );
				await Task.Yield();
			}
		}

		//Compile that bad boy
		using ( analytic.ScopeTimer( "Compile" ) )
		{
			LoadingScreen.Subtitle = null;
			await group.BuildAsync();
			await Task.Yield();
		}

		if ( !group.BuildResult.Success )
		{
			//Add an analytic so we can track these failures on the backend
			var er = new Api.Events.EventRecord( "package.compile.error" );
			er.SetValue( "package", package.Package.FullIdent );
			er.SetValue( "version", package.Package.Revision?.VersionId );
			er.SetValue( "errors", group.BuildResult.BuildDiagnosticsString( Microsoft.CodeAnalysis.DiagnosticSeverity.Error ) );
			er.Submit();

			return false;
		}

		analytic.SetValue( "Diagnostics", group.BuildResult.Diagnostics
											.Where( x => x.Severity > Microsoft.CodeAnalysis.DiagnosticSeverity.Warning )
											.Select( x => new
											{
												x.Severity,
												x.Location?.SourceTree?.FilePath,
												x.Location?.GetLineSpan().StartLinePosition,
												Message = x.GetMessage()
											} )
											.ToArray() );

		//Should be successful
		Assert.True( group.BuildResult.Success );

		using ( analytic.ScopeTimer( "Write" ) )
		{
			var memFs = new MemoryFileSystem();
			//Copy built assemblies flat, like downloaded dlls; ToArray avoids a struct enumerator field
			foreach ( var assembly in group.BuildResult.Output.ToArray() )
			{
				Log.Trace( $"WRITE {assembly.Compiler.AssemblyName}.dll" );
				memFs.WriteAllBytes( $"{assembly.Compiler.AssemblyName}.dll", assembly.AssemblyData );
				LoadingScreen.Subtitle = assembly.Compiler.AssemblyName;
				await Task.Yield();
			}

			package.AssemblyFileSystem ??= new AggregateFileSystem();
			package.AssemblyFileSystem.Mount( memFs );
		}

		LoadingScreen.Subtitle = null;

		analytic.Submit();

		return true;
	}

	//Another active package's assembly, as a reference for package's compiler - a child addon compiling against its parent game.
	internal static Func<string, PortableExecutableReference> ReferenceFinder( PackageManager.ActivePackage package ) => reference => FindReference( package, reference );

	private static PortableExecutableReference FindReference( PackageManager.ActivePackage self, string reference )
	{
		//we can't do anything unless it's in a package
		if ( !reference.StartsWith( "package." ) )
			return default;

		var targetAssemblyName = $"{reference}.dll";
		Log.Trace( $"ActivePackage: Looking for reference: {targetAssemblyName}" );

		//Do any of the active packages have this dll?
		foreach ( var package in PackageManager.ActivePackages )
		{
			if ( package == self )
				continue;

			//TODO - maybe we should filter to make sure the package has the same tag as us?

			var found = package.AssemblyFileSystem.FindFile( "/", targetAssemblyName, true ).FirstOrDefault();
			if ( found == null ) continue;

			var bytes = package.AssemblyFileSystem.ReadAllBytes( found ).ToArray();
			return MetadataReference.CreateFromImage( bytes );
		}

		return default;
	}
}
