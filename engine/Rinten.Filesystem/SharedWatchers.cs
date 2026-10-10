namespace Rinten;

//One recursive watcher per root shared by all listeners, to save scarce inotify instances on Linux
internal static class SharedWatchers
{
	//Someone listening to a shared watcher for what happens under their folder.
	internal interface IListener
	{
		//The physical folder listened to, with a slash on the end.
		string Folder { get; }

		//Whether what happens in its subfolders is wanted as well.
		bool Recursive { get; }

		void Deliver( Zio.WatcherChangeTypes type, string fullPath, string oldFullPath );

		void DeliverError( Exception exception );
	}

	private static readonly object gate = new();

	//Folders everything under which goes through one watcher.
	private static readonly List<string> roots = new();

	private static readonly List<Hub> hubs = new();

	//Where a watcher could not be made yet, how deep it has to hear, and who is waiting for it - they are attached the moment one can be made.
	private static readonly Dictionary<string, (bool Recursive, List<IListener> Waiting)> refused = new();

	//Tries the refused watchers again while there are any; see Retry.
	private static System.Threading.Timer retryTimer;

	//How often a refused watcher is tried again.
	private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds( 5 );

	//Everything under folder is watched through one watcher: the engine's folder, a project's.
	internal static void AddRoot( string folder )
	{
		if ( string.IsNullOrWhiteSpace( folder ) ) return;

		var root = Normalize( folder );

		lock ( gate )
		{
			if ( !roots.Contains( root ) ) roots.Add( root );
		}
	}

	//A folder as the watchers compare them: full, forward slashes, one on the end.
	internal static string Normalize( string folder )
	{
		var full = System.IO.Path.GetFullPath( folder ).Replace( '\\', '/' );

		return full.EndsWith( '/' ) ? full : full + "/";
	}

	//Whether a path is the listener's folder or anything under it.
	private static bool Covers( IListener listener, string path )
	{
		return path.StartsWith( listener.Folder, StringComparison.Ordinal ) || IsFolder( listener, path );
	}

	//Whether a path is the listener's folder itself.
	private static bool IsFolder( IListener listener, string path )
	{
		return string.Equals( path + "/", listener.Folder, StringComparison.Ordinal );
	}

	//Whether a folder is under one of the roots.
	private static bool UnderRoot( string folder )
	{
		lock ( gate )
		{
			return roots.Any( x => folder.StartsWith( x, StringComparison.Ordinal ) );
		}
	}

	//Starts a listener hearing what happens under its folder.
	internal static void Attach( IListener listener )
	{
		lock ( gate )
		{
			foreach ( var existing in hubs )
			{
				if ( existing.Has( listener ) ) return;
			}

			var hub = hubs.FirstOrDefault( x => x.Serves( listener ) );

			if ( hub is null )
			{
				var at = HubRoot( listener.Folder, out var underRoot );

				//Under a root it is the root's watcher, of everything under it; otherwise only as deep as the listener
				//wants - on Windows a single folder watched recursively is every build under it arriving here.
				var recursive = underRoot || listener.Recursive;

				//Already refused: wait with the others for it to be made.
				if ( refused.TryGetValue( at, out var pending ) )
				{
					if ( !pending.Waiting.Contains( listener ) ) pending.Waiting.Add( listener );

					//Made as deep as the deepest of them wants.
					refused[at] = (pending.Recursive || recursive, pending.Waiting);
					return;
				}

				try
				{
					hub = Open( at, recursive );
				}
				catch ( Exception e )
				{
					refused[at] = (recursive, [listener]);
					retryTimer ??= new System.Threading.Timer( _ => Retry(), null, RetryEvery, RetryEvery );

					Log.Warning( $"Can't watch {at} for changes yet, so nothing under it hotloads until it can - " +
						$"trying again every {RetryEvery.TotalSeconds:0} seconds. On Linux this is fs.inotify.max_user_instances " +
						$"run out: the desktop holds most of it, and a session left behind holds a desktop's worth more. ({e.Message})" );

					return;
				}
			}

			hub.Add( listener );
		}
	}

	//Makes the watcher for a root and puts it on the list, taking over any narrower ones made before the root above them was known. Under the gate.
	private static Hub Open( string at, bool recursive )
	{
		var hub = new Hub( at, recursive );

		if ( recursive )
		{
			foreach ( var narrower in hubs.Where( x => x.Root.StartsWith( at, StringComparison.Ordinal ) ).ToArray() )
			{
				hub.TakeOver( narrower );
				hubs.Remove( narrower );
			}
		}

		hubs.Add( hub );

		return hub;
	}

	//Retry failed watchers and hand them to waiting listeners; changes missed meanwhile aren't replayed
	private static void Retry()
	{
		lock ( gate )
		{
			foreach ( var at in refused.Keys.ToArray() )
			{
				var (recursive, waiting) = refused[at];

				if ( waiting.Count == 0 )
				{
					refused.Remove( at );
					continue;
				}

				Hub hub;

				try
				{
					hub = Open( at, recursive );
				}
				catch
				{
					continue;
				}

				refused.Remove( at );

				foreach ( var listener in waiting )
					hub.Add( listener );

				Log.Info( $"Watching {at} for changes again - hotload is back for everything under it." );
			}

			if ( refused.Count == 0 )
			{
				retryTimer?.Dispose();
				retryTimer = null;
			}
		}
	}

