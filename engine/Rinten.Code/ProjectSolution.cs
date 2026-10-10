using Rinten.SolutionGenerator;
using System.IO;
using System.Text.RegularExpressions;

namespace Rinten;

//The solution a project's code opens in: a C# project for its game code, its server
//code, its editor code and its unit tests. The editor's alone - see GenerateSolution.
internal sealed class ProjectSolution
{
	private readonly Project owner;

	//(Re)generate the active project's solution file.
	internal static async Task GenerateSolution()
	{
		var solutionName = $"rinten.slnx";
		var solutionFolder = EngineFileSystem.Root.GetFullPath( "/" );

		if ( Project.Current is not null )
		{
			solutionName = $"{Project.Current.Config.Ident}.slnx";
			solutionFolder = Project.Current.GetRootPath();
		}

		try
		{
			var generator = new Rinten.SolutionGenerator.Generator();

			foreach ( var project in Project.All.ToArray() )
			{
				if ( !project.Active ) continue;

				//Don't put menu project in everyone's slns
				if ( project.Config.Ident == "menu" && Project.Current?.Config?.Ident != "menu" ) continue;

				await new ProjectSolution( project ).GenerateProject( generator );
			}

			generator.Run( EngineExecutables.EditorFile, "bin/managed", solutionName, EngineFileSystem.Root.GetFullPath( "/" ), solutionFolder );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception when generating {solutionName} ({e.Message})" );
		}

		if ( Project.Current is not null )
		{
			Project.WriteVsCodeWorkspace( Project.Current );
		}
	}

	private ProjectSolution( Project owner )
	{
		this.owner = owner;
	}

	bool AddonTypeUsesCode()
	{
		if ( owner.Config.Type == "game" ) return true;
		if ( owner.Config.Type == "tool" ) return true;
		if ( owner.Config.Type == "library" ) return true;
		if ( owner.Config.Type == "addon" ) return true;

		return false;
	}

	private async Task GenerateProject( Rinten.SolutionGenerator.Generator generator )
	{
		//justify the async
		await Task.Yield();

		if ( !AddonTypeUsesCode() )
			return;

		if ( owner.Config.Directory is null )
			return;

		var projectName = owner.Config.GetMetaOrDefault( "CsProjName", owner.Config.Ident );
		if ( !Regex.IsMatch( projectName, @"^[a-zA-Z\.\ \-_0-9]{1,32}$" ) )
		{
			if ( !string.IsNullOrWhiteSpace( projectName ) )
			{
				Log.Warning( $"Project name '{projectName}' is invalid - reverting to ident" );
			}

			projectName = owner.Config.Ident;
		}

		string projectFolder = owner.Config.Type == "library" ? "Project.Libraries" : $"{owner.Config.Type.ToTitleCase()}s";
		if ( projectFolder == "Games" ) projectFolder = null;

		ProjectInfo project = null;

		if ( owner.HasCodePath() )
		{
			//Code project
			var compilerSettings = owner.Config.GetCompileSettings();

			if ( owner.Config.Type == "game" )
			{
				compilerSettings.IgnoreFolders.Add( "editor" );
				compilerSettings.IgnoreFolders.Add( "unittest" );
			}

			project = generator.AddProject( owner.Config.Type, owner.Config.FullIdent, projectName, owner.GetCodePath(), compilerSettings );
			project.Folder = projectFolder;
			project.RintenProjectFilePath = owner.ConfigFilePath;

			//Add each reference to the project
			foreach ( var reference in compilerSettings.DistinctAssemblyReferences )
			{
				project.References.Add( $"{reference}.dll" );
			}

			//Server projects
			if ( owner.Config.Type == "game" || owner.Config.Type == "library" )
			{
				var serverProject = AddServerProjectFrom( projectName, generator );
				if ( serverProject is not null )
				{
					serverProject.Folder = projectFolder;

					//Hide server files from the main project
					project.IgnoreFiles.Add( "**/*.Server.cs" );
				}
			}

			if ( owner.Config.Type == "tool" )
			{
				project.AddToolAssemblyReferences();
				project.AddToolsNamespaceGlobalStatic();
				project.AddGameNamespaceGlobalStatic();

				if ( owner.Config.Ident != "toolbase" )
					project.PackageReferences.Add( "local.toolbase" );
			}
			else if ( owner.Config.Type == "game" || owner.Config.Type == "library" || owner.Config.Type == "addon" )
			{
				project.AddAspComponentsGlobalUsing();
				project.AddGameNamespaceGlobalStatic();

				if ( owner.Config.FullIdent != "local.base" && !project.PackageReferences.Contains( "local.base" ) )
				{
					project.PackageReferences.Add( "local.base" );
				}
			}

			if ( (owner.Config.Type == "game" || owner.Config.Type == "addon") && !owner.IsBuiltIn )
			{
				AddLibrariesToProject( project );
			}

			if ( owner.Config.Type == "addon" )
			{
				AddParentPackageReferenceToProject( project );
			}
		}

		if ( owner.Config.Type == "game" || owner.Config.Type == "library" || owner.Config.Type == "addon" )
		{
			//Editor project
			var editorProject = AddEditorProjectFrom( projectName, generator );
			if ( editorProject is not null )
			{
				editorProject.Folder = projectFolder;
				editorProject.RintenProjectFilePath = owner.ConfigFilePath;

				if ( project is not null )
					editorProject.PackageReferences.Add( project.Name );
			}

			//Unit test project
			var testProject = AddUnitTestProjectFrom( projectName, generator );
			if ( testProject is not null )
			{
				testProject.Folder = projectFolder;

				if ( project is not null )
					testProject.PackageReferences.Add( project.Name );

				if ( editorProject is not null )
					testProject.PackageReferences.Add( editorProject.Name );
			}
		}
	}

