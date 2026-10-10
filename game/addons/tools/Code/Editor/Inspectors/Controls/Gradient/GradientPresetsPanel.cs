using System.Collections.Immutable;
using static Rinten.Gradient;
namespace Editor.Inspectors;
[StyleSheet( "GradientPresetsPanel.cs.scss" )]
public sealed class GradientPresetsPanel : Panel
{
	public record GradientPreset( Gradient gradient, bool isDefault );
	public GradientPresetsPanel() : this( "GradientPresets" )
	{
	}
	public Action<Gradient> OnPresetSelected;
	public Func<Gradient> GetCurrentValue;
	readonly List<Gradient> _userList = new();
	readonly List<Gradient> _defaultList;
	readonly Panel _grid;
	readonly string _cookie;
	public GradientPresetsPanel( string cookie = "GradientPresets" )
	{
		_cookie = cookie;
		_grid = AddChild<Panel>( "grid" );
		_userList.AddRange( EditorCookie.Get( cookie, new List<Gradient>() ) );
		_defaultList =
		[
			new Gradient( new ColorFrame( 0, "#000000" ), new ColorFrame( 1, "#FFFFFF" ) )
			{
				Alphas = ImmutableList<AlphaFrame>.Empty.Add( new AlphaFrame( 0, 1 ) ).Add( new AlphaFrame( 1, 1 ) )
			},
			new Gradient( new ColorFrame( 0, "#FFFFFF" ), new ColorFrame( 1, "#000000" ) )
			{
				Alphas = ImmutableList<AlphaFrame>.Empty.Add( new AlphaFrame( 0, 1 ) ).Add( new AlphaFrame( 1, 1 ) )
			},
			new Gradient(
				new ColorFrame( 0, "#FF4500" ),
				new ColorFrame( 0.5f, "#FFD700" ),
				new ColorFrame( 1, "#00008B" ) )
			{
				Alphas = ImmutableList<AlphaFrame>.Empty.Add( new AlphaFrame( 0, 1 ) ).Add( new AlphaFrame( 1, 1 ) )
			},
			new Gradient(
				new ColorFrame( 0, "#FF0000" ),
				new ColorFrame( 0.17f, "#FF7F00" ),
				new ColorFrame( 0.33f, "#FFFF00" ),
				new ColorFrame( 0.5f, "#00FF00" ),
				new ColorFrame( 0.67f, "#0000FF" ),
				new ColorFrame( 0.83f, "#4B0082" ),
				new ColorFrame( 1, "#8B00FF" ) )
			{
				Alphas = ImmutableList<AlphaFrame>.Empty.Add( new AlphaFrame( 0, 1 ) ).Add( new AlphaFrame( 1, 1 ) )
			},
			new Gradient(
				new ColorFrame( 0, "#8B0000" ),
				new ColorFrame( 0.33f, "#FF0000" ),
				new ColorFrame( 0.66f, "#FFA500" ),
				new ColorFrame( 1, "#FFFF00" ) )
			{
				Alphas = ImmutableList<AlphaFrame>.Empty.Add( new AlphaFrame( 0, 1 ) ).Add( new AlphaFrame( 1, 1 ) )
			},
			new Gradient( new ColorFrame( 0, "#FFFFFF" ), new ColorFrame( 1, "#FFFFFF" ) )
			{
				Alphas = ImmutableList<AlphaFrame>.Empty.Add( new AlphaFrame( 0, 0 ) ).Add( new AlphaFrame( 1, 1 ) )
			},
			new Gradient( new ColorFrame( 0, "#000000" ), new ColorFrame( 1, "#000000" ) )
			{
				Alphas = ImmutableList<AlphaFrame>.Empty.Add( new AlphaFrame( 0, 0 ) ).Add( new AlphaFrame( 1, 1 ) )
			}
		];
		Rebuild();
	}
	void SaveChanges() => EditorCookie.Set( _cookie, _userList );
	public void Rebuild()
	{
		_grid.DeleteChildren();
		foreach ( var preset in _defaultList.Select( x => new GradientPreset( x, true ) ) )
			_grid.AddChild( new GradientPresetItem( preset, OnPresetClicked, ShowPresetContext ) );
		foreach ( var preset in _userList.Select( x => new GradientPreset( x, false ) ) )
			_grid.AddChild( new GradientPresetItem( preset, OnPresetClicked, ShowPresetContext ) );
		var add = new GradientPresetItem();
		add.AddClass( "add" );
		add.OnClicked = () =>
		{
			_userList.Add( GetCurrentValue?.Invoke() ?? default );
			Rebuild();
			SaveChanges();
		};
		_grid.AddChild( add );
	}
	void OnPresetClicked( GradientPreset preset ) => OnPresetSelected?.Invoke( preset.gradient );
	void ShowPresetContext( GradientPreset preset )
	{
		var menu = new Menu();
		menu.AddOption( "Copy", "content_copy", () =>
		{
			EditorUtility.Clipboard.Copy( JsonSerializer.Serialize( preset.gradient ) );
		} );
		var clipboard = EditorUtility.Clipboard.Paste();
		var paste = menu.AddOption( "Paste", "content_paste", () =>
		{
			var value = JsonSerializer.Deserialize<Gradient>( clipboard );
			var i = _userList.IndexOf( preset.gradient );
			_userList.Remove( preset.gradient );
			_userList.Insert( i, value );
			Rebuild();
			SaveChanges();
		} );
		try
		{
			JsonSerializer.Deserialize<Gradient>( clipboard );
		}
		catch
		{
			paste.Enabled = false;
		}
		paste.Enabled &= !preset.isDefault;
		menu.AddSeparator();
		menu.AddOption( "Replace", "refresh", () =>
		{
			var i = _userList.IndexOf( preset.gradient );
			_userList.Remove( preset.gradient );
			_userList.Insert( i, GetCurrentValue?.Invoke() ?? default );
			Rebuild();
			SaveChanges();
		} ).Enabled = !preset.isDefault;
		menu.AddSeparator();
		menu.AddOption( "Delete", "delete", () =>
		{
			_userList.Remove( preset.gradient );
			Rebuild();
			SaveChanges();
		} ).Enabled = !preset.isDefault;
		menu.OpenAtCursor( this );
	}
}
[StyleSheet( "GradientPresetsPanel.cs.scss" )]
public sealed class GradientPresetItem : Panel
{
	GradientPresetsPanel.GradientPreset _preset;
	Action<GradientPresetsPanel.GradientPreset> _onContext;
	Image _image;
	int _lastWidth;
	public Action OnClicked;
	public GradientPresetItem( GradientPresetsPanel.GradientPreset preset, Action<GradientPresetsPanel.GradientPreset> onClicked, Action<GradientPresetsPanel.GradientPreset> onContext )
	{
		_preset = preset;
		_onContext = onContext;
		OnClicked = () => onClicked( preset );
		AddEventListener( "onmousedown", OnMouseDown );
		_image = AddChild<Image>( "fill" );
	}
	public GradientPresetItem()
	{
		AddEventListener( "onmousedown", OnMouseDown );
		var label = AddChild<Rinten.UI.Label>( "plus" );
		label.Text = "+";
	}
	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );
		UpdateTexture();
	}
	public override void Tick()
	{
		base.Tick();
		if ( _preset is null || _image is null || !_image.IsValid() )
			return;
		var width = (int)Box.RectInner.Width;
		if ( width < 1 )
			width = (int)Box.Rect.Width;
		if ( width == _lastWidth && _image.Texture is not null )
			return;
		_lastWidth = width;
		UpdateTexture();
	}
	void UpdateTexture()
	{
		if ( _preset is null || _image is null || !_image.IsValid() )
			return;
		var width = (int)Box.RectInner.Width;
		if ( width < 1 )
			width = (int)Box.Rect.Width;
		if ( width < 1 )
			return;
		GradientPaint.ApplyTexture( _image, _preset.gradient, width );
	}
	void OnMouseDown( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe )
			return;
		if ( mpe.MouseButton == MouseButtons.Right && _preset is { } preset )
		{
			_onContext?.Invoke( preset );
			e.StopPropagation();
			return;
		}
		if ( mpe.MouseButton != MouseButtons.Left )
			return;
		OnClicked?.Invoke();
		e.StopPropagation();
	}
}
