namespace Editor.Inspectors;

//Thumbnail or type icon for a resource field.
[StyleSheet( "ResourceThumbPanel.cs.scss" )]
sealed class ResourceThumbPanel : Rinten.UI.Panel
{
	Rinten.UI.Image _image;
	Rinten.UI.Label _errorIcon;
	string _lastSource;
	string _cachedKey;
	Texture _cachedTexture;

	public ResourceThumbPanel()
	{
		AddClass( "thumb" );

		_image = AddChild<Rinten.UI.Image>( "thumb-image" );

		_errorIcon = AddChild<Rinten.UI.Label>();
		_errorIcon.AddClass( "icon" );
		_errorIcon.AddClass( "error" );
		_errorIcon.Text = "error";
		_errorIcon.Tokenize = false;
	}

	public void Update( ResourceDisplayState state )
	{
		SetClass( "error", state.Mode == ResourceFieldMode.Missing );

		if ( state.Mode == ResourceFieldMode.Missing )
		{
			_image.Texture = null;
			SetClass( "image", false );
			SetClass( "empty", false );
			return;
		}

		var source = state.Resource?.ResourcePath ?? state.Path ?? "";
		Pixmap pixmap = null;

		if ( state.Mode != ResourceFieldMode.Multiple && state.Asset is { IsDeleted: false } asset )
		{
			try
			{
				pixmap = asset.GetAssetThumb( false );
			}
			catch ( Exception ex )
			{
				Log.Error( ex, $"ResourceControl: GetAssetThumb failed for {asset.Path}" );
			}
		}

		if ( pixmap?.picture is null || !pixmap.picture.IsValid )
			pixmap = state.Thumb;

		if ( pixmap?.picture is null || !pixmap.picture.IsValid )
			pixmap = state.AssetType?.Icon64;

		if ( pixmap?.picture is null || !pixmap.picture.IsValid )
		{
			_cachedKey = null;
			_cachedTexture = null;
			_image.Texture = null;
			_lastSource = source;
			SetClass( "image", false );
			SetClass( "empty", true );
			return;
		}

		var key = pixmap.picture.GetHashCode().ToString();
		if ( source != _lastSource || _cachedKey != key || _cachedTexture is null || !_cachedTexture.IsValid )
		{
			_cachedKey = key;
			_cachedTexture = pixmap.picture.ToTexture( mips: false );
			_lastSource = source;
		}

		_image.Texture = _cachedTexture;
		SetClass( "image", true );
		SetClass( "empty", false );
	}
}
