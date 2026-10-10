namespace Rinten;

//Holds the state of a standalone game
internal partial class StandaloneGameInstance : GameInstance
{
	public StandaloneGameInstance( string ident, GameLoadingFlags flags ) : base( ident, flags )
	{
		this.flags = flags;
	}

	/// <summary>Load the game this build is: its project off the disk rather than fetched, its package, and then what every load does - see <see cref="GameInstance.MountPackageAsync"/>.</summary>
	public override async Task<bool> LoadAsync( PackageLoader.Enroller enroller, CancellationToken token )
	{
		PrepareLoad();

		var project = Project.AddFromFile( Standalone.GamePath );
		await Project.SyncWithPackageManager();

		//Nothing to compile: a build carries its code compiled, in the project's .bin, and the enroller loads it from there below.
		await project.Package.MountAsync();

		_package = project.Package;
		Log.Info( $"Added from file {project.Config.Title} ({project.Package.FullIdent})" );

		PackageManager.CmdList();

		Application.GameIdent = Package is null ? Ident : $"{_package.Org.Ident}.{_package.Ident}";
		Application.GamePackage = _package;

		{
			var downloadOptions = new PackageLoadOptions
			{
				PackageIdent = Package.FullIdent,
				ContextTag = "gamemenu",
				AllowLocalPackages = true,
				CancellationToken = token
			};

			Log.Trace( $"Install Async from Package {Package.Title}" );
			LoadingScreen.Title = $"Installing {Package.Title}";
			activePackage = await PackageManager.InstallAsync( downloadOptions );
			if ( activePackage is null )
			{
				Log.Warning( $"Package {Package.FullIdent} was null" );
				return false;
			}
		}

		if ( token.IsCancellationRequested )
			return false;

		Log.Trace( $"Loading package {Package.Title}" );
		LoadingScreen.Title = $"Loading {Package.Title}";
		try
		{
			//Load the package. Mount it and add it to the file system. Only load the assemblies inside the package
			//if we're not a developer host (If we're a develop host we load assemblies from the network table)
			if ( !enroller.LoadPackage( Package.FullIdent, !IsDeveloperHost ) )
			{
				if ( IsDeveloperHost )
					return true;

				Log.Warning( "There were errors when trying to load the package for the menu" );
				return false;
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception when loading {Package.FullIdent}: {e.Message}" );
			return false;
		}

		//The build's content, for what the engine opens by its real path - before the resources are
		//read, which is when that starts. Where it is is the project's to say: see Project.GetAssetsPath.
		EngineFiles.AddProjectPath( Ident, project.GetAssetsPath() );

		return await MountPackageAsync( enroller, token );
	}
}
