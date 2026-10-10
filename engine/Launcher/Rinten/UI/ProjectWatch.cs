using System.IO;
using System.Threading;

namespace Editor;

//Watches project folders and their parents via OS notifications so deleted projects drop off
internal sealed class ProjectWatch : IDisposable
{
	//What is being watched, by folder. Guarded by itself.
	private readonly Dictionary<string, IDisposable> watchers = new( StringComparer.OrdinalIgnoreCase );

	//Folders whose watch fell over and has to be put back.
	private readonly HashSet<string> lost = new( StringComparer.OrdinalIgnoreCase );

	//Whether something has happened that has not been acted on yet.
	private volatile bool pending;

	//When the last thing happened, as Environment.TickCount64.
	private long touched;

	//Quiet time in ms before rereading, so a burst of delete events causes one reread
	private const int Quiet = 250;

	//Cap on concurrent folder watches, since each uses an inotify watch slot
	private const int Most = 32;

	//Whether something changed and the disk has been still since. Asked once a frame and answered out of two fields - no folder
	//is opened and nothing is stat'ed to work it out. The reading of the disk happens after this says yes, and only then.
	public bool Ready => pending && Environment.TickCount64 - Interlocked.Read( ref touched ) >= Quiet;

	//Says the change has been dealt with.
	public void Taken() => pending = false;

	//Syncs watches to these folders: keeps existing, drops unwanted, arms new; missing ones are fine
	public void Follow( IEnumerable<string> folders )
	{
		var wanted = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var folder in folders )
		{
			if ( wanted.Count >= Most ) break;
			if ( string.IsNullOrWhiteSpace( folder ) ) continue;

			string full;

			try
			{
				full = Path.GetFullPath( folder );
			}
			catch ( Exception )
			{
				continue;
			}

			if ( !Directory.Exists( full ) ) continue;

			wanted.Add( full );
		}

		lock ( watchers )
		{
			//The ones that fell over are dropped here rather than in the handler that heard about it: a watcher
			//disposed from inside its own event is a watcher disposed on the thread it raises events on.
			foreach ( var folder in watchers.Keys.Where( x => !wanted.Contains( x ) || lost.Contains( x ) ).ToArray() )
			{
				Drop( folder );
			}

			lost.Clear();

			foreach ( var folder in wanted )
			{
				if ( watchers.ContainsKey( folder ) ) continue;

				Arm( folder );
			}
		}
	}

	public void Dispose()
	{
		lock ( watchers )
		{
			foreach ( var folder in watchers.Keys.ToArray() )
			{
				Drop( folder );
			}
		}
	}

	//Starts listening to one folder. Call under the lock.
	private void Arm( string folder )
	{
		//Names only and the folder itself, not contents; unwatchable folders just stay silent
		var self = folder.Replace( '\\', '/' ).TrimEnd( '/' );

		watchers[folder] = FileSystem.WatchFolder( folder, path =>
		{
			//The folder itself going, or the watch losing count: what is on the screen is out of date either way, and the
			//folder is armed again if it is still there - a whole checkouts folder deleted is every project under it gone.
			if ( string.Equals( path.TrimEnd( '/' ), self, StringComparison.OrdinalIgnoreCase ) )
			{
				lock ( watchers )
				{
					lost.Add( folder );
				}
			}

			Bump();
		} );
	}

	//Stops listening to one folder. Call under the lock.
	private void Drop( string folder )
	{
		if ( watchers.Remove( folder, out var watch ) ) watch.Dispose();
	}

	private void Bump()
	{
		Interlocked.Exchange( ref touched, Environment.TickCount64 );

		pending = true;
	}
}
