using System.Collections.Concurrent;
using System.IO;
using Zio;
using Zio.FileSystems;

namespace Rinten;

//A physical filesystem that resolves paths case-insensitively on Linux.
internal sealed class CaseInsensitivePhysicalFileSystem : PhysicalFileSystem
{
	//Real directory path -> the names inside it.
	private readonly ConcurrentDictionary<string, DirectoryEntries> _directoryCache = new( StringComparer.Ordinal );

	//Input path -> resolved path with correct on-disk casing. Keyed exactly, not ignoring case: when two folders
	//differ only by case they both really exist, and "code" and "Code" have to be able to resolve to different places.
	private readonly ConcurrentDictionary<string, string> _resolvedPathCache = new( StringComparer.Ordinal );

	//Cached misses for paths whose parent listing exists; cleared whenever this filesystem changes
	private readonly ConcurrentDictionary<string, string> _unresolved = new( StringComparer.Ordinal );

	//Every one of these alive, for DiskChanged.
	private static readonly List<WeakReference<CaseInsensitivePhysicalFileSystem>> all = new();

	public CaseInsensitivePhysicalFileSystem()
	{
		lock ( all )
		{
			all.RemoveAll( x => !x.TryGetTarget( out _ ) );
			all.Add( new WeakReference<CaseInsensitivePhysicalFileSystem>( this ) );
		}
	}

	//External disk change: drop cached listings so the next lookup sees the disk as it is now
	internal static void DiskChanged( string fullPath )
	{
		if ( string.IsNullOrEmpty( fullPath ) ) return;

		var path = fullPath.Replace( '\\', '/' ).TrimEnd( '/' );

		CaseInsensitivePhysicalFileSystem[] live;

		lock ( all )
		{
			live = all.Select( x => x.TryGetTarget( out var fs ) ? fs : null ).Where( x => x is not null ).ToArray();
		}

		foreach ( var fs in live ) fs.Forget( path );
	}

	//What was known about a path that changed, and about everything under it.
	private void Forget( string path )
	{
		InvalidateParent( path );

		var under = path + "/";

		foreach ( var key in _directoryCache.Keys )
		{
			if ( key == path || key.StartsWith( under, StringComparison.Ordinal ) )
				_directoryCache.TryRemove( key, out _ );
		}
	}

	protected override string ConvertPathToInternalImpl( UPath path )
	{
		return ResolvePathCasing( base.ConvertPathToInternalImpl( path ) );
	}

	//A watcher that listens to the one shared watcher under its folder rather than being an inotify instance of its own - see SharedWatchers.
	protected override IFileSystemWatcher WatchImpl( UPath path )
	{
		return new SharedPhysicalWatcher( this, path, ConvertPathToInternal( path ) );
	}

	//Resolves each path component to its on-disk casing; exact matches kept, stops at first missing one
	private string ResolvePathCasing( string path )
	{
		if ( path.Length < 2 )
			return path;

		if ( _resolvedPathCache.TryGetValue( path, out var cached ) )
			return cached;

		if ( _unresolved.TryGetValue( path, out var partial ) )
			return partial;

		var components = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );
		var resolved = "/";

		for ( var i = 0; i < components.Length; i++ )
		{
			var entries = GetDirectoryEntries( resolved );

			if ( entries is null || !entries.TryResolve( components[i], out var realName ) )
			{
				//Resolving stops at the first name that is not there, and the rest is kept as asked for: a file about to be
				//made in a folder the caller spelt with other capitals lands in that folder rather than in a new one beside it.
				var rest = Append( resolved, string.Join( '/', components, i, components.Length - i ) );

				//A no is remembered only when the folder that said it exists - one that is not there yet may be by the time we are asked again.
				if ( entries is not null )
					_unresolved.TryAdd( path, rest );

				return rest;
			}

			resolved = Append( resolved, realName );
		}

