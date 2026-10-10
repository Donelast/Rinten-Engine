namespace Editor;

partial class EnumControlWidget
{
	
	public override void SyncSurfaceContent()
	{
		base.SyncSurfaceContent();


		SyncEnumDisplayColors();
	}

	private void SyncEnumDisplayColors()
	{
		if ( _enumDesc is null || !_titleLabel.IsValid() ) return;

		var color = IsControlHovered ? Theme.Ink : Theme.InkDim;
		if ( IsControlDisabled ) color = color.WithAlpha( 0.5f );

		_titleLabel.Color = SerializedProperty.IsMultipleDifferentValues ? Theme.MultipleValues : color;
		_arrowLabel.Color = color;

		if ( _valueIcon.IsValid() && !_valueIcon.Hidden )
		{
			_valueIcon.Foreground = color;
			_valueIcon.ForegroundActive = color;
		}
	}
}