	private void AddLibrariesToProject( ProjectInfo project )
	{
		foreach ( var library in Project.Libraries.Where( x => x.HasCodePath() ) )
		{
			project.PackageReferences.Add( library.Package.GetIdent( false, false ) );
		}
	}

	private bool HasServersideCode()
	{
		if ( !owner.HasCodePath() )
			return false;

		var codePath = owner.GetCodePath().Replace( '\\', '/' );

		//Check for any .Server.cs files recursively
		try
		{
			return Directory.EnumerateFiles( codePath, "*.Server.cs", SearchOption.AllDirectories ).Any();
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to check for server files in {codePath}: {e.Message}" );
			return false;
		}
	}

	ProjectInfo AddServerProjectFrom( string projectName, Rinten.SolutionGenerator.Generator generator )
	{
		if ( !owner.HasCodePath() )
			return default;

		if ( !HasServersideCode() )
			return default;

		var serverSettings = owner.Config.GetCompileSettings();
		serverSettings.DefineConstants += ";SERVER";

		var project = generator.AddProject( owner.Config.Type,
			$"{owner.Config.FullIdent}.server",
			$"{projectName}.server",
			owner.GetCodePath(),
			serverSettings );

		foreach ( var reference in serverSettings.DistinctAssemblyReferences )
		{
			project.References.Add( $"{reference}.dll" );
		}

		//Add standard references
		if ( owner.Config.Type == "game" || owner.Config.Type == "library" )
		{
			project.AddAspComponentsGlobalUsing();
			project.AddGameNamespaceGlobalStatic();

			if ( !project.PackageReferences.Contains( "local.base" ) )
			{
				project.PackageReferences.Add( "local.base" );
			}
		}

		if ( owner.Config.Type == "game" )
		{
			AddLibrariesToProject( project );
		}

		return project;
	}

	//Writes the parent package's assembly to .rinten/bin and adds a project reference for Intellisense.
	void AddParentPackageReferenceToProject( ProjectInfo project )
	{
		var parentPackage = owner.Config.GetMetaOrDefault<string>( "ParentPackage", null );

		if ( string.IsNullOrWhiteSpace( parentPackage ) )
			return;

		if ( !Package.TryParseIdent( parentPackage, out var parentParts ) )
			return;

		var parentAp = PackageManager.Find( parentPackage, true, false );
		if ( parentAp?.AssemblyFileSystem is null )
			return;

		var dllName = $"package.{parentParts.org}.{parentParts.package}.dll";
		var found = parentAp.AssemblyFileSystem.FindFile( "/", dllName, true ).FirstOrDefault();
		if ( found is null )
			return;

		var bytes = parentAp.AssemblyFileSystem.ReadAllBytes( found ).ToArray();
		var binDir = System.IO.Path.Combine( owner.GetRootPath(), Project.CacheFolder, "bin" );
		System.IO.Directory.CreateDirectory( binDir );
		System.IO.File.WriteAllBytes( System.IO.Path.Combine( binDir, dllName ), bytes );

		project.PackageReferences.Add( Path.Combine( binDir, dllName ) );
	}

