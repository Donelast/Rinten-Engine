namespace Editor.Inspectors;

/// <summary>A widget that edits one file type ([CanEdit("asset:png")]), hosted by FileInspectorPanel</summary>
public interface IAssetInspector
{
	/// <summary>Called when a single asset is selected. Bind your editing UI to it.</summary>
	public void SetAsset( Asset asset );

	/// <summary>Called instead of <see cref="SetAsset"/> when multiple assets of the same type are selected. Return true if this inspector applied the whole selection. The default returns false, meaning "I only handle a single asset" - the caller then shows nothing rather than silently editing just the first.</summary>
	public bool SetAssets( Asset[] assets ) => false;
}
