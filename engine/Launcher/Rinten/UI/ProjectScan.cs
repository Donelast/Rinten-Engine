using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Editor;

//Finds projects on disk the launcher wasn't told about; runs on a thread and publishes as it goes
internal sealed class ProjectScan
{
	//Where a project was found: made here (projects folder) or pulled down (checkouts folder)
	public enum FoundIn
	{
		/// <summary>The folder new projects are made in, and anything called that beside it.</summary>
		Projects,

		/// <summary>A checkouts folder - what has been cloned onto this machine.</summary>
		Github
	}

	//One thing found on the disk that is not in the list.
	public readonly record struct Found( string Path, string Title, string Folder, bool IsPort, FoundIn Source );

	//What has been found so far. Replaced whole, never edited in place.
	public IReadOnlyList<Found> Results => results;

	//Whether the walk is still going on.
	public bool Running { get; private set; }

	//Whether it has been asked for at all.
	public bool Asked { get; private set; }

	//Bumped whenever there is something new to show. The panel folds this into its build hash, which is how a
	//walk that finishes on some other thread ends up on the screen without anything having to be told about it.
	public int Revision { get; private set; }

	private volatile Found[] results = Array.Empty<Found>();

	//Somebody else's project, which is a project to convert.
	private const string SBox = ".sbproj";

	//The folders worth looking in, by name, and which column each one is. Matched without minding case rather than being spelt out
	//as paths: these are called github on one machine and GitHub on the next, and on Linux the difference is two different folders.
	private static readonly (string Name, FoundIn Source)[] Names =
		[("github", FoundIn.Github), ("rinten projects", FoundIn.Projects)];

	//Folders nothing worth showing is ever inside. templates is here because the engine's own checkout has a folder of project
	//files that are not projects - they are the files a new one is stamped out of, and every one of them would otherwise be offered.
	private static readonly string[] Skipped =
		[".git", ".vs", ".rinten", "bin", "obj", "node_modules", "packages", "templates"];

	//Two levels covers project files at a checkout's root or one in, and skips the engine's own
	private const int Depth = 2;

	//How many are worth showing. A cap rather than a page: past a screenful this stops being a list of what is on
	//the machine and starts being a directory listing, and the dialog is still there for anything it did not reach.
	private const int Most = 48;

	//Whether a walk is going on, as something to test and set.
	private int walking;

	//Whether another walk has been asked for since this one started.
	private volatile bool wanted;

	//Starts the walk, once.
	public void Start()
	{
		if ( Asked ) return;

		Again();
	}

	//Rescans after a disk change; a request during a walk queues one more walk instead of many
	public void Again()
	{
		Asked = true;
		Running = true;
		wanted = true;

		if ( Interlocked.CompareExchange( ref walking, 1, 0 ) != 0 ) return;

		_ = Task.Run( Loop );
	}

	private void Loop()
	{
		try
		{
			while ( wanted )
			{
				wanted = false;

				Walk();
			}
		}
		finally
		{
			Running = false;

			Interlocked.Exchange( ref walking, 0 );

			Revision++;
		}
	}

	private void Walk()
	{
		try
		{
			var found = new List<Found>();
			var folders = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

			foreach ( var (root, source) in Roots() )
			{
				Gather( root, 0, source, found, folders );

				//Published per root rather than at the end: the projects folder is small and immediate, and a checkouts
				//folder is not - so what is quick to find is on the screen while the slow one is still being walked.
				Publish( found );

				if ( found.Count >= Most ) break;
			}

			Publish( found );
		}
		catch ( Exception )
		{
			//A folder that cannot be read is a folder with nothing in it as far as this is concerned. There is nothing here worth stopping the launcher over.
		}
	}

	private void Publish( List<Found> found )
	{
		results = found.ToArray();
		Revision++;
	}

	//Where this looks, for anything that wants to hear about the same places. ProjectWatch watches these as well as the folders the listed projects
	//sit in: a project made or cloned into one of them is a card that appears on its own, without the walk having to be repeated on a timer to find it.
	public static IEnumerable<string> Places() => Roots().Select( x => x.Path );

	//Where to look: the folder new projects are made in, and anything under the home or documents folders called one of Names.
	private static IEnumerable<(string Path, FoundIn Source)> Roots()
	{
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var (root, source) in Candidates() )
		{
			if ( string.IsNullOrWhiteSpace( root ) ) continue;

			string full;

			try
			{
				full = Path.GetFullPath( root );
			}
			catch ( Exception )
			{
				continue;
			}

			if ( !seen.Add( full ) ) continue;
			if ( !Directory.Exists( full ) ) continue;

			yield return (full, source);
		}
	}

	private static IEnumerable<(string Path, FoundIn Source)> Candidates()
	{
		//Where the launcher puts one it makes, whatever that has been set to.
		yield return (LauncherPreferences.DefaultProjectLocation, FoundIn.Projects);

		var home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );
		var documents = Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments );

		foreach ( var under in new[] { home, documents } )
		{
			if ( string.IsNullOrWhiteSpace( under ) || !Directory.Exists( under ) ) continue;

			IEnumerable<string> inside;

			try
			{
				inside = Directory.EnumerateDirectories( under );
			}
			catch ( Exception )
			{
				continue;
			}

			foreach ( var directory in inside )
			{
				var name = Path.GetFileName( directory );

				foreach ( var (wanted, source) in Names )
				{
					if ( !string.Equals( name, wanted, StringComparison.OrdinalIgnoreCase ) ) continue;

					yield return (directory, source);
					break;
				}
			}
		}
	}

	private void Gather( string directory, int depth, FoundIn source, List<Found> found, HashSet<string> folders )
	{
		if ( found.Count >= Most ) return;

		foreach ( var file in Files( directory ) )
		{
			if ( found.Count >= Most ) return;

			var port = file.EndsWith( SBox, StringComparison.OrdinalIgnoreCase );

			if ( !port && !file.EndsWith( Project.Extension, StringComparison.OrdinalIgnoreCase ) ) continue;

			//One card per folder. A checkout with two project files in it - the game and a library beside
			//it - is one thing somebody has, and the second card would only be the same folder said twice.
			if ( !folders.Add( directory ) ) continue;

			found.Add( new Found( file, Titled( file, directory ), directory, port, source ) );
		}

		if ( depth >= Depth ) return;

		foreach ( var inside in Directories( directory ) )
		{
			var name = Path.GetFileName( inside );

			if ( name.StartsWith( '.' ) ) continue;
			if ( Skipped.Contains( name, StringComparer.OrdinalIgnoreCase ) ) continue;

			Gather( inside, depth + 1, source, found, folders );
		}
	}

	private static IEnumerable<string> Files( string directory )
	{
		try
		{
			return Directory.EnumerateFiles( directory );
		}
		catch ( Exception )
		{
			return Array.Empty<string>();
		}
	}

	private static IEnumerable<string> Directories( string directory )
	{
		try
		{
			return Directory.EnumerateDirectories( directory );
		}
		catch ( Exception )
		{
			return Array.Empty<string>();
		}
	}

	//Project title read as loose json, falling back to the folder name if it won't parse
	private static string Titled( string file, string directory )
	{
		try
		{
			using var document = JsonDocument.Parse( File.ReadAllText( file ) );

			if ( document.RootElement.ValueKind == JsonValueKind.Object &&
				document.RootElement.TryGetProperty( "Title", out var title ) &&
				title.ValueKind == JsonValueKind.String &&
				title.GetString() is { Length: > 0 } written )
			{
				return written;
			}
		}
		catch ( Exception )
		{
		}

		return Path.GetFileName( directory );
	}
}
