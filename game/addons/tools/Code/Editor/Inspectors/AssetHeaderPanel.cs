namespace Editor.Inspectors;

/// <summary>Asset page header card: thumbnail or icon, name and path, plus the page's own tools and fields</summary>
[StyleSheet( "AssetHeaderPanel.cs.scss" )]
public sealed class AssetHeaderPanel : Rinten.UI.Panel
{
	readonly Asset _asset;
	readonly Rinten.UI.Panel _thumb;
	readonly Rinten.UI.Image _image;

	/// <summary>Beside the name, at the far end: the page's buttons.</summary>
	public Rinten.UI.Panel Tools { get; }

	/// <summary>Under the name: the page's own settings, as plain rows.</summary>
	public Rinten.UI.Panel Fields { get; }

	Pixmap _shown;
	Texture _texture;
	RealTimeSince _sinceAsked;

	public AssetHeaderPanel( Asset asset )
	{
		_asset = asset;

		AddClass( "asset-header inspector-head" );

		var top = AddChild<Rinten.UI.Panel>( "top-row" );

		_thumb = top.AddChild<Rinten.UI.Panel>( "thumb" );
		_image = _thumb.AddChild<Rinten.UI.Image>( "thumb-image" );

		var text = top.AddChild<Rinten.UI.Panel>( "text" );

		var title = text.AddChild<Rinten.UI.Label>( "title" );
		title.Text = asset.Name;
		title.Tokenize = false;

		var path = text.AddChild<Rinten.UI.Label>( "path" );
		path.Text = asset.Path;
		path.Tokenize = false;
		path.Tooltip = asset.AbsolutePath;

		if ( asset.AssetType is { } type )
		{
			var kind = text.AddChild<Rinten.UI.Label>( "kind" );
			kind.Text = type.FriendlyName;
			kind.Tokenize = false;
			kind.Style.FontColor = type.Color;
		}

		Tools = top.AddChild<Rinten.UI.Panel>( "tools" );
		Fields = AddChild<Rinten.UI.Panel>( "fields" );

		RefreshThumb();
	}

	public override void Tick()
	{
		base.Tick();

		if ( _sinceAsked < 0.5f ) return;

		RefreshThumb();
	}

	void RefreshThumb()
	{
		_sinceAsked = 0;

		if ( _asset is null || _asset.IsDeleted ) return;

		Pixmap pixmap = null;

		try
		{
			pixmap = _asset.GetAssetThumb();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"No thumbnail for {_asset.Path}" );
		}

		if ( pixmap?.picture is null || !pixmap.picture.IsValid )
			pixmap = _asset.AssetType?.Icon256;

		if ( pixmap?.picture is null || !pixmap.picture.IsValid || pixmap == _shown ) return;

		_shown = pixmap;
		_texture = pixmap.picture.ToTexture( mips: false );
		_image.Texture = _texture;
	}
}
