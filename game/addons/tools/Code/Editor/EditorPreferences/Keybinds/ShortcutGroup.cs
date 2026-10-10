namespace Editor.Preferences;

/// <summary>Shortcuts listed under one heading in the keybinds page.</summary>
public sealed class ShortcutGroup
{
	public ShortcutGroup( string title, IReadOnlyList<ShortcutBinding> bindings )
	{
		Title = title;
		Bindings = bindings;
	}

	public string Title { get; }
	public IReadOnlyList<ShortcutBinding> Bindings { get; }
}
