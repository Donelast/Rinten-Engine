namespace Editor.Inspectors;

[StyleSheet( "NullableControl.cs.scss" )]
public class NullableControl : BaseControl
{
	ToolbarButton _toggle;
	Rinten.UI.Panel _body;
	BaseControl _inner;
	bool _lastIsNull;

	public override bool SupportsMultiEdit => true;

	public NullableControl()
	{
		ShowBackground = false;
		MultipleRows = true;
	}

	public override void Rebuild()
	{
		DeleteChildren();

		if ( Property is null )
			return;

		_toggle = AddChild<ToolbarButton>();
		_toggle.ToolTip = "Has Value";
		_toggle.OnPressed = ToggleNull;

		_body = AddChild<Rinten.UI.Panel>( "body" );

		_inner = PanelControlFactory.CreateFor( Property, wrapNullable: false );
		if ( _inner is not null )
		{
			_inner.Property = Property;
			_body.AddChild( _inner );
		}

		SetClass( "inline-body", _inner is InlineEditorControl );
		UpdateNullState();
	}

	public override void Tick()
	{
		base.Tick();

		if ( Property is null )
			return;

		if ( Property.IsNull != _lastIsNull )
			UpdateNullState();
	}

	void ToggleNull()
	{
		if ( Property is null || IsPropertyReadOnly() )
			return;

		Property.SetNullState( !Property.IsNull );
		UpdateNullState();
	}

	void UpdateNullState()
	{
		if ( Property is null || _toggle is null )
			return;

		var multiple = Property.IsMultipleDifferentValues;
		var isNull = Property.IsNull && !multiple;
		var nullChanged = Property.IsNull != _lastIsNull;

		_lastIsNull = Property.IsNull;
		_toggle.Icon = isNull ? "radio_button_unchecked" : "circle";
		_toggle.Tint = multiple ? Theme.MultipleValues : Theme.TextLight;
		_toggle.Disabled = IsPropertyReadOnly();
		_toggle.StateHasChanged();

		if ( _inner.IsValid() )
		{
			_inner.ReadOnly = Property.IsNull;
			if ( nullChanged )
				_inner.Rebuild();
		}
	}
}
