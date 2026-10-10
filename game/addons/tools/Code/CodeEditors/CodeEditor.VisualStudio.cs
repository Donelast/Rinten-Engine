using System;

namespace Editor.CodeEditors;

[Title( "Visual Studio" )]
public class VisualStudio : ICodeEditor
{
	public void OpenFile( string path, int? line, int? column )
	{
		//The one that is already open, told directly - and it is the only way the line survives.
		if ( OperatingSystem.IsWindows() && VisualStudioInstance.TryOpenFile( path, line, column, CodeEditorPaths.SolutionFor( path ) ) )
			return;

		//Nothing running to tell: /edit starts one, and devenv takes no line on its command line.
		Launch( FindVisualStudio(), $"/edit \"{path}\"" );
	}

	public void OpenSolution()
	{
		var sln = CodeEditor.AddonSolutionPath();

		if ( OperatingSystem.IsWindows() && VisualStudioInstance.TryFocusSolution( sln ) )
			return;

		Launch( FindVisualStudio(), $"\"{sln}\"" );
	}

	public void OpenAddon( Project addon )
	{
		OpenSolution();
	}

	public bool IsInstalled() => !string.IsNullOrEmpty( FindVisualStudio() );

	private static void Launch( string exe, string arguments )
	{
		if ( string.IsNullOrEmpty( exe ) )
			return;

		var startInfo = new System.Diagnostics.ProcessStartInfo
		{
			CreateNoWindow = true,
			Arguments = arguments,
			FileName = exe
		};

		System.Diagnostics.Process.Start( startInfo );
	}

	static string VisualStudioPath;

	//Uses vswhere (https://github.com/microsoft/vswhere) to find where Visual Studio is
	//installed. This will return the most latest version, as well as one with .NET SDK installed.
	static string FindVisualStudio()
	{
		//Only an answer is kept: Visual Studio can be installed or opened later in the session.
		if ( !string.IsNullOrEmpty( VisualStudioPath ) )
			return VisualStudioPath;

		if ( !OperatingSystem.IsWindows() )
			return "";

		//Always use the same one that's already open
		foreach ( var p in System.Diagnostics.Process.GetProcessesByName( "devenv" ) )
		{
			//One running as somebody else: its modules are not ours to read.
			try
			{
				VisualStudioPath = p.MainModule.FileName;
			}
			catch ( Exception )
			{
				continue;
			}

			return VisualStudioPath;
		}

		if ( FindVsWhere() is not { } vswhere )
			return "";

		//Otherwise use vswhere to find the latest one with .NET installed (prerelease is valid too)
		var startInfo = new System.Diagnostics.ProcessStartInfo
		{
			CreateNoWindow = true,
			Arguments = "-latest -prerelease -requires Microsoft.NetCore.Component.SDK -property productPath",
			FileName = vswhere,
			RedirectStandardOutput = true
		};

		try
		{
			var process = System.Diagnostics.Process.Start( startInfo );
			VisualStudioPath = process.StandardOutput.ReadToEnd().Trim();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't ask vswhere where Visual Studio is: {e.Message}" );
			return "";
		}

		return VisualStudioPath;
	}

	//Visual Studio's own copy of vswhere, which its installer puts at a fixed place for exactly this.
	static string FindVsWhere()
	{
		foreach ( var root in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles } )
		{
			var folder = Environment.GetFolderPath( root );

			if ( string.IsNullOrEmpty( folder ) )
				continue;

			var path = System.IO.Path.Combine( folder, "Microsoft Visual Studio", "Installer", "vswhere.exe" );

			if ( System.IO.File.Exists( path ) )
				return path;
		}

		return null;
	}
}
