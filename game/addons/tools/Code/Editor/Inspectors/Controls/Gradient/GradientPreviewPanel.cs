namespace Editor.Inspectors;
[StyleSheet( "GradientPreviewPanel.cs.scss" )]
public sealed class GradientPreviewPanel : Panel
{
	public Gradient Value { get; set; }
	public bool IsColorSelection { get; set; }
	public Action<Color> OnPickColor;
	Image _image;
	int _lastWidth;
	public GradientPreviewPanel()
	{
		AddChild<Panel>( "checker" );
		_image = AddChild<Image>( "fill" );
		AddEventListener( "onmousedown", OnMouseDown );
		AddEventListener( "onmouseenter", OnMouseEnter );
		AddEventListener( "onmouseleave", OnMouseLeave );
	}
	public void SetValue( Gradient value )
	{
		Value = value;
		UpdateTexture();
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
		GradientPaint.ApplyTexture( _image, Value, width );
	}
	void OnMouseEnter( PanelEvent e )
	{
		SetClass( "picking", IsColorSelection );
	}
	void OnMouseLeave( PanelEvent e )
	{
		SetClass( "picking", false );
	}
	void OnMouseDown( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe || mpe.MouseButton != MouseButtons.Left )
			return;
		if ( !IsColorSelection )
			return;
		var rect = Box.RectInner;
		if ( rect.Width <= 0 )
			rect = Box.Rect;
		if ( rect.Width <= 0 )
			return;
		var local = ScreenPositionToPanelPosition( mpe.PointerPosition );
		var t = ((local.x - rect.Left) / rect.Width).Clamp( 0, 1 );
		OnPickColor?.Invoke( Value.Evaluate( t ) );
		e.StopPropagation();
	}
}
