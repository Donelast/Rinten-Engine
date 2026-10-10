using System.IO;
namespace Editor;

public static partial class EditorUtility
{
	/// <summary>Open a file save dialog. Returns null on cancel, else the absolute path of the target file.</summary>
	public static string SaveFileDialog( string title, string extension, string defaultPath )
	{
		extension = extension.Trim( '.' );

		//A folder to start in, or a file whose folder it is and whose name is offered. Told apart by what is on disk, not by a dot: "is
		//there a dot in it" took a folder under ~/.cache for a file, and a bare folder with no slash on the end for the name to save under.
		var isFolder = string.IsNullOrEmpty( defaultPath ) || Directory.Exists( defaultPath )
			|| defaultPath.EndsWith( '/' ) || defaultPath.EndsWith( '\\' );

		var fd = new FileDialog( null );
		fd.Title = title;
		fd.Directory = isFolder ? defaultPath : Path.GetDirectoryName( defaultPath );
		fd.DefaultSuffix = $".{extension}";
		if ( !isFolder ) fd.SelectFile( Path.GetFileName( defaultPath ) );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( $"{extension} (*.{extension})" );

		if ( !fd.Execute() )
			return null;

		return fd.SelectedFile;
	}

	/// <summary>Open a file open dialog. Returns null on cancel, else the absolute path of the target file.</summary>
	public static string OpenFileDialog( string title, string extension, string defaultPath )
	{
		extension = extension.Trim( '.' );

		var fd = new FileDialog( null );
		fd.Title = title;
		fd.Directory = Path.GetDirectoryName( defaultPath );
		fd.DefaultSuffix = $".{extension}";
		fd.SelectFile( Path.GetFileName( defaultPath ) );
		fd.SetFindFile();
		fd.SetModeOpen();
		fd.SetNameFilter( $"{extension} (*.{extension})" );

		if ( !fd.Execute() )
			return null;

		return fd.SelectedFile;
	}
}
