using Rinten;
using System;
using System.Collections.Generic;

namespace Editor;

//This is one of the few things worth asking the operating system for rather than drawing ourselves: people know their own file box, it knows their shortcuts and their network drives, and SDL already has the call for it.
/// <summary>The platform's own open and save boxes.</summary>
public partial class FileDialog
{
	private readonly List<string> chosen = new();

	private string title;
	private string directory;
	private string suffix;
	private string filterName = "All files";
	private string filterPattern = "*";

	private bool saving;
	private bool folders;
	private bool many;

	internal bool ExecuteManaged()
	{
		//Where the box opens: the folder, and in it the file already chosen - the name SelectFile gave, which a save box then
		//offers. It was cleared here and only the folder was passed, so every "save as" in the editor opened on an empty name.
		var location = Location( chosen.Count > 0 ? chosen[0] : null );

		chosen.Clear();

		var picked = folders
			? CoreFileDialog.Folder( title, location )
			: (saving
				? CoreFileDialog.Save( title, location, filterName, filterPattern )
				: CoreFileDialog.Open( title, location, filterName, filterPattern, many ));

		if ( picked is null || picked.Count == 0 ) return false;

		foreach ( var path in picked )
		{
			//A saved file gets the suffix the caller asked for, where whoever typed the name left it off.
			var wanted = path;

			if ( saving && !string.IsNullOrEmpty( suffix ) && !wanted.Contains( '.' ) )
				wanted = $"{wanted}.{suffix.TrimStart( '.' )}";

			chosen.Add( wanted );
		}

		return true;
	}

	//The folder the box starts in, with the file chosen beforehand in it where there is one: an absolute path as it stands, a bare name
	//joined to the folder. A save box's name wants its suffix, which the caller may have left off - the same rule the answer is given below.
	private string Location( string preset )
	{
		if ( folders || string.IsNullOrWhiteSpace( preset ) ) return directory;

		var file = System.IO.Path.IsPathRooted( preset ) || string.IsNullOrEmpty( directory )
			? preset
			: System.IO.Path.Combine( directory, preset );

		if ( System.IO.Directory.Exists( file ) ) return file;

		if ( saving && !string.IsNullOrEmpty( suffix ) && !System.IO.Path.HasExtension( file ) )
			file = $"{file}.{suffix.TrimStart( '.' )}";

		return file;
	}

	internal string TitleManaged { get => title; set => title = value; }
	internal string DirectoryManaged { get => directory; set => directory = value; }
	internal string SuffixManaged { get => suffix; set => suffix = value; }

	internal string SelectedFileManaged
	{
		get => chosen.Count > 0 ? chosen[0] : null;
		set
		{
			chosen.Clear();

			if ( !string.IsNullOrEmpty( value ) ) chosen.Add( value );
		}
	}

	internal List<string> SelectedFilesManaged => chosen;

	internal void SetFilterManaged( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return;

		var open = text.IndexOf( '(' );
		var close = text.LastIndexOf( ')' );

		if ( open < 0 || close < open )
		{
			filterName = text.Trim();
			filterPattern = "*";
			return;
		}

		filterName = text[..open].Trim();

		var patterns = text[(open + 1)..close]
			.Replace( "*.", "" )
			.Replace( "*", "" )
			.Split( new[] { ' ', ';', ',' }, StringSplitOptions.RemoveEmptyEntries );

		filterPattern = patterns.Length > 0 ? string.Join( ';', patterns ) : "*";
	}

	internal void SetModeManaged( bool save ) => saving = save;
	internal void SetFoldersManaged( bool wantFolders ) => folders = wantFolders;
	internal void SetManyManaged( bool wantMany ) => many = wantMany;
}
