

using Rinten.Engine;

namespace Rinten;

internal static class EngineFileSystem
{
	public static BaseFileSystem Root { get; private set; }
	public static BaseFileSystem Config { get; private set; }
	public static BaseFileSystem Addons { get; private set; }
	public static BaseFileSystem Data { get; private set; }
	public static BaseFileSystem CoreContent { get; private set; }
	public static BaseFileSystem Mounted => GlobalContext.Current.FileMount;

	/// <summary>Content from libraries. This only exists in editor.</summary>
	public static BaseFileSystem LibraryContent { get; private set; }

	/// <summary>For tools, maintain a list of mounted addon content paths</summary>
	public static BaseFileSystem Assets { get; private set; }

	internal static BaseFileSystem DownloadedFiles { get; private set; }

	/// <summary>A place to write files temporarily. This is stored in memory so cleaning up after yourself is a good idea (!)</summary>
	public static BaseFileSystem Temporary { get; private set; }

	/// <summary>The .rinten/temp folder</summary>
	public static BaseFileSystem EditorTemporary { get; private set; }

	//The folder holding the project's settings files
	internal static BaseFileSystem ProjectSettings { get; set; }

	//Don't try to use the filesystem until you've called this!
	internal static void Initialize( string rootFolder, bool skipBaseFolderInit = false )
	{
		if ( Root != null )
			throw new System.Exception( "Filesystem Multi-Initialize" );

		if ( Application.OnWeb )
		{
			Root = new MemoryFileSystem();
			foreach ( var folder in new[] { "/config", "/addons", "/addons/base", "/addons/base/Assets", "/data", "/core" } )
				Root.CreateDirectory( folder );
		}
		else
		{
			Root = new LocalFileSystem( rootFolder );

			//Everything under the engine's folder is watched through one watcher.
			SharedWatchers.AddRoot( rootFolder );
		}

		Temporary = new MemoryFileSystem();

		if ( skipBaseFolderInit ) return;

		if ( Application.IsEditor )
		{
			LibraryContent = new AggregateFileSystem();

			//Create the temp folder first: it may be missing, and mounting a missing folder fails at boot
			Root.CreateDirectory( "/.rinten/temp" );
			EditorTemporary = Root.CreateSubSystem( "/.rinten/temp" );
		}

		Assets = new AggregateFileSystem();
		CoreContent = new AggregateFileSystem();

		if ( Application.IsStandalone )
		{
			Mount( CoreContent, "/core/" );

			Mount( Assets, "/core/" );
			Mount( Assets, "/addons/base/Assets" );
		}
		else
		{
			Mount( CoreContent, "/core/" );
			Mount( CoreContent, "/addons/base/Assets/" );

			Mount( Assets, "/core/" );
			Mount( Assets, "/addons/base/Assets/" );
		}
	}

	//Mounts a shipped engine folder only if present, so a removed addon doesn't break startup
	private static void Mount( BaseFileSystem into, string path )
	{
		if ( !Root.DirectoryExists( path ) ) return;

		into.CreateAndMount( Root, path );
	}

	//Setup Config parameter
	internal static void InitializeConfigFolder( string name = "/config" )
	{
		Assert.NotNull( name );
		Assert.NotNull( Root );

		Root.CreateDirectory( "/config" );
		Config = Root.CreateSubSystem( "/config" );
	}

	//Setup Addons parameter (there's no reason for this to exist now?)
	internal static void InitializeAddonsFolder( string name = "/addons" )
	{
		Assert.NotNull( name );
		Assert.NotNull( Root );

		Root.CreateDirectory( "/addons" );
		Addons = Root.CreateSubSystem( "/addons" );
	}

	//Setup Download folder
	internal static void InitializeDownloadsFolder( string name = "/download" )
	{
		Assert.NotNull( name );
		Assert.NotNull( Root );

		//alex: Don't bother if we're in standalone mode, because games aren't able to download anything from the backend
		if ( Application.IsStandalone )
			return;

		Root.CreateDirectory( $"{name}" );
		Root.CreateDirectory( $"{name}/.sv" );
		DownloadedFiles = Root.CreateSubSystem( $"{name}" );
	}

	//Separate folder for player data (saves, cookies), or null for /data; set by the host at startup
	internal static string DataFolder { get; set; }

	//Setup Addons parameter (there's no reason for this to exist now?)
	internal static void InitializeDataFolder( string name = "/data" )
	{
		Assert.NotNull( name );
		Assert.NotNull( Root );

		if ( !string.IsNullOrEmpty( DataFolder ) && !Application.OnWeb )
		{
			System.IO.Directory.CreateDirectory( DataFolder );
			Data = new LocalFileSystem( DataFolder );
			return;
		}

		Root.CreateDirectory( $"{name}" );
		Data = Root.CreateSubSystem( $"{name}" );
	}

	//Should only be called at the very death
	internal static void Shutdown()
	{
		Root = null;
		Config = null;

		DownloadedFiles?.Dispose();
		DownloadedFiles = null;

		Addons?.Dispose();
		Addons = null;

		Root?.Dispose();
		Root = null;
	}

	internal static void AddContentPath( string v )
	{
		CoreContent.Mount( new LocalFileSystem( v ) );
	}

	internal static void AddAssetPath( string ident, string path )
	{
		//Mounts are looked for while the process is still coming up, before there is a context with a filesystem in it to mount them into.
		//The path is still registered; a game that wants one of these mounts asks for it later, by which time there is somewhere to put it.
		Mounted?.Mount( new LocalFileSystem( path ) );

		EngineFiles.AddProjectPath( "xxx", path );
	}
}
