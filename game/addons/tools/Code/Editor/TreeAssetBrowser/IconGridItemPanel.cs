namespace Editor.TreeBrowser;

using System.IO;

//One tile in the icon grid. Tiles are pooled and re-pointed rather than rebuilt.
[StyleSheet( "IconGridItemPanel.cs.scss" )]
sealed class IconGridItemPanel : Panel
{
	public GridItem Item { get; private set; }
	public int Index { get; private set; }

	readonly IconGridCanvasPanel grid;
	readonly Panel iconHost;
	readonly Rinten.UI.Label icon;
	readonly Rinten.UI.Image thumb;
	readonly Rinten.UI.Image badge;
	readonly Rinten.UI.Label title;
	readonly Rinten.UI.Label extension;

	bool thumbResolved;

	float placedLeft = float.NaN;
	float placedTop = float.NaN;

	/// <summary>Whether this pooled tile is currently showing an item.</summary>
	public bool Live { get; private set; }

	public IconGridItemPanel( IconGridCanvasPanel grid )
	{
		this.grid = grid;

		AddClass( "icon-grid-item" );
		SetClass( "hidden", true );

		iconHost = AddChild<Panel>( "icon-host" );
		icon = iconHost.AddChild<Rinten.UI.Label>( "icon" );
		icon.AddClass( "material-icon" );
		thumb = iconHost.AddChild<Rinten.UI.Image>( "thumb" );
		thumb.AddClass( "hidden" );

		badge = iconHost.AddChild<Rinten.UI.Image>( "badge" );
		badge.AddClass( "hidden" );

		title = AddChild<Rinten.UI.Label>( "title" );
		extension = AddChild<Rinten.UI.Label>( "extension" );
		extension.AddClass( "extension" );

		ApplyViewMode();
	}

	/// <summary>Puts an item on this tile and brings it on screen.</summary>
	public void Show( GridItem item, int index )
	{
		if ( !Live )
		{
			Live = true;
			SetClass( "hidden", false );
		}

		var same = ReferenceEquals( Item, item );

		Item = item;
		Index = index;

		if ( same ) return;

		SetClass( "folder", item.IsFolder );
		Refresh();
	}

	/// <summary>Takes this tile out of view and back into the pool.</summary>
	public void Park()
	{
		if ( !Live ) return;

		Live = false;
		SetClass( "hidden", true );
	}

	/// <summary>Where this tile sits in the grid's own coordinates.</summary>
	public void Place( float left, float top )
	{
		if ( placedLeft == left && placedTop == top )
			return;

		placedLeft = left;
		placedTop = top;

		Style.Left = Length.Pixels( left );
		Style.Top = Length.Pixels( top );
	}

	public void ApplyViewMode()
	{
		RemoveClass( "small" );
		RemoveClass( "medium" );
		RemoveClass( "large" );
		RemoveClass( "list" );

		AddClass( GridMetrics.ClassFor( BrowserSettings.ViewMode ) );
	}

	/// <summary>Everything that is free to draw; the preview is fetched later, once on screen.</summary>
	public void Refresh()
	{
		if ( Item is null ) return;

		RefreshLabels();

		thumb.SetClass( "hidden", true );
		icon.SetClass( "hidden", false );
		icon.Text = Item.IsFolder ? "folder" : AssetFileIcons.GetIcon( Item.Extension );
		icon.SetClass( "folder", Item.IsFolder );

		SetClass( "engine-asset", false );
		badge.SetClass( "hidden", true );

		thumbResolved = false;
	}

	/// <summary>Just the writing, which is all a view mode change can affect.</summary>
	public void RefreshLabels()
	{
		if ( Item is null ) return;

		if ( Item.IsFolder )
		{
			title.Text = Item.Name;
			extension.SetClass( "hidden", true );
			return;
		}

		title.Text = Path.GetFileNameWithoutExtension( Item.Name );

		//The name is drawn without it, so the extension is a line of its own under the title in the icon modes and a column of its own in the list.
		extension.Text = Item.Extension?.TrimStart( '.' ) ?? string.Empty;
		extension.SetClass( "hidden", string.IsNullOrEmpty( Item.Extension ) );
	}

	/// <summary>Is there still a preview to fetch for this tile?</summary>
	public bool NeedsThumb => !thumbResolved && Item is { IsFolder: false, Asset: not null };

	/// <summary>Fetches the preview: a disk read, a decode and a texture upload, so the grid only ever asks for tiles on screen and only a few of them per frame.</summary>
	public void LoadThumb()
	{
		thumbResolved = true;

		Pixmap pixmap = null;
		try { pixmap = Item.Asset.GetAssetThumb(); }
		catch { /* ignore */ }

		if ( pixmap?.picture is null )
			return;

		thumb.Texture = pixmap.picture.ToTexture( mips: false );
		thumb.SetClass( "hidden", false );
		icon.SetClass( "hidden", true );

		MarkEngineAsset();
	}

	//Green outline and type badge on tiles for engine-loaded assets, so they differ from sources
	void MarkEngineAsset()
	{
		var type = Item.Asset?.AssetType;
		var picture = type is { IsGameResource: true } ? ( type.Icon64 ?? type.Icon16 )?.picture : null;

		SetClass( "engine-asset", type is { IsGameResource: true } );
		badge.SetClass( "hidden", picture is null );

		if ( picture is null ) return;

		badge.Texture = picture.ToTexture( mips: false );
	}

	/// <summary>The engine finished rendering this asset's preview, so ask for it again.</summary>
	public void InvalidateThumb() => thumbResolved = false;

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton == MouseButtons.Right )
		{
			grid.OpenItemContextMenu( Index );
			e.StopPropagation();
			return;
		}

		if ( e.MouseButton != MouseButtons.Left ) return;

		grid.BeginItemPress( Index, e );
		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		grid.EndItemPress( Index, e );
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		base.OnDoubleClick( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		grid.ActivateItem( Index );
		e.StopPropagation();
	}
}
