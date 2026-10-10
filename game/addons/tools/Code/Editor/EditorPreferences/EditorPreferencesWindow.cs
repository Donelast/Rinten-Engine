namespace Editor.Preferences;

internal sealed class EditorPreferencesWindow : BaseWindow
{
	readonly PreferencesPanel panel;

	[Menu( "Editor", "Edit/Preferences", "room_preferences", Priority = -1 )]
	public static EditorPreferencesWindow OpenEditorPreferences()
	{
		var prefs = new EditorPreferencesWindow();
		prefs.Show();

		return prefs;
	}

	//Opens the window on one page, for the parts of the editor that link to a setting.
	internal void SwitchPage<T>() where T : PreferencesPage => panel.SwitchTo<T>();

	public EditorPreferencesWindow()
	{
		SetModal( true, true );

		Size = new Vector2( 1024, 768 );
		MinimumSize = Size;
		TranslucentBackground = true;
		NoSystemBackground = true;

		WindowTitle = "Editor Settings";
		SetWindowIcon( "room_preferences" );

		panel = new PreferencesPanel();
		Canvas = new PreferencesHost( this, panel );

		EditorEvent.Run( "editor.preferences", panel );
	}
}
