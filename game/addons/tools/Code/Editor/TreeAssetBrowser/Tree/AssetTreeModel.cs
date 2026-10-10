using System.IO;

namespace Editor.TreeBrowser;

//Builds and caches the asset tree model.
sealed class AssetTreeModel
{
	public const string LibrariesSectionKey = "__header__Libraries";

	readonly Dictionary<string, AssetTreeItem> cache = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, IDisposable> watchers = new( StringComparer.OrdinalIgnoreCase );
	readonly string corePath;

	//Read when asked rather than kept: a browser is made with the editor, which comes up before its
	//project, and it is the same browser once one is open - Refresh starts the tree again from these.
	static string assetsPath => Project.Current?.GetAssetsPath();
	static string codePath => Project.Current?.GetCodePath();

	static string editorPath => Project.Current?.GetRootPath() is { Length: > 0 } rootPath
		? Path.Combine( rootPath, "Editor" )
		: null;

	HashSet<string> matchingPaths = [];
	int revision;
	bool childrenBuilt;

	public AssetTreeModel()
	{
		corePath = global::Editor.FileSystem.Root.GetFullPath( "/core/" );
	}

	/// <summary>Mode this tree is built for.</summary>
	public TreeBrowserOptions Options { get; set; } = TreeBrowserOptions.Dock;

	/// <summary>Which body of content this tree lists.</summary>
	public AssetTreeScope Scope { get; set; } = AssetTreeScope.Project;

	/// <summary>Which part of the content the tree is showing. See AssetTreeSection.</summary>
	public AssetTreeSection Showing { get; set; } = AssetTreeSections.Default;

	/// <summary>Which files the tree is allowed to list.</summary>
	public IAssetFilter Filter { get; set; } = AllAssetsFilter.Instance;

	public int Revision => revision;

	public IReadOnlyList<AssetTreeItem> Roots { get; private set; } = [];

	/// <summary>Roots worth drawing: while searching, only the ones something was found in.</summary>
	public IEnumerable<AssetTreeItem> VisibleRoots => IsSearchActive
		? Roots.Where( ShouldShowInSearch )
		: Roots;

	/// <summary>The section a branch root hangs under, for walking back up out of it.</summary>
	public AssetTreeItem FindSectionContaining( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return null;

		foreach ( var root in Roots )
		{
			if ( root.Kind != AssetTreeItemKind.Section )
				continue;

			foreach ( var child in root.Children )
			{
				if ( string.Equals( child.FullPath, path, StringComparison.OrdinalIgnoreCase ) )
					return root;
			}
		}

		return null;
	}

	public string SearchFilter { get; private set; } = string.Empty;

	public bool IsSearchActive => !string.IsNullOrEmpty( SearchFilter );

	public void Bump() => revision++;

	/// <summary>Marks the item's children for re-reading after a disk change; cached items keep their state</summary>
	public void Invalidate( AssetTreeItem item )
	{
		if ( item is null ) return;

		item.Children.Clear();

		//Something under it that is gone takes its own watch with it.
		if ( !string.IsNullOrEmpty( item.FullPath ) )
		{
			var prefix = item.FullPath.TrimEnd( Path.DirectorySeparatorChar, '/' ) + Path.DirectorySeparatorChar;

			foreach ( var gone in watchers.Keys.Where( x => x.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) && !Directory.Exists( x ) ).ToList() )
			{
				watchers[gone].Dispose();
				watchers.Remove( gone );
				cache.Remove( gone );
			}
		}

