using System.IO;
using System.Threading;

namespace Rinten;

//Records files a build reads (like a depfile) so editing any of them triggers a rebuild
internal static class ImportReads
{
	private static readonly AsyncLocal<Recording> current = new();

	//One build's reads.
	internal sealed class Recording : IDisposable
	{
		private readonly Recording outer;
		private readonly object gate = new();
		private readonly HashSet<string> files = new( StringComparer.Ordinal );
		private readonly Dictionary<string, string> keyed = new( StringComparer.Ordinal );

		internal Recording( Recording outer ) => this.outer = outer;

		/// <summary>Files read whole: any change to one is a change to the build.</summary>
		public IReadOnlyCollection<string> Files
		{
			get { lock ( gate ) return files.ToArray(); }
		}

		/// <summary>Files of which a few fields were read, with what those fields said - the rest of such a file can change without the build changing.</summary>
		public IReadOnlyDictionary<string, string> Keyed
		{
			get { lock ( gate ) return new Dictionary<string, string>( keyed, StringComparer.Ordinal ); }
		}

		internal void AddFile( string path )
		{
			lock ( gate ) files.Add( path );
		}

		internal void AddKeyed( string path, string key )
		{
			lock ( gate )
			{
				//Two reads of one file for different fields are both of them.
				keyed[path] = keyed.TryGetValue( path, out var had ) && had != key ? $"{had}\n{key}" : key;
			}
		}

		public void Dispose() => current.Value = outer;
	}

	/// <summary>Start writing down what is read, until the returned recording is disposed.</summary>
	public static Recording Record()
	{
		var recording = new Recording( current.Value );
		current.Value = recording;

		return recording;
	}

	/// <summary>A file read whole.</summary>
	public static void File( string path )
	{
		if ( current.Value is not { } recording || string.IsNullOrWhiteSpace( path ) ) return;

		recording.AddFile( Full( path ) );
	}

	/// <summary>A file of which only what <paramref name="key"/> says was used - a material read for where it cuts, which its colour changing does not touch.</summary>
	public static void Keyed( string path, string key )
	{
		if ( current.Value is not { } recording || string.IsNullOrWhiteSpace( path ) ) return;

		recording.AddKeyed( Full( path ), key ?? "" );
	}

	private static string Full( string path )
	{
		try
		{
			return Path.GetFullPath( path ).Replace( '\\', '/' );
		}
		catch ( Exception )
		{
			return path.Replace( '\\', '/' );
		}
	}
}
