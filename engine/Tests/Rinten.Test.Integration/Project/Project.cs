using Rinten.Diagnostics;
using System;

namespace ProjectTests;

[TestClass]
public class ProjectTest
{
	[TestInitialize]
	public void TestInitialize()
	{
		Logging.Enabled = true;
		Project.Clear();
		var dir = $"{Environment.CurrentDirectory}/.rinten/test_download_cache/project";
		AssetDownloadCache.Initialize( dir );
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Project.Clear();
	}

	//Find and load a local package
	[TestMethod]
	public void AddProject()
	{
		var project = Project.AddFromFile( "unittest/addons/testmap/.rinproj" );

		Assert.IsNotNull( project.ConfigFilePath );
		Assert.IsNotNull( project.GetRootPath() );
		Assert.IsNotNull( project.GetAssetsPath() );

	}

	//Find and load a local package
	[TestMethod]
	public async Task AddBaseAddon()
	{
		var project = Project.AddFromFileBuiltIn( "addons/base/.rinproj" );

		Assert.IsNotNull( project.ConfigFilePath );
		Assert.IsNotNull( project.GetRootPath() );
		Assert.IsNotNull( project.GetAssetsPath() );

		await Project.SyncWithPackageManager();
		await Project.CompileAsync();
	}

	/*
	[TestMethod]
	public async Task OpenGameProject()
	{
		Project.AddFromFileBuiltIn( "addons/base/.rinproj" );

		var project = Project.AddFromFile( "unittest/addons/spacewars", false );

		var ct = new CancellationToken();
		await EditorUtility.Projects.OpenProject( project.Path, null, ct ); ;

		Assert.IsNotNull( project.Path );
		Assert.IsNotNull( project.GetRootPath() );
		Assert.IsNotNull( project.GetAssetsPath() );

		var assemblies = PackageManager.MountedFileSystem.FindFile( "/.bin/", "*.dll" ).ToArray();

		Assert.AreEqual( 2, assemblies.Length );

		foreach ( var asm in assemblies )
		{
			Console.WriteLine( asm );
		}
	}
	*/

	//Initialize the menu addon
	[TestMethod]
	public async Task MenuInitialization()
	{
		Project.AddFromFileBuiltIn( "addons/base/.rinproj" );

		var project = Project.AddFromFile( "addons/menu/.rinproj" );

		Assert.IsNotNull( project.ConfigFilePath );
		Assert.IsNotNull( project.GetRootPath() );
		Assert.IsNotNull( project.GetAssetsPath() );

		await Project.SyncWithPackageManager();
		await Project.CompileAsync();

		var assemblies = PackageManager.ActivePackages
			.SelectMany( ap => ap.AssemblyFileSystem.FindFile( "/", "*.dll", true ) )
			.ToArray();

		Assert.AreEqual( 2, assemblies.Length );

		foreach ( var asm in assemblies )
		{
			Console.WriteLine( asm );
		}

	}
}
