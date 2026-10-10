namespace Editor.CodeEditors;

//Cookie-backed settings for CustomCodeEditor, exposed as properties so they can be added to a ControlSheet.
internal static class CustomCodeEditorSettings
{
	/// <summary>Path to the executable to launch.</summary>
	[FilePath( "exe", AllowExternal = true )]
	public static string ExecutablePath
	{
		get => EditorCookie.GetString( CustomCodeEditor.ExecutablePathKey, "" );
		set => EditorCookie.SetString( CustomCodeEditor.ExecutablePathKey, value );
	}

	/// <summary>Arguments used when opening a file. Placeholders: {file}, {line}, {column}, {solution}, {project_root}</summary>
	public static string OpenFileArgs
	{
		get => EditorCookie.GetString( CustomCodeEditor.OpenFileArgsKey, CustomCodeEditor.DefaultOpenFileArgs );
		set => EditorCookie.SetString( CustomCodeEditor.OpenFileArgsKey, value );
	}

	/// <summary>Arguments used when opening a solution. Placeholders: {solution}, {project_root}</summary>
	public static string OpenSolutionArgs
	{
		get => EditorCookie.GetString( CustomCodeEditor.OpenSolutionArgsKey, CustomCodeEditor.DefaultOpenSolutionArgs );
		set => EditorCookie.SetString( CustomCodeEditor.OpenSolutionArgsKey, value );
	}
}
