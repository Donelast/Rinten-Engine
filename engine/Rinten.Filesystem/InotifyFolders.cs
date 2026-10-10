using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Rinten;

//Linux: watches standalone folders in one shared inotify instance instead of one instance each
internal static class InotifyFolders
{
	[DllImport( "libc", SetLastError = true )]
	private static extern int inotify_init1( int flags );

	[DllImport( "libc", SetLastError = true )]
	private static extern int inotify_add_watch( int fd, [MarshalAs( UnmanagedType.LPUTF8Str )] string path, uint mask );

	[DllImport( "libc", SetLastError = true )]
	private static extern int inotify_rm_watch( int fd, int wd );

	[DllImport( "libc", SetLastError = true )]
	private static extern nint read( int fd, byte[] buffer, nint count );

	private const int IN_CLOEXEC = 0x80000;
	private const int EINTR = 4;

	private const uint IN_MODIFY = 0x2;
	private const uint IN_ATTRIB = 0x4;
	private const uint IN_CLOSE_WRITE = 0x8;
	private const uint IN_MOVED_FROM = 0x40;
	private const uint IN_MOVED_TO = 0x80;
	private const uint IN_CREATE = 0x100;
	private const uint IN_DELETE = 0x200;
	private const uint IN_DELETE_SELF = 0x400;
	private const uint IN_MOVE_SELF = 0x800;
	private const uint IN_Q_OVERFLOW = 0x4000;
	private const uint IN_IGNORED = 0x8000;
	private const uint IN_ONLYDIR = 0x1000000;

	//Everything that can happen to what is in a folder, and to the folder itself.
	private const uint Mask = IN_MODIFY | IN_ATTRIB | IN_CLOSE_WRITE | IN_MOVED_FROM | IN_MOVED_TO
		| IN_CREATE | IN_DELETE | IN_DELETE_SELF | IN_MOVE_SELF | IN_ONLYDIR;

	//The size of an event before its name.
	private const int Header = 16;

	private static readonly object gate = new();

	private static int fd = -1;

	//The instance could not be had, and has been said so once.
	private static bool unavailable;

	//Who listens to each watch. One folder asked for twice is one watch - the kernel hands the same one back - so a watch can have several.
	private static readonly Dictionary<int, SharedWatchers.IListener[]> listeners = new();

	private static readonly Dictionary<SharedWatchers.IListener, int> watches = new();

	//Starts a listener hearing what happens directly in its folder. False where this is not Linux and a watcher has to be had
	//some other way; true otherwise, even when there is nothing to hear - a folder that is not there, or no instance to be had.
	internal static bool Add( SharedWatchers.IListener listener )
	{
		if ( !OperatingSystem.IsLinux() ) return false;

		lock ( gate )
		{
			if ( watches.ContainsKey( listener ) ) return true;

			if ( !Open() ) return true;

			var path = listener.Folder.Length > 1 ? listener.Folder.TrimEnd( '/' ) : listener.Folder;

			//Not there, not a folder, or the user's watches spent: nothing to hear about, the same as a watcher that could not be made.
			var wd = inotify_add_watch( fd, path, Mask );
			if ( wd < 0 ) return true;

			watches[listener] = wd;
			listeners[wd] = listeners.TryGetValue( wd, out var had ) ? [.. had, listener] : [listener];

			return true;
		}
	}

	//Stops a listener hearing anything; the watch goes with its last listener.
	internal static void Remove( SharedWatchers.IListener listener )
	{
		lock ( gate )
		{
			if ( !watches.Remove( listener, out var wd ) ) return;
			if ( !listeners.TryGetValue( wd, out var had ) ) return;

			var left = had.Where( x => x != listener ).ToArray();

			if ( left.Length > 0 )
			{
				listeners[wd] = left;
				return;
			}

			listeners.Remove( wd );
			inotify_rm_watch( fd, wd );
		}
	}

