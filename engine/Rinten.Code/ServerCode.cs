using Microsoft.CodeAnalysis.CSharp;
using System.IO;

namespace Rinten;

//Code a joined server sends, compiled here; separate type so web builds don't pull in Roslyn
internal sealed class ServerCode : IServerCode
{
	private readonly CompileGroup group = PackageCode.NewCompileGroup( "server" );

	/// <summary>Whether an archive has changed since the last build.</summary>
	public bool NeedsBuild => group.NeedsBuild;

	/// <summary>Takes in one archive, as it came over the network, to be built with the rest.</summary>
	public void Update( byte[] bytes )
	{
		var archive = new CodeArchive( bytes );
		var compiler = group.GetOrCreateCompiler( archive.CompilerName );
		compiler.UpdateFromArchive( archive );
	}

	//Synchronously, because network state made with the new assemblies may arrive next, and a client without them could not read it.
	/// <summary>Builds what changed and loads the assemblies it made into <paramref name="enroller"/>. False when it did not build; the reason is in the log.</summary>
	public bool BuildAndLoad( PackageLoader.Enroller enroller )
	{
		SyncContext.RunBlocking( group.BuildAsync() );
		if ( !group.BuildResult.Success )
			return false;

		foreach ( var assembly in group.BuildResult.Output )
		{
			using var stream = new MemoryStream( assembly.AssemblyData );
			enroller.LoadAssemblyFromStream( assembly.Compiler.AssemblyName, stream );
		}

		return true;
	}

	//A game's code archive with everything behind SERVER taken out, for the clients of a dedicated
	//server - or null where no compiler here built it. See GameInstanceDll.AddArchiveToCodeArchiveTable.
	internal static byte[] WithoutServerCode( string assemblyName )
	{
		//Find matching compiler
		var compiler = Project.All
			.Select( x => ProjectCompilers.Of( x )?.Game )
			.FirstOrDefault( x => x is not null && x.AssemblyName.Equals( assemblyName ) );

		if ( compiler?.Output?.Archive is null )
			return null;

		var config = compiler.GetConfiguration();

		//Strip the SERVER define from the archive's DefineConstants
		var parts = config.GetPreprocessorSymbols();
		parts.RemoveWhere( x => x.Equals( "SERVER", StringComparison.OrdinalIgnoreCase ) );

		var newConfig = config with { DefineConstants = string.Join( ";", parts ) };

		var strippedArchive = new CodeArchive
		{
			CompilerName = compiler.Name,
			Configuration = newConfig
		};

		foreach ( var tree in compiler.Output.Archive.SyntaxTrees )
		{
			var text = tree.GetText().ToString();
			var newTree = CSharpSyntaxTree.ParseText( text, path: tree.FilePath, encoding: System.Text.Encoding.UTF8, options: newConfig.GetParseOptions() );

			//Make sure we strip any disabled text trivia from the tree
			newTree = Compiler.StripDisabledTextTrivia( newTree );
			strippedArchive.SyntaxTrees.Add( newTree );
		}

		//Need to maintain all the extra stuff in the original archive
		foreach ( var f in compiler.Output.Archive.AdditionalFiles )
		{
			strippedArchive.AdditionalFiles.Add( f );
		}

		foreach ( var kv in compiler.Output.Archive.FileMap )
		{
			strippedArchive.FileMap[kv.Key] = kv.Value;
		}

		foreach ( var r in compiler.Output.Archive.References )
		{
			strippedArchive.References.Add( r );
		}

		return strippedArchive.Serialize();
	}
}
