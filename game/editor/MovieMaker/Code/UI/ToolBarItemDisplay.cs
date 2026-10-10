using Rinten.Utility;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>What a toolbar item says about itself: its name, its icon, and why you would press it.</summary>
public sealed record ToolBarItemDisplay(
	string Title,
	string Icon,
	string? Description = null,
	bool Background = true,
	string? ShortcutIdentifier = null,
	string? ShortcutKeyBind = null )
{
	public string? ShortcutDisplayKeys => !string.IsNullOrEmpty( ShortcutIdentifier ) && EditorShortcuts.GetDisplayKeys( ShortcutIdentifier ) is { } keys
		? keys : ShortcutKeyBind?.ToTitleCase();

	/// <summary>The title with the keys that do the same thing, for a tooltip.</summary>
	public string FormattedTitle => ShortcutDisplayKeys is { } keys
		? $"{Title} ({keys})"
		: Title;

	/// <summary>Title on one line, description under it.</summary>
	public string ToolTip => string.IsNullOrEmpty( Description )
		? FormattedTitle
		: $"{FormattedTitle}\n{Description}";
}