	//The instance, and the thread reading it. Under the gate.
	private static bool Open()
	{
		if ( fd >= 0 ) return true;
		if ( unavailable ) return false;

		fd = inotify_init1( IN_CLOEXEC );

		if ( fd < 0 )
		{
			unavailable = true;

			Log.Warning( $"Can't watch folders for changes (inotify_init1: errno {Marshal.GetLastPInvokeError()}), so the editor " +
				"won't notice them. Raise fs.inotify.max_user_instances - the desktop holds most of the default 128." );

			return false;
		}

		new Thread( Read ) { IsBackground = true, Name = "Folder Watch" }.Start();

		return true;
	}

	private static void Read()
	{
		//A name is at most 255 bytes, so this holds a few hundred events at a time.
		var buffer = new byte[64 * 1024];

		while ( true )
		{
			var got = (int)read( fd, buffer, buffer.Length );

			if ( got < 0 )
			{
				var errno = Marshal.GetLastPInvokeError();
				if ( errno == EINTR ) continue;

				Log.Warning( $"Folder watching stopped (read: errno {errno}); changes to folders are not noticed from here on." );
				return;
			}

			var at = 0;

			while ( at + Header <= got )
			{
				var wd = MemoryMarshal.Read<int>( buffer.AsSpan( at ) );
				var mask = MemoryMarshal.Read<uint>( buffer.AsSpan( at + 4 ) );
				var length = MemoryMarshal.Read<int>( buffer.AsSpan( at + 12 ) );

				var name = buffer.AsSpan( at + Header, length );
				var end = name.IndexOf( (byte)0 );
				if ( end >= 0 ) name = name[..end];

				Happened( wd, mask, Encoding.UTF8.GetString( name ) );

				at += Header + length;
			}
		}
	}

	private static void Happened( int wd, uint mask, string name )
	{
		SharedWatchers.IListener[] told;

		if ( ( mask & IN_Q_OVERFLOW ) != 0 )
		{
			//More happened than the kernel kept: nobody knows what, so everybody is told something did.
			lock ( gate )
			{
				told = listeners.Values.SelectMany( x => x ).Distinct().ToArray();
			}

			foreach ( var listener in told ) Tell( () => listener.DeliverError( new System.IO.InternalBufferOverflowException() ) );

			return;
		}

		lock ( gate )
		{
			if ( !listeners.TryGetValue( wd, out told ) ) return;

			//The folder went, or moved - where it is now is not the folder that was asked about - and the watch with it.
			if ( ( mask & ( IN_DELETE_SELF | IN_MOVE_SELF | IN_IGNORED ) ) != 0 )
			{
				listeners.Remove( wd );

				foreach ( var listener in told ) watches.Remove( listener );

				if ( ( mask & IN_MOVE_SELF ) != 0 ) inotify_rm_watch( fd, wd );
			}
		}

		foreach ( var listener in told )
		{
			if ( ( mask & ( IN_DELETE_SELF | IN_MOVE_SELF ) ) != 0 )
			{
				Tell( () => listener.Deliver( Zio.WatcherChangeTypes.Deleted, listener.Folder.TrimEnd( '/' ), null ) );
				continue;
			}

			if ( ( mask & IN_IGNORED ) != 0 || name.Length == 0 ) continue;

			var type = ( mask & ( IN_CREATE | IN_MOVED_TO ) ) != 0 ? Zio.WatcherChangeTypes.Created
				: ( mask & ( IN_DELETE | IN_MOVED_FROM ) ) != 0 ? Zio.WatcherChangeTypes.Deleted
				: Zio.WatcherChangeTypes.Changed;

			var path = listener.Folder + name;

			Tell( () => listener.Deliver( type, path, null ) );
		}
	}

	private static void Tell( Action action )
	{
		try
		{
			action();
		}
		catch ( Exception e )
		{
			Log.Warning( e, "a folder watch's listener threw" );
		}
	}
}
