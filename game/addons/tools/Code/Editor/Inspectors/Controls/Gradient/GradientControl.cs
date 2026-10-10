namespace Editor.Inspectors;
[StyleSheet( "GradientControl.cs.scss" )]
[CustomEditor( typeof( Gradient ) )]
public class GradientControl : BaseControl
{
	public override bool SupportsMultiEdit => true;
	GradientSwatchFill _preview;
	public GradientControl()
	{
		ShowBackground = false;
	}
	public override void Rebuild()
	{
		DeleteChildren();
		_preview = AddChild<GradientSwatchFill>( "preview" );
		_preview.AddEventListener( "onmousedown", OnMouseDown );
		RefreshValue();
	}
	public override void Tick()
	{
		base.Tick();
		RefreshValue();
	}
	void RefreshValue()
	{
		if ( _preview is null || Property is null )
			return;
		if ( Property.IsMultipleDifferentValues )
		{
			_preview.Multiple = true;
			return;
		}
		_preview.Multiple = false;
		_preview.Value = Property.GetValue<Gradient>();
	}
	void OnMouseDown( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe || Property is null )
			return;
		if ( mpe.MouseButton != MouseButtons.Left )
			return;
		if ( IsReadOnly )
			return;
		OpenEditor();
		e.StopPropagation();
	}

	public override void OnPropertyContextMenu( Menu menu )
	{
		if ( Property is null ) return;

		menu.AddOption( "Open in Editor", "edit", OpenEditor ).Enabled = !IsReadOnly;
	}
	void OpenEditor()
	{
		var value = Property.GetValue<Gradient>();
		GradientEditorWindow.Open( Property, value, x =>
		{
			if ( Property.GetValue<Gradient>().Equals( x ) )
				return;
			Property.SetValue( x );
		} );
	}
}
