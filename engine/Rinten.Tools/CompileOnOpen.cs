using Rinten.Diagnostics;
using System;

namespace Editor;

//Guards asset compiles during project open: failures are logged, not fatal; models are deferred
internal static class CompileOnOpen
{
	[ThreadStatic] static int depth;

	static readonly object gate = new();
	static readonly Queue<(string Path, Func<bool> Compile)> leftover = new();

	//What is on the queue, so a file that changes twice before its turn is built once.
	static readonly HashSet<string> queued = new( StringComparer.OrdinalIgnoreCase );
	static int leftoverTotal;
	static int skipFrames;

	//Whether a compile should wait: everything while opening, then only models while the queue drains
	internal static bool Defer( string path )
	{
		if ( StartupLoadProject.IsLoading ) return true;
		if ( !IsModel( path ) ) return false;

		lock ( gate ) return leftover.Count > 0;
	}

	//How many models are waiting for a frame after the project is open.
	internal static int Pending
	{
		get { lock ( gate ) return leftover.Count; }
	}

	//A model is minutes of FBX. Everything else can compile while opening.
	internal static bool IsModel( string path )
		=> ( path ?? "" ).EndsWith( ".mdl", StringComparison.OrdinalIgnoreCase );

	//Runs one compile. True if it returned true. A throw is a failed compile.
	internal static bool One( string path, Func<bool> compile )
	{
		var name = string.IsNullOrWhiteSpace( path ) ? "(no path)" : path;

		if ( depth > 0 )
		{
			try { return compile(); }
			catch ( Exception e )
			{
				StartupLoadProject.Log.Error( e, $"compiling {name} threw - opening carried on" );
				return false;
			}
		}

		depth++;

		try
		{
			StartupLoadProject.Log.Info( $"Compiling {name}" );
			Logging.Flush();

			return compile();
		}
		catch ( Exception e )
		{
			StartupLoadProject.Log.Error( e, $"compiling {name} threw - opening carried on" );
			return false;
		}
		finally
		{
			depth--;
		}
	}

	//Models to compile on later frames: the ones the opening pass left, and any that arrive or change
	//while the editor is open. Whoever shows one is told once it is built - see AssetSystem.Rebuilt.
	internal static void Enqueue( IEnumerable<Asset> assets )
	{
		foreach ( var asset in assets )
		{
			if ( asset is null ) continue;

			Enqueue( asset.Path, () =>
			{
				var built = asset.Compile( false );
				if ( built ) AssetSystem.Rebuilt( asset );
				return built;
			} );
		}
	}

	//One compile for a later frame. Used by the editor and by tests.
	internal static void Enqueue( string path, Func<bool> compile )
	{
		lock ( gate )
		{
			if ( path is not null && !queued.Add( path ) ) return;

			if ( leftover.Count == 0 )
				skipFrames = 2;

			leftover.Enqueue( ( path, compile ) );
			leftoverTotal++;
		}
	}

	//True if anything is still queued after this one.
	internal static bool PumpOne()
	{
		(string Path, Func<bool> Compile) item;
		int remaining;
		var finished = 0;

		lock ( gate )
		{
			if ( leftover.Count == 0 ) return false;

			item = leftover.Dequeue();
			if ( item.Path is not null ) queued.Remove( item.Path );
			remaining = leftover.Count;
			if ( remaining == 0 )
			{
				finished = leftoverTotal;
				leftoverTotal = 0;
			}
		}

		One( item.Path, item.Compile );

		if ( remaining == 0 && finished > 0 )
			StartupLoadProject.Log.Info( $"Queued models built ({finished})" );

		return remaining > 0;
	}

	[EditorEvent.Frame]
	internal static void Frame()
	{
		if ( StartupLoadProject.IsLoading ) return;

		lock ( gate )
		{
			if ( leftover.Count == 0 ) return;
			if ( skipFrames > 0 )
			{
				skipFrames--;
				return;
			}
		}

		//One queued model per frame, regardless of user input
		PumpOne();
		Rinten.Engine.IToolsDll.Current?.Spin();
	}

	//Tests leave nothing in the queue for the next one.
	internal static void ClearLeftover()
	{
		lock ( gate )
		{
			leftover.Clear();
			queued.Clear();
			leftoverTotal = 0;
			skipFrames = 0;
		}
	}
}