	//Stops a listener hearing anything; the watcher goes with its last listener.
	internal static void Detach( IListener listener )
	{
		lock ( gate )
		{
			foreach ( var pending in refused.Values )
				pending.Waiting.Remove( listener );

			foreach ( var hub in hubs )
			{
				if ( !hub.Remove( listener ) ) continue;

				if ( hub.Empty )
				{
					hubs.Remove( hub );
					hub.Close();
				}

				return;
			}
		}
	}

	//Where the watcher for a folder goes: the outermost root it is under, or the folder itself.
	private static string HubRoot( string folder, out bool underRoot )
	{
		string best = null;

		foreach ( var root in roots )
		{
			if ( !folder.StartsWith( root, StringComparison.Ordinal ) ) continue;

			if ( best is null || root.Length < best.Length ) best = root;
		}

		underRoot = best is not null;

		return best ?? folder;
	}

	//Be told when something in a folder changes, through the shared watcher.
	internal static IDisposable Listen( string folder, bool recursive, bool contents, Action<System.IO.WatcherChangeTypes, string> changed )
	{
		var listener = new CallbackListener( Normalize( folder ), recursive, contents, changed );

		//A folder of its own, under nothing watched already, is a watch in the one shared instance on Linux rather than an instance.
		if ( recursive || UnderRoot( listener.Folder ) || !InotifyFolders.Add( listener ) )
			Attach( listener );

		return listener;
	}

	private sealed class CallbackListener : IListener, IDisposable
	{
		public string Folder { get; }

		public bool Recursive { get; }

		private readonly bool contents;
		private Action<System.IO.WatcherChangeTypes, string> changed;

		public CallbackListener( string folder, bool recursive, bool contents, Action<System.IO.WatcherChangeTypes, string> changed )
		{
			Folder = folder;
			Recursive = recursive;
			this.contents = contents;
			this.changed = changed;
		}

		public void Deliver( Zio.WatcherChangeTypes type, string fullPath, string oldFullPath )
		{
			if ( type == Zio.WatcherChangeTypes.Changed && !contents ) return;

			//Only what is directly in the folder, and the folder itself, the way a watcher of just that folder would have seen it.
			if ( !Recursive && !InFolder( fullPath ) && ( oldFullPath is null || !InFolder( oldFullPath ) ) ) return;

			var kind = type switch
			{
				Zio.WatcherChangeTypes.Created => System.IO.WatcherChangeTypes.Created,
				Zio.WatcherChangeTypes.Deleted => System.IO.WatcherChangeTypes.Deleted,
				Zio.WatcherChangeTypes.Renamed => System.IO.WatcherChangeTypes.Renamed,
				_ => System.IO.WatcherChangeTypes.Changed,
			};

			changed?.Invoke( kind, fullPath );
		}

		private bool InFolder( string path )
		{
			if ( IsFolder( this, path ) ) return true;

			var slash = path.LastIndexOf( '/' );

			return slash >= 0 && string.Equals( path[..( slash + 1 )], Folder, StringComparison.Ordinal );
		}

		public void DeliverError( Exception exception )
		{
			changed?.Invoke( System.IO.WatcherChangeTypes.Changed, Folder.Length > 1 ? Folder.TrimEnd( '/' ) : Folder );
		}

		public void Dispose()
		{
			changed = null;

			InotifyFolders.Remove( this );
			Detach( this );
		}
	}

	//The one watcher under a root, and who is listening to it.
	private sealed class Hub
	{
		public string Root { get; }

		/// <summary>Whether it hears its subfolders - always, for a root.</summary>
		public bool Recursive { get; }

		private readonly System.IO.FileSystemWatcher watcher;

		//Replaced whole under the gate, read without it from the watcher's threads.
		private IListener[] listeners = Array.Empty<IListener>();

		public Hub( string root, bool recursive )
		{
			Root = root;
			Recursive = recursive;

			watcher = new System.IO.FileSystemWatcher( root.Length > 1 ? root.TrimEnd( '/' ) : root )
			{
				IncludeSubdirectories = recursive,
				NotifyFilter = System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.DirectoryName
					| System.IO.NotifyFilters.Attributes | System.IO.NotifyFilters.Size
					| System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.CreationTime
					| System.IO.NotifyFilters.Security,

				//Everyone under the root shares this queue, so it is given room.
				InternalBufferSize = 64 * 1024,
			};

			watcher.Changed += ( _, e ) => Dispatch( Zio.WatcherChangeTypes.Changed, e.FullPath, null );
			watcher.Created += ( _, e ) => Dispatch( Zio.WatcherChangeTypes.Created, e.FullPath, null );
			watcher.Deleted += ( _, e ) => Dispatch( Zio.WatcherChangeTypes.Deleted, e.FullPath, null );
			watcher.Renamed += ( _, e ) => Dispatch( Zio.WatcherChangeTypes.Renamed, e.FullPath, e.OldFullPath );
			watcher.Error += ( _, e ) => DispatchError( e.GetException() );

			try
			{
				watcher.EnableRaisingEvents = true;
			}
			catch
			{
				watcher.Dispose();
				throw;
			}
		}

