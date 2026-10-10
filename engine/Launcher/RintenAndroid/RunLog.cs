using System;
using System.IO;
using System.Threading;
using Android.Content;

//System has a Uri of its own, and what a content resolver takes is Android's.
using AndroidUri = global::Android.Net.Uri;

namespace Rinten.Android;

//Live run log copied to Downloads/Rinten/last-run.txt; an unfinished run is kept as a crash report
internal sealed class RunLog
{
	//This run's log, in the app's own folder.
	private const string Live = "run.log";

	//There while a run is going. Still there when the next one starts means that run died.
	private const string Going = "run.going";

	//Where this install's copy among the downloads is, so every run writes the same one.
	private const string Where = "run.where";

	//What the copy is called.
	private const string Name = "last-run.txt";

	//The least time between two copies while the engine is talking.
	private static readonly TimeSpan Apart = TimeSpan.FromSeconds( 1 );

	private readonly Context context;
	private readonly string folder;
	private readonly StreamWriter writer;
	private readonly Thread thread;
	private readonly AutoResetEvent wake = new( false );

	private AndroidUri copy;
	private DateTime copied = DateTime.MinValue;
	private volatile bool stop;

	private RunLog( Context context, string folder, StreamWriter writer, AndroidUri copy )
	{
		this.context = context;
		this.folder = folder;
		this.writer = writer;
		this.copy = copy;

		//Every line of the engine's, on the thread that said it - see Logging.Tap.
		Rinten.Diagnostics.Logging.Tap += Written;

		thread = new Thread( Keep ) { Name = "rinten-log", IsBackground = true, Priority = ThreadPriority.BelowNormal };
		thread.Start();
	}

	//Keeps what the last run left if it died, and opens this run's log. Null where there is nowhere to write one; the game runs the same either way.
	public static RunLog Open( Context context, string header )
	{
		StreamWriter writer = null;

		try
		{
			var folder = context?.FilesDir?.AbsolutePath;
			if ( folder is null ) return null;

			var live = Path.Combine( folder, Live );
			var going = Path.Combine( folder, Going );

			//The last run never said it was done. Its log is the only account of how it ended, and the copy below is about to be written over.
			if ( File.Exists( going ) && File.Exists( live ) )
			{
				var when = File.GetLastWriteTime( live );
				Downloads.Write( context, $"crash-{when:yyyyMMdd-HHmmss}.txt", File.ReadAllText( live ) );
			}

			writer = new StreamWriter( live, append: false ) { AutoFlush = true };
			writer.Write( header );

			File.WriteAllText( going, "" );

			//The log owns the file from here.
			var log = new RunLog( context, folder, writer, Remembered( context, folder ) );
			writer = null;

			return log;
		}
		catch ( Exception e )
		{
			global::Android.Util.Log.Error( "rinten", $"no run log: {e.Message}" );
			return null;
		}
		finally
		{
			writer?.Dispose();
		}
	}

	//A line of the launcher's own - what the engine's log knows nothing about. Copied out at once:
	//there are a handful of these, and they are the ones either side of whatever ends a start.
	public void Say( string line )
	{
		global::Android.Util.Log.Info( "rinten", line );

		Append( line );
		CopyOut();
	}

	//Says how the run ended, and marks it as one that did. Once; a second call does nothing.
	public void Close( string how )
	{
		if ( stop ) return;

		stop = true;

		Rinten.Diagnostics.Logging.Tap -= Written;

		Append( $"--- {how} ---" );
		CopyOut();

		try
		{
			lock ( writer ) writer.Dispose();
			File.Delete( Path.Combine( folder, Going ) );
		}
		catch ( Exception )
		{
		}

		wake.Set();
	}

	//One of the engine's lines.
	private void Written( LogEvent e )
	{
		Append( $"{e.Time:HH:mm:ss} {e.Level,-5} {e.Logger}: {e.Message}" );
		wake.Set();
	}

	private void Append( string line )
	{
		try
		{
			lock ( writer ) writer.WriteLine( line );
		}
		catch ( Exception )
		{
			//Closed, or the disk is full: a log is not worth the game.
		}
	}

	//Copies the file out when the engine has said something, and not more often than Apart.
	private void Keep()
	{
		while ( true )
		{
			wake.WaitOne();

			if ( stop ) return;

			var since = DateTime.UtcNow - copied;
			if ( since < Apart ) Thread.Sleep( Apart - since );

			if ( stop ) return;

			CopyOut();
		}
	}

	//The log as it stands, into the copy among the downloads.
	private void CopyOut()
	{
		if ( copy is null ) return;

		string text;

		try
		{
			using var stream = new FileStream( Path.Combine( folder, Live ), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete );
			using var reader = new StreamReader( stream );

			text = reader.ReadToEnd();
		}
		catch ( Exception )
		{
			return;
		}

		lock ( wake )
		{
			//The copy went - the player deleted it, or it was made by an install before this one and is no longer ours to write: a new one, remembered.
			if ( !Downloads.Put( context, copy, text ) )
			{
				copy = Made( context, folder );
				Downloads.Put( context, copy, text );
			}

			copied = DateTime.UtcNow;
		}
	}

	//The copy this install writes to, made the first time.
	private static AndroidUri Remembered( Context context, string folder )
	{
		var where = Path.Combine( folder, Where );

		try
		{
			if ( File.Exists( where ) ) return AndroidUri.Parse( File.ReadAllText( where ).Trim() );
		}
		catch ( Exception )
		{
		}

		return Made( context, folder );
	}

	private static AndroidUri Made( Context context, string folder )
	{
		var made = Downloads.Create( context, Name );

		try
		{
			if ( made is not null ) File.WriteAllText( Path.Combine( folder, Where ), made.ToString() );
		}
		catch ( Exception )
		{
		}

		return made;
	}
}
