using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Rinten;

//Writes resource files off the frame thread (temp, flush, rename); one ordered chain per file
internal static class ResourceWrites
{
	private static readonly Logger log = new( "Resources" );

	private static readonly object padlock = new();

	//The last write asked for on each file, so the next one follows it.
	private static readonly Dictionary<string, Task> chains = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Queues a write and returns. Ordered against other writes of the same file.</summary>
	public static void Queue( string filename, byte[] contents, Action<string, byte[]> write )
	{
		if ( string.IsNullOrWhiteSpace( filename ) || contents is null || write is null ) return;

		lock ( padlock )
		{
			var previous = chains.GetValueOrDefault( filename ) ?? Task.CompletedTask;

			var next = previous.ContinueWith( _ =>
			{
				try
				{
					write( filename, contents );
				}
				catch ( Exception e )
				{
					//The one thing a background write must not do is take the editor down with it. Said out
					//loud instead, because a file that quietly did not get written is the worst outcome here.
					log.Warning( e, $"Could not write {filename}" );
				}
			}, TaskScheduler.Default );

			chains[filename] = next;
		}
	}

	//Called where the answer has to be on the disk before anything else happens: the editor closing, a project being packaged, a game being started from what was just edited. Nothing else waits - that is the whole point of the queue.
	/// <summary>Waits for every queued write to land.</summary>
	public static void Flush()
	{
		Task[] waiting;

		lock ( padlock )
		{
			waiting = chains.Values.ToArray();
			chains.Clear();
		}

		if ( waiting.Length == 0 ) return;

		try
		{
			Task.WaitAll( waiting, TimeSpan.FromSeconds( 30 ) );
		}
		catch ( Exception e )
		{
			log.Warning( e, "Some resource writes did not finish" );
		}
	}

	/// <summary>Whether anything is still on its way to the disk.</summary>
	public static bool Pending
	{
		get
		{
			lock ( padlock )
			{
				foreach ( var task in chains.Values )
				{
					if ( !task.IsCompleted ) return true;
				}

				return false;
			}
		}
	}
}
