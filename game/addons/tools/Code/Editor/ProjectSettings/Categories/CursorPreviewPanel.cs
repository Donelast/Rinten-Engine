namespace Editor.ProjectSettingPages.Categories;

[StyleSheet( "CursorPreviewPanel.cs.scss" )]
sealed class CursorPreviewPanel : Panel
{
	string systemCursor;
	string imagePath;
	Vector2 hotspot;

	Image image;
	Rinten.UI.Label placeholder;
	Texture texture;
	string loadedKey;

	public string SystemCursor
	{
		get => systemCursor;
		set
		{
			if ( systemCursor == value ) return;
			systemCursor = value;
			Invalidate();
		}
	}

	public string ImagePath
	{
		get => imagePath;
		set
		{
			if ( imagePath == value ) return;
			imagePath = value;
			Invalidate();
		}
	}

	public Vector2 Hotspot
	{
		get => hotspot;
		set
		{
			if ( hotspot == value ) return;
			hotspot = value;
			Invalidate();
		}
	}

	public override void SetPropertyObject( string name, object value )
	{
		if ( TrySet( name, value ) )
			return;

		base.SetPropertyObject( name, value );
	}

	public override void SetProperty( string name, string value )
	{
		if ( string.Equals( name, "class", StringComparison.OrdinalIgnoreCase )
			|| string.Equals( name, "style", StringComparison.OrdinalIgnoreCase ) )
		{
			base.SetProperty( name, value );
			return;
		}

		if ( TrySet( name, value ) )
			return;

		base.SetProperty( name, value );
	}

	bool TrySet( string name, object value )
	{
		if ( string.Equals( name, "SystemCursor", StringComparison.OrdinalIgnoreCase ) )
		{
			SystemCursor = value as string;
			return true;
		}

		if ( string.Equals( name, "ImagePath", StringComparison.OrdinalIgnoreCase ) )
		{
			ImagePath = value as string;
			return true;
		}

		if ( string.Equals( name, "Hotspot", StringComparison.OrdinalIgnoreCase )
			&& value is Vector2 hotspotValue )
		{
			Hotspot = hotspotValue;
			return true;
		}

		return false;
	}

	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );
		EnsureImage();
		Reload();
	}

	protected override void OnParametersSet()
	{
		base.OnParametersSet();
		EnsureImage();
		Reload();
	}

	public void UpdatePreview( string path, Vector2 hotspotValue )
	{
		ImagePath = path;
		Hotspot = hotspotValue;
	}

	void EnsureImage()
	{
		if ( image.IsValid() )
			return;

		image = AddChild<Image>( "cursor-preview-image" );
	}

	void EnsurePlaceholder()
	{
		if ( placeholder.IsValid() )
			return;

		placeholder = AddChild<Rinten.UI.Label>();
		placeholder.AddClass( "cursor-preview-placeholder" );
		placeholder.Text = "mouse";
	}

	void Invalidate()
	{
		loadedKey = null;
		Reload();
	}

	void Reload()
	{
		var key = $"{systemCursor}|{imagePath}|{hotspot}";
		if ( key == loadedKey )
			return;

		loadedKey = key;
		texture = LoadTexture();

		var hasImage = texture is not null && texture.IsValid();
		SetClass( "has-image", hasImage );

		EnsureImage();
		if ( image.IsValid() )
		{
			image.Texture = hasImage ? texture : null;
			image.SetClass( "hidden", !hasImage );
		}

		if ( !hasImage )
		{
			EnsurePlaceholder();
			placeholder.SetClass( "hidden", false );
		}
		else if ( placeholder.IsValid() )
		{
			placeholder.SetClass( "hidden", true );
		}

		MarkRenderDirty();
	}

	Texture LoadTexture()
	{
		if ( !string.IsNullOrWhiteSpace( systemCursor ) )
		{
			var pixmap = CursorSystemPixmap.Get( systemCursor );
			if ( pixmap?.picture is null )
			{
				Log.Warning( $"Couldn't extract system cursor preview for '{systemCursor}'" );
				return null;
			}

			return pixmap.picture.ToTexture( mips: false );
		}

		if ( string.IsNullOrWhiteSpace( imagePath ) )
			return null;

		var path = imagePath.Trim();
		var fullPath = FileSystem.Content.GetFullPath( path );
		if ( string.IsNullOrWhiteSpace( fullPath ) )
			return null;

		var custom = Pixmap.FromFile( fullPath );
		return custom?.picture?.ToTexture( mips: false );
	}

	Rect GetTextureRect()
	{
		if ( texture is null || !texture.IsValid() )
			return default;

		var r = Box.RectInner.Shrink( 2 );
		if ( r.Width <= 0 || r.Height <= 0 )
			return default;

		var scale = MathF.Min( r.Width / texture.Width, r.Height / texture.Height );
		var drawW = texture.Width * scale;
		var drawH = texture.Height * scale;

		return new Rect(
			r.Left + (r.Width - drawW) * 0.5f,
			r.Top + (r.Height - drawH) * 0.5f,
			drawW,
			drawH );
	}

	public override void OnDraw()
	{
		if ( hotspot == default || !string.IsNullOrWhiteSpace( systemCursor ) )
			return;

		if ( texture is null || !texture.IsValid() )
			return;

		var rect = GetTextureRect();
		if ( rect.Width <= 0 || rect.Height <= 0 )
			return;

		var scale = rect.Width / texture.Width;
		var hx = rect.Left + Hotspot.x * scale;
		var hy = rect.Top + Hotspot.y * scale;
		var size = 4f;

		Draw.Rect( new Rect( hx - size, hy - 0.5f, size * 2f, 1f ), Color.Red );
		Draw.Rect( new Rect( hx - 0.5f, hy - size, 1f, size * 2f ), Color.Red );
	}
}
