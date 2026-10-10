namespace Editor.Inspectors;

//The strip of saved curves: a handful that are always there, and the user's own.
[StyleSheet( "CurvePresetsPanel.cs.scss" )]
sealed class CurvePresetsPanel : Panel
{
	public sealed record Preset( Curve Curve, bool IsDefault );

	/// <summary>What to do when one is picked.</summary>
	public Action<Curve> OnPresetSelected { get; set; }

	/// <summary>Where the "save this one" button gets the curve from.</summary>
	public Func<Curve?> GetCurrentValue { get; set; }

	readonly List<Curve> userList = new();
	readonly List<Curve> defaultList;
	readonly Panel grid;
	readonly string cookie;

	public CurvePresetsPanel() : this( "CurvePresets.Panel" )
	{
	}

	public CurvePresetsPanel( string cookie )
	{
		this.cookie = cookie;

		grid = AddChild<Panel>( "grid" );

		userList.AddRange( EditorCookie.Get( cookie, new List<Curve>() ) );

		defaultList =
		[
			new Curve(),
			Curve.Ease,
			Curve.Ease.Reverse(),
			Curve.Linear,
			Curve.Linear.Reverse(),
			Curve.EaseIn,
			Curve.EaseIn.Reverse(),
			Curve.EaseOut,
			Curve.EaseOut.Reverse(),
			new Curve( new Curve.Frame( 0, 1 ), new Curve.Frame( 0.5f, 0 ), new Curve.Frame( 1, 1 ) ),
			new Curve( new Curve.Frame( 0, 0 ), new Curve.Frame( 0.5f, 1 ), new Curve.Frame( 1, 0 ) ),
		];

		Rebuild();
	}

	void SaveChanges() => EditorCookie.Set( cookie, userList );

	public void Rebuild()
	{
		grid.DeleteChildren();

		foreach ( var curve in defaultList )
		{
			grid.AddChild( new CurvePresetItem( new Preset( curve, true ), Pick, ShowContext ) );
		}

		foreach ( var curve in userList )
		{
			grid.AddChild( new CurvePresetItem( new Preset( curve, false ), Pick, ShowContext ) );
		}

		var add = new CurvePresetItem();
		add.AddClass( "add" );
		add.OnClicked = () =>
		{
			var current = GetCurrentValue?.Invoke();

			if ( !current.HasValue ) return;

			userList.Add( current.Value );

			Rebuild();
			SaveChanges();
		};

		grid.AddChild( add );
	}

	void Pick( Preset preset ) => OnPresetSelected?.Invoke( preset.Curve );

	void ShowContext( Preset preset )
	{
		var menu = new Menu();

		menu.AddOption( "Copy Json", "content_copy", () =>
		{
			EditorUtility.Clipboard.Copy( JsonSerializer.Serialize( preset.Curve ) );
		} );

		var clipboard = EditorUtility.Clipboard.Paste();

		var paste = menu.AddOption( "Paste Json", "content_paste", () =>
		{
			var value = JsonSerializer.Deserialize<Curve>( clipboard );

			var index = userList.IndexOf( preset.Curve );

			if ( index < 0 ) return;

			userList[index] = value;

			Rebuild();
			SaveChanges();
		} );

		try
		{
			JsonSerializer.Deserialize<Curve>( clipboard );
		}
		catch
		{
			paste.Enabled = false;
		}

		paste.Enabled &= !preset.IsDefault;

		menu.AddSeparator();

		menu.AddOption( "Replace", "refresh", () =>
		{
			var current = GetCurrentValue?.Invoke();

			if ( !current.HasValue ) return;

			var index = userList.IndexOf( preset.Curve );

			if ( index < 0 ) return;

			userList[index] = current.Value;

			Rebuild();
			SaveChanges();
		} ).Enabled = !preset.IsDefault && (GetCurrentValue?.Invoke()).HasValue;

		menu.AddSeparator();

		menu.AddOption( "Delete", "delete", () =>
		{
			userList.Remove( preset.Curve );

			Rebuild();
			SaveChanges();
		} ).Enabled = !preset.IsDefault;

		menu.OpenAtCursor( this );
	}
}

//One saved curve in the strip - or the button that adds another.
[StyleSheet( "CurvePresetsPanel.cs.scss" )]
sealed class CurvePresetItem : Panel
{
	readonly CurvePresetsPanel.Preset preset;
	readonly Action<CurvePresetsPanel.Preset> onContext;
	readonly List<Vector2> scratch = new();

	Image image;
	Bitmap bitmap;
	Texture texture;
	Vector2Int painted;

	public Action OnClicked { get; set; }

	public CurvePresetItem( CurvePresetsPanel.Preset preset, Action<CurvePresetsPanel.Preset> onClicked, Action<CurvePresetsPanel.Preset> onContext )
	{
		this.preset = preset;
		this.onContext = onContext;

		OnClicked = () => onClicked( preset );

		image = AddChild<Image>( "fill" );

		AddEventListener( "onmousedown", OnMouseDown );
	}

	public CurvePresetItem()
	{
		AddClass( "add" );

		var label = AddChild<Rinten.UI.Label>( "plus" );
		label.Text = "+";

		AddEventListener( "onmousedown", OnMouseDown );
	}

	bool wasHovered;
	bool dirty = true;

	public override void Tick()
	{
		base.Tick();

		if ( preset is null || image is null ) return;

		if ( wasHovered != HasHovered )
		{
			wasHovered = HasHovered;
			dirty = true;
		}

		Repaint();
	}

	void Repaint()
	{
		var rect = Box.RectInner;

		var width = rect.Width.FloorToInt();
		var height = rect.Height.FloorToInt();

		if ( width < 2 || height < 2 ) return;

		var size = new Vector2Int( width, height );

		if ( !dirty && size == painted ) return;

		dirty = false;

		if ( size != painted || bitmap is null )
		{
			bitmap?.Dispose();
			bitmap = new Bitmap( width, height );
			painted = size;
		}

		var scale = ScaleFromScreen;

		image.Style.Width = Length.Pixels( width * scale );
		image.Style.Height = Length.Pixels( height * scale );

		bitmap.Clear( Color.Transparent );
		bitmap.SetAntialias( true );

		CurveDraw.SampleCurve( preset.Curve, new Rect( 0, 0, width, height ).Shrink( 5 ), scratch );
		CurveDraw.Polyline( bitmap, scratch, Theme.Primary.WithAlpha( HasHovered ? 1f : 0.7f ), 3 );

		CurveDraw.Present( image, bitmap, ref texture );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		bitmap?.Dispose();
		bitmap = null;

		texture?.Dispose();
		texture = null;
	}

	void OnMouseDown( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe ) return;

		if ( mpe.MouseButton == MouseButtons.Right && preset is not null )
		{
			onContext?.Invoke( preset );
			e.StopPropagation();
			return;
		}

		if ( mpe.MouseButton != MouseButtons.Left ) return;

		OnClicked?.Invoke();
		e.StopPropagation();
	}
}
