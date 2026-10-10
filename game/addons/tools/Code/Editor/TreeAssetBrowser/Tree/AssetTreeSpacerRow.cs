namespace Editor.TreeBrowser;

//Blank gap between tree sections; never selectable.
[StyleSheet( "AssetTreeSpacerRow.cs.scss" )]
sealed class AssetTreeSpacerRow : TreeRowPanel<AssetTreeItem>
{
	public AssetTreeSpacerRow( AssetTreePanel tree, AssetTreeItem node, TreeItemInfo info, int depth )
		: base( tree, node, info, depth, 0 )
	{
		AddClass( "asset-tree-spacer" );
	}
}
