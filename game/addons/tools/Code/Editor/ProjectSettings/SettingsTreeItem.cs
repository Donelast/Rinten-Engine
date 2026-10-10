namespace Editor.ProjectSettingsUI;

/// <summary>One node in the project settings navigation tree.</summary>
public sealed class SettingsTreeItem
{
	readonly List<SettingsTreeItem> children = new();

	public string Title { get; init; }
	public string Description { get; init; }
	public string Icon { get; init; }
	public SettingsTreeItemKind Kind { get; init; }
	public IReadOnlyList<Type> CategoryTypes { get; internal set; } = [];
	internal Action<ProjectSettingsCategoryPanel> Configure { get; init; }

	public IReadOnlyList<SettingsTreeItem> Children => children;
	public bool HasChildren => children.Count > 0;

	internal void AddChild( SettingsTreeItem child ) => children.Add( child );
}

public enum SettingsTreeItemKind
{
	Section,
	Category,
	Child
}
