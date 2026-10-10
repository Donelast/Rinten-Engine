namespace Editor.Preferences;

//The window and the sidebar know only this, so a test - or a tool that wants a preferences window over its own settings - hands in a different list without either of them changing.
/// <summary>Where the preferences window gets its pages from.</summary>
public interface IPreferencesPageSource
{
	IReadOnlyList<PreferencesPageSection> Sections();
}
