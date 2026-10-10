namespace Editor.TreeBrowser;

sealed class GridItem
{
	public string Path;
	public string Name;
	public bool IsFolder;
	public Asset Asset;
	public string Extension;

	/// <summary>Set on folders that live in a browse location rather than on disk.</summary>
	public AssetBrowser.Location Location;
}
