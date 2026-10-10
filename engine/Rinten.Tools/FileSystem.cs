using System.IO;

namespace Editor;

/// <summary>A filesystem that can be accessed by the game.</summary>
public static class FileSystem
{
	/// <summary>Paths from tool addons which are mounted.</summary>
	public static BaseFileSystem Mounted => Rinten.FileSystem.Mounted;

	/// <summary>Root of the game's folder.</summary>
	public static BaseFileSystem Root => EngineFileSystem.Root;

	/// <summary>The engine /game/.rinten/ folder for temporary files and caches.</summary>
	public static BaseFileSystem Temporary { get; internal set; }

	/// <summary>The engine /game/config/ folder</summary>
	public static BaseFileSystem Config => EngineFileSystem.Config;

	/// <summary>The engine /game/.rinten/http/ folder.</summary>
	public static BaseFileSystem WebCache { get; internal set; }

	/// <summary>The current project's .rinten/ folder for temporary files and caches.</summary>
	public static BaseFileSystem ProjectTemporary { get; internal set; }

	/// <summary>The current project's .rinten/cloud/ folder. Downloaded packages land here.</summary>
	public static BaseFileSystem Cloud { get; internal set; }

	/// <summary>The current project's .rinten/transient/ folder. This is where assets are created at runtime. These are assets that are created by another asset,that don't need to be stored in source control or anything - because they can get re-created at will.</summary>
	public static BaseFileSystem Transient { get; internal set; }

	/// <summary>Content from active addons (and content paths)</summary>
	public static BaseFileSystem Content { get; internal set; }

	/// <summary>The current project's ProjectSettings folder</summary>
	public static BaseFileSystem ProjectSettings { get; internal set; }

	/// <summary>The current project's Libraries folder</summary>
	public static BaseFileSystem Libraries { get; internal set; }

	/// <summary>The current project's Localization folder</summary>
	public static BaseFileSystem Localization { get; internal set; }

	//Should be called on startup and whenever the mounted local addons have changed
	internal static void RebuildContentPath()
	{
		Content?.Dispose();
		Content = null;

		Content = new AggregateFileSystem();
		Content.CreateAndMountIfExists( EngineFileSystem.Root, "/core/" );
		Content.CreateAndMountIfExists( EngineFileSystem.Root, "/addons/base/Assets/" );
		Content.Mount( Cloud );

		foreach ( var addon in Project.All.Where( x => x.Active ) )
		{
			var contentPath = addon.GetAssetsPath();
			if ( string.IsNullOrWhiteSpace( contentPath ) ) continue;
			if ( !System.IO.Directory.Exists( contentPath ) ) continue;

			Content.CreateAndMount( contentPath );
		}

		var watch = Content.Watch();
		watch.OnChangedFile += OnContentFileChanged;

		if ( EditorBackend.Panels ) return;

		foreach ( var addon in Project.All.Where( x => x.Active && x.Config.Type == "tool" ) )
		{
			var contentPath = addon.GetAssetsPath();
			if ( string.IsNullOrWhiteSpace( contentPath ) ) continue;
			if ( !System.IO.Directory.Exists( contentPath ) ) continue;

		}
	}

	//Called when a file has changed on the Content path.
	private static void OnContentFileChanged( string filename )
	{
		ThreadSafe.AssertIsMainThread();

		//The editor's own write, of something nothing built from the file carries - a model's graph, written into its definition. Compiling the
		//model again and reading it back onto the card is what the rest of this does, and neither would change a thing. See ResourceReload.Unchanged.
		if ( Content?.GetFullPath( filename ) is { } onDisk && Rinten.ResourceReload.IsUnchanged( onDisk ) )
			return;

		//Textures generated from a picture that was drawn over.
		Rinten.Resources.ImageFileGenerator.FileChanged( filename );

		//A file that appeared is an asset that appeared, where we are the ones keeping the list.
		CoreAssetScan.FileChanged( Content?.GetFullPath( filename ) ?? filename );

		//Check to see if this asset was deleted from explorer - and mark it as deleted in the asset system.
		if ( AssetSystem.FindByPath( filename ) is Asset asset )
		{
			var fileExists = System.IO.File.Exists( asset.AbsolutePath );
			if ( !asset.IsDeleted && !fileExists )
			{
				asset.IsDeleted = true;
			}

			//Source file for a gameresource was edited, let's compile it (for detecting changes from version control)
			if ( asset.TryLoadResource<GameResource>( out var gameResource ) )
			{
				asset.Compile( false );
			}
		}

		//Tell engine caches the file changed, or they keep serving the old version until restart
		Rinten.ResourceReload.Changed( filename );

		EditorEvent.Run( "content.changed", filename );
	}

	/// <summary>Stop the game from triggering a hotload for this file - because presumably you have already reloaded it.</summary>
	public static void SuppressNextHotload()
	{
		FileWatch.SuppressWatchers = RealTime.Now + 0.5f;
	}

	/// <summary>Be told when a file or folder is created, deleted or renamed directly in a folder on the disk - through the editor's shared watcher rather than one of your own, which on Linux is an inotify instance out of a budget the desktop has mostly spent already. See SharedWatchers.</summary>
	public static System.IDisposable WatchFolder( string folder, System.Action<string> changed )
	{
		return SharedWatchers.Listen( folder, recursive: false, contents: false, ( _, path ) => changed?.Invoke( path ) );
	}

	//Initialize the editor filesytems from this project, which is assumably the main game project.
	internal static void InitializeFromProject( Project project )
	{
		var root = project.GetRootPath();

		//Everything under the project's folder is watched through one watcher.
		SharedWatchers.AddRoot( root );

		var projectsFolder = Path.Combine( root, "ProjectSettings" );
		Directory.CreateDirectory( projectsFolder );
		ProjectSettings = new LocalFileSystem( projectsFolder );

		var librariesFolder = Path.Combine( root, "Libraries" );
		Directory.CreateDirectory( librariesFolder );
		Libraries = new LocalFileSystem( librariesFolder );

		var localizationFolder = Path.Combine( root, "Localization" );
		Directory.CreateDirectory( localizationFolder );
		Localization = new LocalFileSystem( localizationFolder );

		var cacheFolder = Path.Combine( root, Project.CacheFolder );
		Directory.CreateDirectory( cacheFolder );
		ProjectTemporary = new LocalFileSystem( cacheFolder );

		//Set folder as hidden. Will hide it from explorer (by default) and from the asset browser
		var di = new DirectoryInfo( cacheFolder );
		di.Attributes |= FileAttributes.Hidden;

		var cloudFolder = Path.Combine( cacheFolder, "cloud" );
		Directory.CreateDirectory( cloudFolder );
		Cloud = new LocalFileSystem( cloudFolder );
		Mounted.Mount( Cloud );

		var transientFolder = Path.Combine( cacheFolder, "transient" );
		Directory.CreateDirectory( transientFolder );
		Transient = new LocalFileSystem( transientFolder );
		Mounted.Mount( Transient );

		EditorSheetPreamble.Register();
	}
}
