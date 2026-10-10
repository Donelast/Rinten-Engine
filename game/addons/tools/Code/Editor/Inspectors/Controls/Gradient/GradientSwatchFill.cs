namespace Editor.Inspectors;
public sealed class GradientSwatchFill : Panel
{
	Gradient _value;
	bool _multiple;
	Image _image;
	int _lastWidth;
	public Gradient Value
	{
		get => _value;
		set
		{
			if ( value.Equals( _value ) )
				return;
			_value = value;
			UpdateTexture();
		}
	}
	public bool Multiple
	{
		get => _multiple;
		set
		{
			if ( _multiple == value )
				return;
			_multiple = value;
			UpdateTexture();
		}
	}
	public GradientSwatchFill()
	{
		AddChild<Panel>( "checker" );
		_image = AddChild<Image>( "fill" );
	}
	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );
		UpdateTexture();
	}
	public override void Tick()
	{
		base.Tick();
		var width = (int)Box.RectInner.Width;
		if ( width < 1 )
			width = (int)Box.Rect.Width;
		if ( width == _lastWidth )
			return;
		_lastWidth = width;
		UpdateTexture();
	}
	void UpdateTexture()
	{
		if ( _image is null || !_image.IsValid() )
			return;
		var width = (int)Box.RectInner.Width;
		if ( width < 1 )
			width = (int)Box.Rect.Width;
		if ( width < 1 )
			return;
		if ( _multiple )
		{
			_image.Texture = null;
			SetClass( "multiple", true );
			return;
		}
		SetClass( "multiple", false );
		GradientPaint.ApplyTexture( _image, _value, width );
	}
}
