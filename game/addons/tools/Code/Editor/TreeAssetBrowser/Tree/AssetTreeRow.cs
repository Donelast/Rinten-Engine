namespace Editor.TreeBrowser;

//The asset browser's tree row: the shared row, with the file's thumbnail in place of the glyph
[StyleSheet( "AssetTreeRow.cs.scss" )]
sealed class AssetTreeRow : TreeRowPanel<AssetTreeItem>
{
	readonly Rinten.UI.Image picture;

	Pixmap shown;

	public AssetTreeRow( AssetTreePanel tree, AssetTreeItem node, TreeItemInfo info, int depth )
		: base( tree, node, info, depth, 0 )
	{
		AddClass( "asset-tree-row" );

		picture = IconSlot.AddChild<Rinten.UI.Image>( "picture" );

		Decorate();
	}

	protected override bool CanExpand => Node.Kind != AssetTreeItemKind.File && Info.HasChildren;

	//The picture the row would show now: the file's thumbnail where the browser shows them, else its type's icon, else none.
	Pixmap CurrentPicture()
	{
		if ( Node.Kind != AssetTreeItemKind.File ) return null;

		if ( BrowserSettings.ShowTreeIcons )
		{
			try
			{
				if ( Node.Asset?.GetAssetThumb() is { picture: not null } thumb ) return thumb;
			}
			catch { /* a thumbnail that cannot be made is no thumbnail */ }
		}

		return Node.Asset?.AssetType?.Icon16;
	}

	//The picture as well as the pixmap: a thumbnail can arrive after the row is
	//made. Asked each frame; the picture is made into a texture only when it moved.
	protected override int BuildHash()
	{
		var pixmap = CurrentPicture();

		return HashCode.Combine( base.BuildHash(), pixmap, pixmap?.picture );
	}

	protected override void OnBuildHashChanged()
	{
		base.OnBuildHashChanged();

		Decorate();
	}

	protected override void Decorate()
	{
		//Called by the base before this row's own parts are made.
		if ( picture is null ) return;

		var pixmap = CurrentPicture();
		var has = pixmap?.picture is not null;

		picture.SetClass( "hidden", !has );
		Icon.SetClass( "hidden", has || string.IsNullOrEmpty( Info.Icon ) );

		if ( !has || pixmap == shown ) return;

		shown = pixmap;
		picture.Texture = pixmap.picture.ToTexture( mips: false );
	}
}