		Bump();
	}

	/// <summary>A file became an asset after its folder was read - the asset system registers a new file after the tree has already listed it - so its item takes it now.</summary>
	public void AssetChanged( Asset asset )
	{
		if ( asset is null || asset.IsDeleted || string.IsNullOrEmpty( asset.AbsolutePath ) ) return;

		if ( !cache.TryGetValue( Path.GetFullPath( asset.AbsolutePath ), out var item ) ) return;
		if ( item.Kind != AssetTreeItemKind.File || ReferenceEquals( item.Asset, asset ) ) return;

		item.Asset = asset;
		Bump();
	}

	public void Refresh( bool skipDefaultOpen = true )
	{
		DisposeWatchers();
		cache.Clear();
		childrenBuilt = false;
		Roots = BuildRoots( skipDefaultOpen );
		Bump();
	}

	public IEnumerable<AssetTreeItem> GetChildren( AssetTreeItem item )
	{
		if ( item.Children.Count == 0 )
			BuildChildren( item );

		if ( IsSearchActive )
		{
			foreach ( var child in item.Children )
			{
				if ( ShouldShowInSearch( child ) )
					yield return child;
			}

			yield break;
		}

		foreach ( var child in item.Children )
			yield return child;
	}

	public AssetTreeItem FindNode( string path )
	{
		var normalized = Path.GetFullPath( path );

		foreach ( var root in Roots )
		{
			if ( root.Kind == AssetTreeItemKind.Folder )
			{
				var found = FindUnder( root, normalized );
				if ( found is not null )
					return found;
			}
		}

		return null;
	}

	public void ApplySearch( string text )
	{
		SearchFilter = text?.ToLowerInvariant() ?? string.Empty;
		matchingPaths.Clear();

		if ( string.IsNullOrEmpty( SearchFilter ) )
		{
			Bump();
			return;
		}

		ScanMatches( assetsPath );
		ScanMatches( codePath );
		ScanMatches( editorPath );
		ScanMatches( corePath );

		foreach ( var project in EditorUtility.Projects.GetAll() )
		{
			if ( project == Project.Current || project.Config.Ident == "menu" )
				continue;

			ScanMatches( project.GetRootPath() );
		}

		Bump();
	}

	public void ClearSearch()
	{
		SearchFilter = string.Empty;
		matchingPaths.Clear();
		Bump();
	}

	public TreeItemInfo GetInfo( AssetTreeItem item ) => new( item.Label, item.Icon, item.HasChildren );

	public void EnsureWatcher( AssetTreeItem item )
	{
		if ( item.Kind != AssetTreeItemKind.Folder || string.IsNullOrEmpty( item.FullPath ) )
			return;

		if ( watchers.ContainsKey( item.FullPath ) )
			return;

		if ( !Directory.Exists( item.FullPath ) )
			return;

		//The editor's shared watcher, not one of our own per folder: each of those was an inotify
		//instance, and a few folders opened spent what the desktop had left - see FileSystem.WatchFolder.
		watchers[item.FullPath] = global::Editor.FileSystem.WatchFolder( item.FullPath, _ => MainThread.Queue( () => Invalidate( item ) ) );
	}

	void DisposeWatchers()
	{
		foreach ( var watcher in watchers.Values )
			watcher.Dispose();

		watchers.Clear();
	}

	//A search leaves only what it found: a group with no hit under it is not drawn. Recents and Everything list assets
	//rather than a folder, so they stand for the whole project - they are worth showing whenever the search found anything.
	bool ShouldShowInSearch( AssetTreeItem item ) => item.Kind switch
	{
		AssetTreeItemKind.Section => item.Children.Any( ShouldShowInSearch ),
		AssetTreeItemKind.Folder => matchingPaths.Contains( item.FullPath ),
		AssetTreeItemKind.File => matchingPaths.Contains( item.FullPath ),
		AssetTreeItemKind.PackageFolder => matchingPaths.Contains( item.Key ),
		AssetTreeItemKind.PackageSubFolder => matchingPaths.Contains( item.Key ),
		AssetTreeItemKind.Aggregate => item.IsPinned ? matchingPaths.Contains( item.FullPath ) : matchingPaths.Count > 0,
		AssetTreeItemKind.Spacer => false,
		_ => true
	};

	AssetTreeItem FindUnder( AssetTreeItem folder, string path )
	{
		if ( string.Equals( folder.FullPath, path, StringComparison.OrdinalIgnoreCase ) )
			return folder;

		BuildChildren( folder );

		foreach ( var child in folder.Children )
		{
			if ( child.Kind == AssetTreeItemKind.Folder )
			{
				if ( path.StartsWith( child.FullPath, StringComparison.OrdinalIgnoreCase ) )
				{
					var found = FindUnder( child, path );
					if ( found is not null )
						return found;
				}
			}
			else if ( child.Kind == AssetTreeItemKind.File &&
			          string.Equals( child.FullPath, path, StringComparison.OrdinalIgnoreCase ) )
			{
				return child;
			}
		}

		return null;
	}

	List<AssetTreeItem> BuildRoots( bool skipDefaultOpen ) => Scope == AssetTreeScope.Mounts
		? BuildMountRoots()
		: BuildProjectRoots( skipDefaultOpen );

	//Builds the roots of the one section the strip has picked
	List<AssetTreeItem> BuildProjectRoots( bool skipDefaultOpen )
	{
		var roots = new List<AssetTreeItem>();

		switch ( Showing )
		{
			case AssetTreeSection.Recent:
				roots.Add( LocationItem( new RecentsLocation(), suppressChildren: true ) );
				return roots;

			case AssetTreeSection.Assets:
				AddFolderRoot( roots, assetsPath, "Assets", "folder_special", skipDefaultOpen );
				return roots;

			case AssetTreeSection.Code:
				AddFolderRoot( roots, codePath, "Code", "code", skipDefaultOpen );
				return roots;

			case AssetTreeSection.Editor:
				AddFolderRoot( roots, editorPath, "Editor", "edit", skipDefaultOpen );
				return roots;

			case AssetTreeSection.Mounts:
				AddMountRoots( roots, skipDefaultOpen );
				return roots;
		}

		return BuildRemainingRoots( skipDefaultOpen );
	}

	//A section that is one folder, opened as the tree's only root.
	void AddFolderRoot( List<AssetTreeItem> roots, string path, string title, string icon, bool skipDefaultOpen )
	{
		if ( string.IsNullOrEmpty( path ) || !Directory.Exists( path ) ) return;

		var folder = Folder( path, title, icon, isRoot: true );
		roots.Add( folder );

		if ( !skipDefaultOpen )
			AssetExpandedPaths.AddDefault( folder.Key );
	}

	//Libraries, the parent package and the engine - the sections built from a list.
	List<AssetTreeItem> BuildRemainingRoots( bool skipDefaultOpen )
	{
		var roots = new List<AssetTreeItem>();

		//No collection expression: the editor's Roslyn reads a leading [] as an attribute list
		var libraryProjects = new List<Project>();

		if ( Showing is AssetTreeSection.Libraries )
		{
			libraryProjects = EditorUtility.Projects.GetAll()
				.Where( p => p != Project.Current && p.Config.Ident != "menu" )
				.OrderBy( p => p.Config.Title )
				.ToList();
		}

		foreach ( var project in libraryProjects )
		{
			var projectRoot = project.GetRootPath();
			if ( string.IsNullOrEmpty( projectRoot ) || !Directory.Exists( projectRoot ) )
				continue;

			var title = !string.IsNullOrEmpty( project.Config.Title ) ? project.Config.Title : project.Config.Ident;
			roots.Add( Folder( projectRoot, title, "inventory_2", isRoot: true ) );
		}

		var parentPackageIdent = Project.Current?.Config?.GetMetaOrDefault( "ParentPackage", "" );
		if ( Showing is AssetTreeSection.Parent &&
		     Project.Current?.Config?.Type == "addon" &&
		     !string.IsNullOrWhiteSpace( parentPackageIdent ) &&
		     Package.TryGetCached( parentPackageIdent, out var parentPackage ) )
		{
			roots.Add( PackageFolder( parentPackage, "supervisor_account" ) );
		}

		if ( Showing is AssetTreeSection.Engine )
		{
			//Its contents without a heading over them: the strip already says which section
			//this is, and a heading under a tab that names the same thing is the name twice.
			AddFolderRoot( roots, corePath, "Core", "folder", skipDefaultOpen );
		}

		return roots;
	}

	//Adds mounted games as a section of the tree; nothing is added when nothing is mounted
	void AddMountRoots( List<AssetTreeItem> roots, bool skipDefaultOpen )
	{
		if ( !Options.EnableMounts ) return;

		var mounts = MountedGames.Available().ToList();
		if ( mounts.Count == 0 ) return;

		foreach ( var source in mounts )
			roots.Add( LocationItem( new MountLocation( source ) ) );
	}

	List<AssetTreeItem> BuildMountRoots()
	{
		var roots = new List<AssetTreeItem>();

		if ( !Options.EnableMounts )
			return roots;

		foreach ( var source in MountedGames.Available() )
			roots.Add( LocationItem( new MountLocation( source ) ) );

		return roots;
	}

	void BuildChildren( AssetTreeItem item )
	{
		if ( item.Children.Count > 0 || childrenBuilt && item.Kind == AssetTreeItemKind.Section )
			return;

		switch ( item.Kind )
		{
			case AssetTreeItemKind.Folder:
				BuildFolderChildren( item );
				EnsureWatcher( item );
				break;
			case AssetTreeItemKind.PackageFolder:
				BuildPackageChildren( item );
				break;
			case AssetTreeItemKind.PackageSubFolder:
				BuildPackageSubChildren( item );
				break;
			case AssetTreeItemKind.Aggregate:
				BuildLocationChildren( item );
				break;
		}
	}

	void BuildFolderChildren( AssetTreeItem item )
	{
		item.Children.Clear();

		if ( !Directory.Exists( item.FullPath ) )
			return;

		try
		{
			foreach ( var dir in Directory.GetDirectories( item.FullPath ).OrderBy( Path.GetFileName, StringComparer.OrdinalIgnoreCase ) )
			{
				var dirInfo = new DirectoryInfo( dir );
				if ( dirInfo.Attributes.HasFlag( FileAttributes.Hidden ) )
					continue;

				if ( dirInfo.Name.Equals( "obj", StringComparison.OrdinalIgnoreCase ) )
					continue;

				if ( dirInfo.Name.StartsWith( '.' ) )
					continue;

				item.Children.Add( Folder( dir ) );
			}

			foreach ( var file in Directory.GetFiles( item.FullPath ).OrderBy( Path.GetFileName, StringComparer.OrdinalIgnoreCase ) )
			{
				var fileInfo = new FileInfo( file );
				if ( fileInfo.Attributes.HasFlag( FileAttributes.Hidden ) )
					continue;

				var fileName = fileInfo.Name;
				if ( fileName.Contains( ".generated", StringComparison.OrdinalIgnoreCase ) ||
				     fileName.EndsWith( ".meta", StringComparison.OrdinalIgnoreCase ) ||
				     fileName.StartsWith( '.' ) )
					continue;

				if ( fileName.EndsWith( "_c" ) && File.Exists( file[..^2] ) )
					continue;

				var child = FileItem( file );
				if ( !Options.AcceptsFile( file ) || !Filter.Accepts( child.Asset ) )
					continue;

				item.Children.Add( child );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Error building folder tree: {ex.Message}" );
		}
	}

	void BuildPackageChildren( AssetTreeItem item )
	{
		item.Children.Clear();

		var files = GetPackageFiles( item.Package );
		if ( files.Count == 0 )
			return;

		var directories = new Dictionary<string, List<string>>( StringComparer.OrdinalIgnoreCase );
		var rootFiles = new List<string>();

		foreach ( var file in files )
		{
			var parts = file.Split( '/', '\\' );
			if ( parts.Length > 1 )
			{
				var topDir = parts[0];
				if ( !directories.TryGetValue( topDir, out var list ) )
				{
					list = [];
					directories[topDir] = list;
				}

				list.Add( file );
			}
			else
			{
				rootFiles.Add( file );
			}
		}

		foreach ( var dir in directories.OrderBy( d => d.Key, StringComparer.OrdinalIgnoreCase ) )
			item.Children.Add( PackageSubFolder( item.Package, dir.Key, dir.Value ) );

		foreach ( var file in rootFiles.OrderBy( f => f, StringComparer.OrdinalIgnoreCase ) )
		{
			var fullPath = global::Editor.FileSystem.Cloud.GetFullPath( file );
			if ( !string.IsNullOrEmpty( fullPath ) )
				item.Children.Add( FileItem( fullPath ) );
		}
	}

	void BuildPackageSubChildren( AssetTreeItem item )
	{
		item.Children.Clear();

		var subDirs = new Dictionary<string, List<string>>( StringComparer.OrdinalIgnoreCase );
		var currentFiles = new List<string>();

		foreach ( var file in item.PackageFiles )
		{
			var relativePath = file;
			var prefix = item.PackageFolderName + "/";
			if ( file.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) ||
			     file.StartsWith( item.PackageFolderName + "\\", StringComparison.OrdinalIgnoreCase ) )
			{
				relativePath = file[(item.PackageFolderName.Length + 1)..];
			}

			var parts = relativePath.Split( '/', '\\' );
			if ( parts.Length > 1 )
			{
				var subDir = parts[0];
				if ( !subDirs.TryGetValue( subDir, out var list ) )
				{
					list = [];
					subDirs[subDir] = list;
				}

				list.Add( file );
			}
			else
			{
				currentFiles.Add( file );
			}
		}

		foreach ( var dir in subDirs.OrderBy( d => d.Key, StringComparer.OrdinalIgnoreCase ) )
		{
			var folderName = item.PackageFolderName + "/" + dir.Key;
			item.Children.Add( PackageSubFolder( item.Package, folderName, dir.Value ) );
		}

		foreach ( var file in currentFiles.OrderBy( f => f, StringComparer.OrdinalIgnoreCase ) )
		{
			var fullPath = global::Editor.FileSystem.Cloud.GetFullPath( file );
			if ( !string.IsNullOrEmpty( fullPath ) )
				item.Children.Add( FileItem( fullPath ) );
		}
	}

	static List<string> GetPackageFiles( Package package )
	{
		try
		{
			return AssetSystem.GetPackageFiles( package ).ToList();
		}
		catch
		{
			return [];
		}
	}

	void ScanMatches( string path, int maxDepth = 15 )
	{
		if ( maxDepth <= 0 || string.IsNullOrEmpty( path ) || !Directory.Exists( path ) )
			return;

		try
		{
			var hasMatchingChild = false;

			foreach ( var file in Directory.EnumerateFiles( path ) )
			{
				var fileName = Path.GetFileName( file );
				if ( fileName.StartsWith( '.' ) ||
				     fileName.Contains( ".generated", StringComparison.OrdinalIgnoreCase ) ||
				     fileName.EndsWith( ".meta", StringComparison.OrdinalIgnoreCase ) )
					continue;

				if ( fileName.EndsWith( "_c" ) && File.Exists( file[..^2] ) )
					continue;

				if ( fileName.ToLowerInvariant().Contains( SearchFilter ) )
				{
					matchingPaths.Add( Path.GetFullPath( file ) );
					hasMatchingChild = true;
				}
			}

			foreach ( var dir in Directory.EnumerateDirectories( path ) )
			{
				var dirName = Path.GetFileName( dir );
				if ( dirName.StartsWith( '.' ) || dirName.Equals( "obj", StringComparison.OrdinalIgnoreCase ) )
					continue;

				if ( dirName.ToLowerInvariant().Contains( SearchFilter ) )
				{
					matchingPaths.Add( Path.GetFullPath( dir ) );
					hasMatchingChild = true;
				}

				var countBefore = matchingPaths.Count;
				ScanMatches( dir, maxDepth - 1 );

				if ( matchingPaths.Count > countBefore )
				{
					matchingPaths.Add( Path.GetFullPath( dir ) );
					hasMatchingChild = true;
				}
			}

			if ( hasMatchingChild )
				matchingPaths.Add( Path.GetFullPath( path ) );
		}
		catch
		{
			// Ignore filesystem errors.
		}
	}

	void BuildLocationChildren( AssetTreeItem item )
	{
		item.Children.Clear();

		if ( item.SuppressChildren || item.BrowseLocation is null )
			return;

		try
		{
			foreach ( var directory in item.BrowseLocation.GetDirectories().OrderBy( x => x.Name, StringComparer.OrdinalIgnoreCase ) )
				item.Children.Add( LocationItem( directory ) );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Error listing browse location: {ex.Message}" );
		}
	}

	AssetTreeItem LocationItem( AssetBrowser.Location location, bool suppressChildren = false )
	{
		var key = $"__location__{location.Path}";
		if ( cache.TryGetValue( key, out var existing ) )
			return existing;

		var item = new AssetTreeItem
		{
			Kind = AssetTreeItemKind.Aggregate,
			Key = key,
			Label = location.Name,
			Icon = location.Icon ?? "folder",
			BrowseLocation = location,
			SuppressChildren = suppressChildren
		};

		cache[key] = item;
		return item;
	}




	AssetTreeItem Spacer( string key )
	{
		if ( cache.TryGetValue( key, out var existing ) )
			return existing;

		var item = new AssetTreeItem { Kind = AssetTreeItemKind.Spacer, Key = key };
		cache[key] = item;
		return item;
	}

	AssetTreeItem Folder( string path, string displayName = null, string icon = "folder", bool isRoot = false )
	{
		var fullPath = Path.GetFullPath( path );
		if ( cache.TryGetValue( fullPath, out var existing ) )
			return existing;

		var item = new AssetTreeItem
		{
			Kind = AssetTreeItemKind.Folder,
			Key = fullPath,
			FullPath = fullPath,
			Label = displayName ?? Path.GetFileName( fullPath ),
			Icon = icon,
			IsRoot = isRoot,
			CanRename = !isRoot,
			CanDrag = !isRoot
		};

		cache[fullPath] = item;
		return item;
	}

	AssetTreeItem FileItem( string path )
	{
		var fullPath = Path.GetFullPath( path );
		if ( cache.TryGetValue( fullPath, out var existing ) )
		{
			//Kept from before the folder was read again: the file may have become an asset since, or stopped being one.
			if ( existing.Asset is null or { IsDeleted: true } )
				existing.Asset = AssetSystem.FindByPath( fullPath );

			return existing;
		}

		var fileName = Path.GetFileName( fullPath );
		var item = new AssetTreeItem
		{
			Kind = AssetTreeItemKind.File,
			Key = fullPath,
			FullPath = fullPath,
			Label = Path.GetFileNameWithoutExtension( fileName ),
			Extension = Path.GetExtension( fileName ).ToLowerInvariant(),
			Icon = AssetFileIcons.GetIcon( Path.GetExtension( fileName ) ),
			CanRename = true,
			CanDrag = true,
			Asset = AssetSystem.FindByPath( fullPath )
		};

		cache[fullPath] = item;
		return item;
	}

	AssetTreeItem Section( string icon, string label, string key )
	{
		if ( cache.TryGetValue( key, out var existing ) )
			return existing;

		var item = new AssetTreeItem
		{
			Kind = AssetTreeItemKind.Section,
			Key = key,
			Label = label,
			Icon = icon
		};

		cache[key] = item;
		return item;
	}

	AssetTreeItem PackageFolder( Package package, string icon )
	{
		var key = $"__package__{package.FullIdent}";
		if ( cache.TryGetValue( key, out var existing ) )
			return existing;

		var item = new AssetTreeItem
		{
			Kind = AssetTreeItemKind.PackageFolder,
			Key = key,
			Label = package.Title,
			Icon = icon,
			Package = package,
			PackageFiles = GetPackageFiles( package )
		};

		cache[key] = item;
		return item;
	}

	AssetTreeItem PackageSubFolder( Package package, string folderName, List<string> files )
	{
		var key = $"__packagesub__{package.FullIdent}__{folderName}";
		if ( cache.TryGetValue( key, out var existing ) )
			return existing;

		var display = folderName.Contains( '/' ) ? folderName.Split( '/' ).Last() : folderName;
		var item = new AssetTreeItem
		{
			Kind = AssetTreeItemKind.PackageSubFolder,
			Key = key,
			Label = display,
			Icon = "folder",
			Package = package,
			PackageFolderName = folderName,
			PackageFiles = files
		};

		cache[key] = item;
		return item;
	}
}
