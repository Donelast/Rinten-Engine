namespace Editor;

//Launcher shown as a modal over an editor with no project; picked projects open in that editor
internal static class ProjectsOverEditor
{
	//The card, telling chosen which project file was picked.
	public static Rinten.UI.Panel Make( Action<string> chosen )
	{
		var panel = new LauncherPanel
		{
			//With mips, so the mark is smooth where it is drawn smaller than it is.
			Mark = Pixmap.FromFile( "logo_rounded.png" )?.picture?.ToTexture( mips: true ),
			Chosen = project =>
			{
				LauncherPreferences.Save();
				chosen( project.ConfigFilePath );
			},
		};

		EditorStyles.Apply( panel, "LauncherPanel.razor.scss" );

		//A pane over the editor rather than a window: its size, its tint and its edge are the stylesheet's - see LauncherPanel.over-editor.
		panel.AddClass( "over-editor" );

		return panel;
	}
}
