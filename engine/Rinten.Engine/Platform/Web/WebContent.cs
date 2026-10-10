using System.IO;

namespace Rinten;

//Puts a web build's files in the runtime's in-memory filesystem so System.IO works as on desktop
internal static class WebContent
{
	//Where the fetched files are, in the runtime's filesystem.
	internal const string Root = "/content";

	private static LocalFileSystem files;

	//The filesystem the fetched files are in.
	internal static BaseFileSystem FileSystem => files ??= new LocalFileSystem( Root );

	//How many files were fetched.
	internal static int Count { get; private set; }

	//Puts one fetched file where the engine will look for it.
	internal static void Add( string path, byte[] bytes )
	{
		if ( string.IsNullOrEmpty( path ) || bytes is not { Length: > 0 } ) return;

		File.WriteAllBytes( Placed( path ), bytes );
		Count++;
	}

	//Fills a buffer with a file's bytes - the page copying in what it fetched.
	internal delegate void Fill( Span<byte> into );

	//Puts one fetched file in place through a buffer outside the collector's heap, which fill writes - see Fetch.
	internal static unsafe void Place( string path, int length, Fill fill )
	{
		var buffer = System.Runtime.InteropServices.NativeMemory.Alloc( (nuint)Math.Max( length, 1 ) );

		try
		{
			var bytes = new Span<byte>( buffer, length );
			fill( bytes );

			using var file = File.Create( Placed( path ) );
			file.Write( bytes );
			Count++;
		}
		finally
		{
			System.Runtime.InteropServices.NativeMemory.Free( buffer );
		}
	}

	//Where a fetched file goes, its folder made.
	internal static string Placed( string path )
	{
		var full = Path.Combine( Root, path.Replace( '\\', '/' ).TrimStart( '/' ) );

		Directory.CreateDirectory( Path.GetDirectoryName( full ) );
		return full;
	}

	//Set by the page: fetches an on-demand file and writes it in place; true on success, null off web
	internal static Func<string, Task<bool>> Fetch { get; set; }

	private static readonly HashSet<string> onDemand = new( StringComparer.OrdinalIgnoreCase );
	private static readonly Dictionary<string, List<Action>> waiting = new( StringComparer.OrdinalIgnoreCase );
	private static readonly Queue<string> requested = new();
	private static bool fetching;

	//Files the build carries that are fetched when first asked for - see WebBuild.OnDemand.
	internal static void OnDemand( IEnumerable<string> paths )
	{
		foreach ( var path in paths )
			onDemand.Add( Normal( path ) );
	}

	//Whether path is a file the build carries and has not fetched yet - and if so, asks for it. arrived runs once it is here. One fetch at a
	//time: the page hands a fetched file over through one slot, and what asks for these - a face of a font - asks for a few, never hundreds.
	internal static bool Request( string path, Action arrived )
	{
		var key = Normal( path );
		if ( !onDemand.Contains( key ) || Has( key ) || Fetch is null ) return false;

		if ( !waiting.TryGetValue( key, out var list ) )
		{
			waiting[key] = list = new();
			requested.Enqueue( key );
		}

		if ( arrived is not null ) list.Add( arrived );

		if ( !fetching ) _ = Pump();
		return true;
	}

	private static async Task Pump()
	{
		fetching = true;

		try
		{
			while ( requested.TryDequeue( out var key ) )
			{
				var came = false;

				try
				{
					came = await Fetch( key );
				}
				catch ( Exception e )
				{
					Log.Warning( $"{key} could not be fetched: {e.Message}" );
				}

				//Taken off the list either way, so a file that will not come is not asked for every frame for ever.
				onDemand.Remove( key );

				if ( waiting.Remove( key, out var list ) )
				{
					foreach ( var arrived in list )
						arrived();
				}
			}
		}
		finally
		{
			fetching = false;
		}
	}

	//Asks for every on-demand file under folder. True while any of them is still to come; false off a page, and once they are all here. For an asset
	//that is a folder of files - a model, its materials, its pictures - and is loaded by asking again until it loads, as a controller's model is.
	internal static bool RequestUnder( string folder )
	{
		if ( onDemand.Count == 0 ) return false;

		var prefix = Normal( folder ).TrimEnd( '/' ) + "/";
		var coming = false;

		foreach ( var path in onDemand.Where( x => x.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) ).ToArray() )
			coming |= Request( path, null );

