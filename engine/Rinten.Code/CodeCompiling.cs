using Rinten.Engine;
using Microsoft.CodeAnalysis;
using System.IO;

namespace Rinten;

//Engine-side compiler and the local project compile group; installed by hosts that compile, not web
internal sealed class CodeCompiling : ProjectCompiling
{
	//The compile group every local project's code builds in, made the first time something asks for it - see HasGroup.
	internal static CompileGroup Group => group ??= NewGroup();

	private static CompileGroup group;

	internal override bool HasGroup => group is not null;

	private static CompileGroup NewGroup()
	{
		var made = new CompileGroup( "local" );
		made.PrintErrorsInConsole = !Application.IsEditor;
		made.OnCompileStarted = () => IToolsDll.Current?.RunEvent( "compile.started", Group );
		made.OnCompileFinished = () => IToolsDll.Current?.RunEvent( "compile.complete", Group );
		made.OnCompileSuccess = OnCompileSuccess;
		return made;
	}

	internal override ProjectCode For( Project project ) => new ProjectCompilers( project );

	internal override void DropGroup()
	{
		group?.Dispose();
		group = null;
	}

	internal override void BuildIfNeeded()
	{
		if ( group is null || !group.NeedsBuild )
			return;

		if ( group.IsBuilding )
			return;

		group.AllowFastHotload = HotloadManager.hotload_fast;

		_ = group.BuildAsync();
	}

	internal override async Task<bool> CompileAsync()
	{
		while ( Group.IsBuilding )
		{
			await Task.Delay( 50 );
		}

		Group.AllowFastHotload = HotloadManager.hotload_fast;

		return await Group.BuildAsync();
	}

	//What the last compile said. Nothing where nothing has compiled - asking does not make a compile group to ask.
	internal static IEnumerable<Diagnostic> Diagnostics()
		=> group?.BuildResult.Diagnostics ?? (IEnumerable<Diagnostic>)Array.Empty<Diagnostic>();

	internal override string ErrorsText() => string.Join( "\n", Diagnostics()
		.Where( x => x.Severity > DiagnosticSeverity.Warning )
		.Select( x => $"{x.Severity} | {x.GetMessage()} - {x.Location?.SourceTree?.FilePath}:{x.Location?.GetLineSpan().StartLinePosition}" ) );

	//Resolve an assembly to a compiler using the assembly name
	internal static Compiler ResolveCompiler( System.Reflection.Assembly assembly )
		=> Group.FindCompilerByAssemblyName( assembly.GetName().Name );

	internal override Task<bool> CompileCodeArchives( PackageManager.ActivePackage package ) => PackageCode.CompileArchives( package );

	internal override IServerCode NewServerCode() => new ServerCode();

	internal override byte[] WithoutServerCode( string assemblyName ) => ServerCode.WithoutServerCode( assemblyName );

	internal override IFastHotload NewFastHotload( string name ) => new ILHotload( name );

	private static void OnCompileSuccess()
	{
		var grouped = Group.BuildResult.Output.GroupBy( x => FindByCompiler( x.Compiler ) );

		foreach ( var grouping in grouped )
		{
			var project = grouping.Key;
			if ( project is null ) continue;

			foreach ( var assembly in grouping )
			{
				if ( !assembly.Successful )
				{
					continue;
				}

				var cll = assembly.Archive.Serialize();

				project.AssemblyFileSystem.CreateDirectory( "/.bin" );
				project.AssemblyFileSystem.WriteAllBytes( $"/.bin/{assembly.Compiler.AssemblyName}.dll", assembly.AssemblyData );
				project.AssemblyFileSystem.WriteAllBytes( $"/.bin/{assembly.Compiler.AssemblyName}.cll", cll );

				if ( project.IsBuiltIn && Application.IsEditor )
				{
					try
					{
						string binPath = Path.Combine( project.GetRootPath(), Project.CacheFolder, "bin" );
						Directory.CreateDirectory( binPath );

						File.WriteAllBytes( Path.Combine( binPath, $"{assembly.Compiler.AssemblyName}.dll" ), assembly.AssemblyData );
						File.WriteAllBytes( Path.Combine( binPath, $"{assembly.Compiler.AssemblyName}.cll" ), cll );
					}
					catch ( Exception ex )
					{
						Log.Warning( $"Failed to write cached assembly for project {project.Config.FullIdent}: {ex.Message}" );
					}
				}
			}
		}
	}

	//Find a project from a compiler
	private static Project FindByCompiler( Compiler compiler )
	{
		return Project.All.FirstOrDefault( x => ProjectCompilers.Of( x ) is { } compilers && ( compilers.Game == compiler || compilers.Editor == compiler ) );
	}
}

/// <summary>What a process that compiles calls as it starts - see <see cref="CodeCompiling"/>.</summary>
public static class RintenCode
{
	/// <summary>Makes this assembly the engine's compiler, where nothing has yet.</summary>
	public static void Install()
	{
		ProjectCompiling.Current ??= new CodeCompiling();
	}

	/// <summary>(Re)generate the active project's solution file - see <see cref="ProjectSolution"/>.</summary>
	public static Task GenerateSolution() => ProjectSolution.GenerateSolution();
}
