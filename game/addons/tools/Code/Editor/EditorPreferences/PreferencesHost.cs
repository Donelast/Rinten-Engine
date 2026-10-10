namespace Editor.Preferences;

//The one widget the preferences window is made of: everything inside it is panels.
sealed class PreferencesHost : PanelWidget
{
	public PreferencesHost( Widget parent, PreferencesPanel panel ) : base( parent )
	{
		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.Flexible;

		SetPanel( panel );
	}
}