		/// <summary>Whether this watcher hears everything a listener wants.</summary>
		public bool Serves( IListener listener )
		{
			if ( !listener.Folder.StartsWith( Root, StringComparison.Ordinal ) ) return false;

			return Recursive || ( !listener.Recursive && listener.Folder == Root );
		}

		public bool Has( IListener listener ) => Array.IndexOf( listeners, listener ) >= 0;

		public void Add( IListener listener ) => listeners = [.. listeners, listener];

		public bool Remove( IListener listener )
		{
			if ( !Has( listener ) ) return false;

			listeners = listeners.Where( x => x != listener ).ToArray();

			return true;
		}

		public bool Empty => listeners.Length == 0;

		public void TakeOver( Hub other )
		{
			listeners = [.. listeners, .. other.listeners];
			other.listeners = Array.Empty<IListener>();
			other.Close();
		}

		public void Close()
		{
			try
			{
				watcher.EnableRaisingEvents = false;
				watcher.Dispose();
			}
			catch
			{
				//Closing a watcher that is already gone.
			}
		}

		private void Dispatch( Zio.WatcherChangeTypes type, string fullPath, string oldFullPath )
		{
			var path = fullPath.Replace( '\\', '/' );
			var old = oldFullPath?.Replace( '\\', '/' );

			//What the case-resolving filesystems know about these folders is not true any more - forgotten before anybody listening asks them again.
			if ( type is Zio.WatcherChangeTypes.Created or Zio.WatcherChangeTypes.Deleted or Zio.WatcherChangeTypes.Renamed )
			{
				CaseInsensitivePhysicalFileSystem.DiskChanged( path );
				if ( old is not null ) CaseInsensitivePhysicalFileSystem.DiskChanged( old );
			}

			foreach ( var listener in listeners )
			{
				if ( !Covers( listener, path ) && ( old is null || !Covers( listener, old ) ) ) continue;

				try
				{
					listener.Deliver( type, path, old );
				}
				catch ( Exception e )
				{
					Log.Warning( e, $"a file watcher's listener threw on {path}" );
				}
			}
		}

		private void DispatchError( Exception exception )
		{
			foreach ( var listener in listeners )
			{
				try
				{
					listener.DeliverError( exception );
				}
				catch
				{
					//Telling somebody the watcher failed is not worth failing over.
				}
			}
		}
	}
}

//A watcher of a physical filesystem that listens to the shared one underneath rather than being one - see SharedWatchers.
internal sealed class SharedPhysicalWatcher : Zio.FileSystems.FileSystemWatcher, SharedWatchers.IListener
{
	private readonly Zio.FileSystems.PhysicalFileSystem physical;

	public string Folder { get; }

	public bool Recursive => IncludeSubdirectories;

	public SharedPhysicalWatcher( Zio.FileSystems.PhysicalFileSystem fileSystem, Zio.UPath path, string folder ) : base( fileSystem, path )
	{
		physical = fileSystem;
		Folder = SharedWatchers.Normalize( folder );
	}

	/// <summary>Listening is being enabled: that is when the shared watcher is asked.</summary>
	public override bool EnableRaisingEvents
	{
		get => base.EnableRaisingEvents;
		set
		{
			if ( base.EnableRaisingEvents == value ) return;

			base.EnableRaisingEvents = value;

			if ( value ) SharedWatchers.Attach( this );
			else SharedWatchers.Detach( this );
		}
	}

	public void Deliver( Zio.WatcherChangeTypes type, string fullPath, string oldFullPath )
	{
		var path = physical.ConvertPathFromInternal( fullPath );

		switch ( type )
		{
			case Zio.WatcherChangeTypes.Created:
				RaiseCreated( new Zio.FileChangedEventArgs( physical, type, path ) );
				break;

			case Zio.WatcherChangeTypes.Deleted:
				RaiseDeleted( new Zio.FileChangedEventArgs( physical, type, path ) );
				break;

			case Zio.WatcherChangeTypes.Renamed:
				RaiseRenamed( new Zio.FileRenamedEventArgs( physical, type, path, physical.ConvertPathFromInternal( oldFullPath ?? fullPath ) ) );
				break;

			default:
				RaiseChanged( new Zio.FileChangedEventArgs( physical, type, path ) );
				break;
		}
	}

	public void DeliverError( Exception exception ) => RaiseError( new Zio.FileSystemErrorEventArgs( exception ) );

	protected override void Dispose( bool disposing )
	{
		if ( disposing ) SharedWatchers.Detach( this );

		base.Dispose( disposing );
	}
}