		return coming || waiting.Keys.Any( x => x.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) );
	}

	private static string Normal( string path ) => path.Replace( '\\', '/' ).TrimStart( '/' );

	private static readonly HashSet<string> packed = new( StringComparer.OrdinalIgnoreCase );
	private static readonly Dictionary<string, List<string>> scenes = new( StringComparer.OrdinalIgnoreCase );

	//Files the build carries packed - see WebBuild.Packed.
	internal static void Packed( IEnumerable<string> paths )
	{
		foreach ( var path in paths ) packed.Add( Normal( path ) );
	}

	//What the page is asked for to fetch a file: its name, marked where it is packed.
	internal static string Asked( string path )
		=> packed.Contains( Normal( path ) ) ? $"{WebBuild.Packed}{Normal( path )}" : Normal( path );

	//Which on-demand files each scene needs, out of WebBuild.SceneListName's text.
	internal static void ForScenes( string text )
	{
		if ( string.IsNullOrEmpty( text ) ) return;

		foreach ( var line in text.Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			var tab = line.IndexOf( '\t' );
			if ( tab <= 0 ) continue;

			var scene = Normal( line[..tab] );
			if ( !scenes.TryGetValue( scene, out var files ) ) scenes[scene] = files = new();
			files.Add( Normal( line[( tab + 1 )..] ) );
		}
	}

	//Fetches missing files for a scene, then reruns load; true if it had to wait, false if all present
	internal static bool ForScene( string scene, Action load )
	{
		if ( scenes.Count == 0 || string.IsNullOrEmpty( scene ) || load is null ) return false;
		if ( !scenes.TryGetValue( Normal( scene ), out var files ) ) return false;

		//Only what is still to come: a file whose fetch failed is off the list, and
		//the load goes ahead without it and says so - rather than waiting for it again.
		var missing = files.Where( x => !Has( x ) && onDemand.Contains( Normal( x ) ) ).ToList();
		if ( missing.Count == 0 ) return false;

		var left = missing.Count;
		ScenesWaiting++;
		Log.Info( $"{scene}: waiting for {left} files before it loads" );

		foreach ( var file in missing )
		{
			//Gone for good where it will not come: the load runs anyway, and says so itself.
			if ( !Request( file, Arrived ) ) Arrived();
		}

		return true;

		void Arrived()
		{
			if ( --left > 0 ) return;
			ScenesWaiting--;
			load();
		}
	}

	//Done once what scene needs is here: for a caller that runs more than the load itself - the page starting
	//its first scene, whose startup and camera come after the load and would otherwise find an empty scene.
	internal static Task SceneReady( string scene )
	{
		var ready = new TaskCompletionSource();
		return ForScene( scene, ready.SetResult ) ? ready.Task : Task.CompletedTask;
	}

	//Scenes asked to load that are still waiting for their files - a run's measurement starts after them.
	internal static int ScenesWaiting { get; private set; }

	//Every file only other scenes need, asked for now - one at a time, between frames - so that a scene the player opens later
	//has mostly arrived by then. The fonts and the headset's controllers are left on demand: those are many and few are wanted.
	internal static void FetchTheRest()
	{
		foreach ( var files in scenes.Values )
			foreach ( var file in files )
				Request( file, null );
	}

	//Whether a fetched file is there.
	internal static bool Has( string path )
		=> !string.IsNullOrEmpty( path ) && File.Exists( Path.Combine( Root, path.Replace( '\\', '/' ).TrimStart( '/' ) ) );

	//Mounts what was fetched, once the engine has a filesystem to mount it on.
	internal static void Mount()
	{
		EngineFileSystem.Mounted?.Mount( FileSystem );
		EngineFileSystem.Assets?.Mount( FileSystem );
		EngineFileSystem.CoreContent?.Mount( FileSystem );

		//The project's settings travel with the build like anything else, and one of them is
		//the input actions - which is what a game means when it asks whether "jump" is down.
		if ( FileSystem.DirectoryExists( "ProjectSettings" ) )
			EngineFileSystem.ProjectSettings = FileSystem.CreateSubSystem( "ProjectSettings" );

		Input.ReadConfig( ProjectSettings.Input );

		//And the fonts the build carries - the base addon's, the project's own - which a
		//desktop game takes up as each package mounts; a page mounts them here, all at once.
		FontManager.Instance.LoadAll( FileSystem );
	}
}
