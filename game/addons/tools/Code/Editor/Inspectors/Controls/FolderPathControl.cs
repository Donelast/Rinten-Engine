namespace Editor.Inspectors;

using System.IO;

/// <summary>A folder on disk, typed or browsed for.</summary>
[StyleSheet( "TextButtonControl.cs.scss" )]
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( FolderPathAttribute )] )]
public sealed class FolderPathControl : TextButtonControl
{
	protected override string ButtonIcon => "folder";

	protected override string ButtonToolTip => "Browse For Folder";

	protected override void OnButtonPressed()
	{
		var current = Current;

		var dialog = new FileDialog( null )
		{
			Title = "Find Folder",
			Directory = string.IsNullOrEmpty( current ) ? null : Path.GetDirectoryName( current )
		};

		dialog.SetFindDirectory();

		if ( !dialog.Execute() ) return;

		Assign( dialog.SelectedFile );
	}
}
