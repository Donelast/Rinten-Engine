namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>Picks one of the game's input actions by name.</summary>
[StyleSheet( "InputActionControl.cs.scss" )]
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( InputActionAttribute )] )]
public sealed class InputActionControl : BaseControl
{
	readonly Panel _field;
	readonly Rinten.UI.Label _value;

	InputActionPopup _popup;

	public override bool SupportsMultiEdit => true;

	public InputActionControl()
	{
		ShowBackground = false;

		_field = AddChild<Panel>( "field" );

		_value = _field.AddChild<Rinten.UI.Label>( "value" );
		_value.Tokenize = false;

		var indicator = _field.AddChild<IconPanel>( "indicator" );
		indicator.Text = "expand_more";
	}

	public override void Rebuild()
	{
		SyncDisplay();
	}

	public override void Tick()
	{
		base.Tick();

		if ( _popup is not null && !_popup.IsValid() )
			_popup = null;

		SetClass( "open", _popup is not null );
		SyncDisplay();
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( Property is null ) return;

		if ( IsReadOnly || e.MouseButton != MouseButtons.Left ) return;

		OpenPicker();
		e.StopPropagation();
	}

	public override void OnPropertyContextMenu( Menu menu )
	{
		if ( Property is null ) return;

		menu.AddOption( "Clear", "backspace", () => Assign( "" ) )
			.Enabled = !IsReadOnly && !string.IsNullOrEmpty( Property.As.String );
	}

	void SyncDisplay()
	{
		if ( Property is null ) return;

		if ( Property.IsMultipleDifferentValues )
		{
			_value.SetClass( "multiple", true );
			_value.Text = "Multiple Values";
			return;
		}

		_value.SetClass( "multiple", false );

		var action = Property.As.String;
		_value.Text = string.IsNullOrWhiteSpace( action ) ? "None" : action.ToLower();
	}

	void OpenPicker()
	{
		if ( _popup is { IsValid: true } )
		{
			_popup.Delete();
			_popup = null;
			return;
		}

		_popup = InputActionPopup.Open( this, Assign );
	}

	void Assign( string action )
	{
		if ( IsReadOnly || Property is null ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.As.String = action;
		Property.Parent?.NoteFinishEdit( Property );
	}
}
