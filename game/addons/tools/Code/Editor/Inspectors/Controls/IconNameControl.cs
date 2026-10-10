namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>Picks a Material Icons glyph by name.</summary>
[StyleSheet( "IconNameControl.cs.scss" )]
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( IconNameAttribute )] )]
public sealed class IconNameControl : BaseControl
{
	readonly Panel _field;
	readonly IconPanel _glyph;
	readonly Rinten.UI.Label _name;

	IconPickerPopup _popup;

	public override bool SupportsMultiEdit => true;

	public IconNameControl()
	{
		ShowBackground = false;

		_field = AddChild<Panel>( "field" );

		//The box is what gets the square and the border; the glyph inside it is left to size itself, which is the only way flex can centre it.
		_glyph = _field.AddChild<Panel>( "glyph" ).AddChild<IconPanel>();

		_name = _field.AddChild<Rinten.UI.Label>( "name" );
		_name.Tokenize = false;
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

		menu.AddOption( "Clear", "clear", () => Assign( "" ) )
			.Enabled = !IsReadOnly && !string.IsNullOrEmpty( Property.As.String );
	}

	void SyncDisplay()
	{
		if ( Property is null ) return;

		if ( Property.IsMultipleDifferentValues )
		{
			_field.SetClass( "empty", true );
			_name.SetClass( "multiple", true );
			_name.Text = "Multiple Values";
			_glyph.Text = "";
			return;
		}

		_name.SetClass( "multiple", false );

		var icon = Property.As.String;
		var empty = string.IsNullOrEmpty( icon );

		_field.SetClass( "empty", empty );
		_glyph.Text = empty ? "" : icon;
		_name.Text = empty ? "Icon" : icon;
	}

	void OpenPicker()
	{
		if ( _popup is { IsValid: true } )
		{
			_popup.Delete();
			_popup = null;
			return;
		}

		_popup = IconPickerPopup.Open( this, Property.As.String, Assign );
	}

	void Assign( string icon )
	{
		if ( IsReadOnly || Property is null ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.As.String = icon;
		Property.Parent?.NoteFinishEdit( Property );
	}
}
