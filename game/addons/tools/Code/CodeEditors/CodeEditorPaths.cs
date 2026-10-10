using System;

namespace Editor.CodeEditors;

//What the code editors need to find things, where each of them was doing it for itself.
internal static class CodeEditorPaths
{
	/// <summary>The first of these executables on PATH, or null - how an editor is found where there is no registry to ask.</summary>
	public static string OnPath( params string[] names )
	{
		var path = Environment.GetEnvironmentVariable( "PATH" );

		if ( string.IsNullOrEmpty( path ) )
			return null;

		foreach ( var directory in path.Split( System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries ) )
		{
			foreach ( var name in names )
			{
				var full = System.IO.Path.Combine( directory.Trim(), name );

				if ( System.IO.File.Exists( full ) )
					return full;
			}
		}

		return null;
	}

	/// <summary>The solution this file belongs to, or null - FindSolutionFromPath throws for a file outside every project.</summary>
	public static string SolutionFor( string path )
	{
		try
		{
			return CodeEditor.FindSolutionFromPath( System.IO.Path.GetDirectoryName( path ) );
		}
		catch ( Exception )
		{
			return null;
		}
	}

	/// <summary>Raises the code editor's window after a file was handed to it.</summary>
	public static void BringToFront( params string[] windowClasses )
	{
		if ( !OperatingSystem.IsLinux() ) return;
		if ( Environment.GetEnvironmentVariable( "XDG_CURRENT_DESKTOP" ) is not { } desktop || !desktop.Contains( "KDE", StringComparison.OrdinalIgnoreCase ) ) return;
		if ( OnPath( "busctl" ) is not { } busctl ) return;

		var classes = windowClasses.Where( x => !string.IsNullOrWhiteSpace( x ) ).Select( x => x.ToLowerInvariant() ).ToArray();
		if ( classes.Length == 0 ) return;

		_ = Task.Run( async () =>
		{
			var script = System.IO.Path.Combine( System.IO.Path.GetTempPath(), $"rinten-raise-{Environment.ProcessId}.js" );

			try
			{
				//let the editor take the file first, or the window raised is the old one
				await Task.Delay( 400 );

				var list = string.Join( ",", classes.Select( x => $"\"{x.Replace( "\\", "" ).Replace( "\"", "" )}\"" ) );

				System.IO.File.WriteAllText( script,
					$"var want = [{list}];\n" +
					"var all = workspace.windowList ? workspace.windowList() : workspace.clientList();\n" +
					"for ( var i = 0; i < all.length; i++ ) {\n" +
					"  var c = (all[i].resourceClass || '').toString().toLowerCase();\n" +
					"  for ( var j = 0; j < want.length; j++ ) {\n" +
					"    if ( c.indexOf( want[j] ) < 0 ) continue;\n" +
					"    if ( workspace.windowList ) workspace.activeWindow = all[i]; else workspace.activeClient = all[i];\n" +
					"    break;\n" +
					"  }\n" +
					"}\n" );

				var name = $"rinten-raise-{Environment.ProcessId}";
				var id = (await Busctl( busctl, "call", "org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "loadScript", "ss", script, name )).Split( ' ' ).LastOrDefault()?.Trim();
				if ( !int.TryParse( id, out var scriptId ) ) return;

				await Busctl( busctl, "call", "org.kde.KWin", $"/Scripting/Script{scriptId}", "org.kde.kwin.Script", "run" );
				await Task.Delay( 200 );
				await Busctl( busctl, "call", "org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "unloadScript", "s", name );
			}
			catch ( Exception )
			{
				//raising the window is a courtesy; the file is open either way
			}
			finally
			{
				try { System.IO.File.Delete( script ); } catch ( Exception ) { }
			}
		} );
	}

	static async Task<string> Busctl( string busctl, params string[] args )
	{
		var info = new System.Diagnostics.ProcessStartInfo { FileName = busctl, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
		info.ArgumentList.Add( "--user" );
		foreach ( var a in args ) info.ArgumentList.Add( a );

		using var process = System.Diagnostics.Process.Start( info );
		var output = await process.StandardOutput.ReadToEndAsync();
		await process.WaitForExitAsync();
		return output;
	}
}
