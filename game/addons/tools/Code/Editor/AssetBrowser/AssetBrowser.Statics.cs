using Editor.TreeBrowser;

namespace Editor;

public partial class AssetBrowser
{
	/// <summary>Opens the asset browser dock at <paramref name="asset"/>, raising it into view.</summary>
	public static void OpenTo( Asset asset ) => TreeAssetBrowser.OpenTo( asset );

	/// <summary>Opens the asset browser dock at <paramref name="entry"/>, raising it into view.</summary>
	public static void OpenTo( AssetEntry entry ) => TreeAssetBrowser.OpenTo( entry );
}
