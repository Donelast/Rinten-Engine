using System.IO;

namespace Editor.TreeBrowser;

public enum AssetTreeItemKind
{
	Section,
	Folder,
	File,
	PackageFolder,
	PackageSubFolder,
	Aggregate,
	Spacer
}

/// <summary>Domain node for the tree asset browser.</summary>
public sealed class AssetTreeItem
{
	public AssetTreeItemKind Kind { get; init; }
	public string Key { get; init; }
	public string Label { get; init; }
	public string Icon { get; init; }
	public string FullPath { get; init; }
	public string Extension { get; init; }
	public bool IsRoot { get; init; }
	public bool CanRename { get; init; }
	public bool CanDrag { get; init; }
	public Package Package { get; init; }
	public string PackageFolderName { get; init; }
	public IReadOnlyList<string> PackageFiles { get; init; }
	public Asset Asset { get; set; }

	/// <summary>Where the grid browses when this node is picked, for nodes that are not plain disk folders.</summary>
	public AssetBrowser.Location BrowseLocation { get; init; }

	/// <summary>Node never lists children, however many the location reports.</summary>
	public bool SuppressChildren { get; init; }

	/// <summary>Node is one of the user's pinned folders.</summary>
	public bool IsPinned { get; init; }

	public bool HasChildren => Kind switch
	{
		AssetTreeItemKind.Section => Children.Count > 0,
		AssetTreeItemKind.Folder => Directory.Exists( FullPath ) && (HasDiskChildren( FullPath ) || Children.Count > 0),
		AssetTreeItemKind.PackageFolder => PackageFiles?.Count > 0 || Children.Count > 0,
		AssetTreeItemKind.PackageSubFolder => PackageFiles?.Count > 0 || Children.Count > 0,
		AssetTreeItemKind.Aggregate => !SuppressChildren && (Children.Count > 0 || HasLocationChildren()),
		_ => false
	};

	internal List<AssetTreeItem> Children { get; } = [];

	bool? locationHasChildren;

	//Asked once per node: a mount walks its whole resource list to answer this.
	bool HasLocationChildren()
	{
		locationHasChildren ??= CountLocationChildren();
		return locationHasChildren.Value;
	}

	bool CountLocationChildren()
	{
		try
		{
			return BrowseLocation?.GetDirectories().Any() == true;
		}
		catch
		{
			return false;
		}
	}

	static bool HasDiskChildren( string path )
	{
		if ( !Directory.Exists( path ) ) return false;

		try
		{
			return Directory.EnumerateFileSystemEntries( path ).Any();
		}
		catch
		{
			return false;
		}
	}
}
