using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Editor.CodeEditors;

[Title( "Visual Studio Code" )]
public class VisualStudioCode : ICodeEditor
{
	public void OpenFile( string path, int? line, int? column )
	{
		var sln = CodeEditorPaths.SolutionFor( path );
		var rootPath = sln is null ? null : Path.GetDirectoryName( sln );

		//Both numbers or neither: `file:12:` is a path VS Code does not find.
		var goTo = $"-g \"{path}:{line ?? 1}:{column ?? 1}\"";

		Launch( rootPath is null ? goTo : $"{goTo} \"{rootPath}\"" );
		CodeEditorPaths.BringToFront( "code", "codium" );
	}

	public void OpenSolution()
	{
		Launch( $"\"{Project.Current.GetRootPath()}\"" );
	}

	public void OpenAddon( Project addon )
	{
		var projectPath = (addon != null) ? addon.GetRootPath() : "";

		Launch( $"\"{projectPath}\"" );
	}

	public bool IsInstalled() => !string.IsNullOrEmpty( GetLocation() );

	private static void Launch( string arguments )
	{
		var startInfo = new System.Diagnostics.ProcessStartInfo
		{
			FileName = GetLocation(),
			Arguments = arguments,
			CreateNoWindow = true,
		};

		System.Diagnostics.Process.Start( startInfo );
	}

	static string Location;

	private static string GetLocation()
	{
		if ( Location != null )
		{
			return Location;
		}

		//No registry off Windows, and the launcher is on PATH there.
		if ( !OperatingSystem.IsWindows() )
			return Location = CodeEditorPaths.OnPath( "code", "code-insiders", "codium" );

		return Location = FromRegistry();
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage( "Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>" )]
	private static string FromRegistry()
	{
		string value = null;
		using ( var key = Registry.ClassesRoot.OpenSubKey( @"Applications\\Code.exe\\shell\\open\\command" ) )
		{
			value = key?.GetValue( "" ) as string;
		}

		if ( value == null )
		{
			return null;
		}

		//Given `"C:\Program Files\Microsoft VS Code\Code.exe" "%1"` grab the first bit
		Regex rgx = new Regex( "\"(.*)\" \".*\"", RegexOptions.IgnoreCase );
		var matches = rgx.Matches( value );
		if ( matches.Count == 0 || matches[0].Groups.Count < 2 )
		{
			return null;
		}

		return matches[0].Groups[1].Value;
	}
}
