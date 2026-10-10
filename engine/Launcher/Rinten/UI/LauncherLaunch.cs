using System.Diagnostics;
using System.IO;

namespace Editor;

//Starts another editor process, only for other engine versions and ports
internal static class LauncherLaunch
{
	//Resolves the program to a runnable path next to this one; Linux won't search the working dir
	private static string Executable( string wanted )
	{
		if ( !string.IsNullOrWhiteSpace( wanted ) && Path.IsPathRooted( wanted ) ) return wanted;

		var running = Environment.ProcessPath;

		if ( string.IsNullOrEmpty( running ) ) return wanted;

		var directory = Path.GetDirectoryName( running );

		if ( !string.IsNullOrEmpty( directory ) && !string.IsNullOrWhiteSpace( wanted ) )
		{
			var beside = Path.Combine( directory, wanted );

			if ( File.Exists( beside ) ) return beside;
		}

		//Whatever is running now, which for the editor's own name is the same file under another spelling.
		return running;
	}

	private static ProcessStartInfo Info( string exe ) => new( exe )
	{
		UseShellExecute = false,
		CreateNoWindow = true,
		WorkingDirectory = Path.IsPathRooted( exe )
			? Path.GetDirectoryName( exe )
			: Environment.CurrentDirectory,
	};

	//Starts an installed engine's editor, which opens on its own launcher
	internal static void StartEditor( string exe )
	{
		if ( string.IsNullOrWhiteSpace( exe ) ) return;

		Process.Start( Info( exe ) );
	}

	//Starts an editor with -port that reads one project, writes another and quits
	internal static Process StartPort( string sourceProjectFile, string destination )
	{
		if ( string.IsNullOrWhiteSpace( sourceProjectFile ) ) return null;
		if ( string.IsNullOrWhiteSpace( destination ) ) return null;

		var exe = Executable( EngineExecutables.EditorFile );

		var info = Info( exe );

		info.ArgumentList.Add( "-project" );
		info.ArgumentList.Add( sourceProjectFile );
		info.ArgumentList.Add( "-port" );
		info.ArgumentList.Add( destination );

		//No install path is passed. The porter goes and finds the installed game itself -
		//see SBoxBaseContent.Find - which is the only place the content it reads actually is.

		//Not headless, so the port shows a progress bar while it runs

		return Process.Start( info );
	}

	//Opens a project in the editor of the engine version it is pinned to.
	internal static void Start( Project project )
	{
		if ( project is null ) return;

		var exe = Executable( EngineVersions.ResolveExeForProject( project ) ?? EngineExecutables.EditorFile );

		var info = Info( exe );

		info.ArgumentList.Add( "-project" );
		info.ArgumentList.Add( project.ConfigFilePath );

		Process.Start( info );
	}
}