	ProjectInfo AddEditorProjectFrom( string projectName, Rinten.SolutionGenerator.Generator generator )
	{
		if ( !owner.HasEditorPath() )
			return default;

		var compilerSettings = owner.Config.GetCompileSettings();
		var project = generator.AddProject( "tool", $"{owner.Config.FullIdent}.editor", $"{projectName}.editor", owner.GetEditorPath(), compilerSettings );
		project.IsEditorProject = true;

		//Add each reference to the project
		foreach ( var reference in compilerSettings.DistinctAssemblyReferences )
		{
			project.References.Add( $"{reference}.dll" );
		}

		if ( (owner.Config.Type == "game" || owner.Config.Type == "addon") && !owner.IsBuiltIn )
		{
			//editor libraries
			foreach ( var library in Project.Libraries.Where( x => x.HasEditorPath() ) )
			{
				project.PackageReferences.Add( $"{library.Package.GetIdent( false, false )}.editor" );
			}
		}

		project.AddToolAssemblyReferences();
		project.AddToolPackageReferences();
		project.AddToolsNamespaceGlobalStatic();
		project.AddGameNamespaceGlobalStatic();

		return project;
	}

	ProjectInfo AddUnitTestProjectFrom( string projectName, Rinten.SolutionGenerator.Generator generator )
	{
		var dirinfo = new DirectoryInfo( System.IO.Path.Combine( owner.GetRootPath(), "UnitTests" ) );
		if ( !dirinfo.Exists )
			return default;

		var compilerSettings = owner.Config.GetCompileSettings();
		var project = generator.AddProject( "unittest", $"{owner.Config.FullIdent}.unittest", $"{projectName}.unittest", dirinfo.FullName, compilerSettings );
		project.IsUnitTestProject = true;

		//Add each reference to the project
		foreach ( var reference in compilerSettings.DistinctAssemblyReferences )
		{
			project.References.Add( $"{reference}.dll" );
		}

		project.AddToolAssemblyReferences();
		project.AddToolPackageReferences();
		project.AddToolsNamespaceGlobalStatic();
		project.AddGameNamespaceGlobalStatic();

		return project;
	}
}

file static class ProjectExtensions
{
	extension( ProjectInfo project )
	{
		/// <summary>Add references to non-package assemblies needed by tools / editor packages.</summary>
		public void AddToolAssemblyReferences()
		{
			project.References.Add( "Rinten.AppSystem.dll" );
			project.References.Add( "Rinten.Tools.dll" );
			project.References.Add( "Rinten.Compiling.dll" );
			project.References.Add( "Microsoft.CodeAnalysis.dll" );
			project.References.Add( "Microsoft.CodeAnalysis.CSharp.dll" );
			project.References.Add( "Rinten.Bind.dll" );
			project.References.Add( "Facepunch.ActionGraphs.dll" );
			project.References.Add( "SkiaSharp.dll" );
		}

		/// <summary>Add references to package assemblies needed by editor packages. Includes <c>toolbase</c> and tools like <c>actiongraph</c>.</summary>
		public void AddToolPackageReferences()
		{
			project.PackageReferences.Add( "local.toolbase" );
			project.PackageReferences.Add( "actiongraph" );
			project.PackageReferences.Add( "moviemaker" );
		}

		/// <summary>Add <c>Microsoft.AspNetCore.Components</c> and <c>Microsoft.AspNetCore.Components.Rendering</c> as global using statements.</summary>
		public void AddAspComponentsGlobalUsing()
		{
			project.GlobalUsing.Add( "Microsoft.AspNetCore.Components" );
			project.GlobalUsing.Add( "Microsoft.AspNetCore.Components.Rendering" );
		}

		/// <summary>Add <c>Rinten.Internal.GlobalToolsNamespace</c> as a global static statement.</summary>
		public void AddToolsNamespaceGlobalStatic()
		{
			project.GlobalStatic.Add( "Rinten.Internal.GlobalToolsNamespace" );
		}

		/// <summary>Add <c>Rinten.Internal.GlobalGameNamespace</c> as a global static statement.</summary>
		public void AddGameNamespaceGlobalStatic()
		{
			project.GlobalStatic.Add( "Rinten.Internal.GlobalGameNamespace" );
		}
	}
}