		_resolvedPathCache.TryAdd( path, resolved );
		return resolved;
	}

	private static string Append( string directory, string name )
	{
		return directory == "/" ? $"/{name}" : $"{directory}/{name}";
	}

	//The names inside directory, or null if it doesn't exist.
	private DirectoryEntries GetDirectoryEntries( string directory )
	{
		if ( _directoryCache.TryGetValue( directory, out var entries ) )
			return entries;

		if ( !Directory.Exists( directory ) )
			return null;

		try
		{
			var lookup = new DirectoryEntries( new DirectoryInfo( directory ).GetFileSystemInfos() );

			_directoryCache.TryAdd( directory, lookup );
			return lookup;
		}
		catch
		{
			return null;
		}
	}

	//Cache invalidation for mutations

	private void InvalidateParent( string resolvedPath )
	{
		var parent = Path.GetDirectoryName( resolvedPath );

		if ( parent is not null )
			_directoryCache.TryRemove( parent, out _ );

		InvalidateResolvedPaths( parent ?? resolvedPath );

		//A name that was not there may be now, under whatever casing it was made with - so every no is asked again, which is the cheap half to lose.
		_unresolved.Clear();
	}

	//Remove resolved-path cache entries whose resolved value passes through directoryPrefix.
	private void InvalidateResolvedPaths( string directoryPrefix )
	{
		foreach ( var kvp in _resolvedPathCache )
		{
			if ( kvp.Value.StartsWith( directoryPrefix, StringComparison.Ordinal ) )
				_resolvedPathCache.TryRemove( kvp.Key, out _ );
		}
	}

	protected override void CreateDirectoryImpl( UPath path )
	{
		base.CreateDirectoryImpl( path );
		var resolved = ConvertPathToInternal( path );
		InvalidateParent( resolved );
		_directoryCache.TryRemove( resolved, out _ );
	}

	protected override void DeleteDirectoryImpl( UPath path, bool isRecursive )
	{
		var resolved = ConvertPathToInternal( path );
		base.DeleteDirectoryImpl( path, isRecursive );
		InvalidateParent( resolved );
		_directoryCache.TryRemove( resolved, out _ );
	}

	protected override void DeleteFileImpl( UPath path )
	{
		var resolved = ConvertPathToInternal( path );
		base.DeleteFileImpl( path );
		InvalidateParent( resolved );
	}

	protected override void MoveDirectoryImpl( UPath srcPath, UPath destPath )
	{
		var resolvedSrc = ConvertPathToInternal( srcPath );
		base.MoveDirectoryImpl( srcPath, destPath );
		InvalidateParent( resolvedSrc );
		InvalidateParent( ConvertPathToInternal( destPath ) );
		_directoryCache.TryRemove( resolvedSrc, out _ );
	}

	protected override void MoveFileImpl( UPath srcPath, UPath destPath )
	{
		var resolvedSrc = ConvertPathToInternal( srcPath );
		base.MoveFileImpl( srcPath, destPath );
		InvalidateParent( resolvedSrc );
		InvalidateParent( ConvertPathToInternal( destPath ) );
	}

	protected override void CopyFileImpl( UPath srcPath, UPath destPath, bool overwrite )
	{
		base.CopyFileImpl( srcPath, destPath, overwrite );
		InvalidateParent( ConvertPathToInternal( destPath ) );
	}

	protected override Stream OpenFileImpl( UPath path, FileMode mode, FileAccess access, FileShare share )
	{
		var stream = base.OpenFileImpl( path, mode, access, share );

		if ( mode is FileMode.Create or FileMode.CreateNew or FileMode.OpenOrCreate )
			InvalidateParent( ConvertPathToInternal( path ) );

		return stream;
	}

	//Clear all caches (e.g. after external file changes).
	internal void InvalidateCache()
	{
		_directoryCache.Clear();
		_resolvedPathCache.Clear();
		_unresolved.Clear();
	}

	//The names in a single directory, looked up ignoring case.
	private sealed class DirectoryEntries
	{
		private readonly Dictionary<string, string> names;

		//Names that lost the case-insensitive race above, so we can still find them by their exact spelling. Stays
		//null for the overwhelming majority of directories, which don't contain two names differing only by case.
		private readonly HashSet<string> alsoSpelled;

		public DirectoryEntries( FileSystemInfo[] infos )
		{
			names = new Dictionary<string, string>( infos.Length, StringComparer.OrdinalIgnoreCase );

			foreach ( var info in infos )
			{
				if ( !names.TryAdd( info.Name, info.Name ) )
				{
					alsoSpelled ??= new HashSet<string>( StringComparer.Ordinal );
					alsoSpelled.Add( info.Name );
				}
			}
		}

		/// <summary>Find the real on-disk name for <paramref name="name"/>, preferring an exact match.</summary>
		public bool TryResolve( string name, out string realName )
		{
			if ( alsoSpelled is not null && alsoSpelled.Contains( name ) )
			{
				realName = name;
				return true;
			}

			return names.TryGetValue( name, out realName );
		}
	}
}
