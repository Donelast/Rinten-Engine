namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>A string somebody may type, with one button beside it that fills it in.</summary>
public abstract class TextButtonControl : BaseControl
{
	readonly TextEntry _entry;
	readonly ToolbarButton _button;

	public override bool SupportsMultiEdit => true;

	protected TextButtonControl()
	{
		ShowBackground = false;
		AddClass( "text-and-button" );

		_entry = AddChild<TextEntry>();
		_entry.OnTextEdited = OnEdited;

		_button = AddChild<ToolbarButton>( "action" );
		_button.OnPressed = () => { if ( !IsReadOnly ) OnButtonPressed(); };
	}

	//The icon on the button.
	protected abstract string ButtonIcon { get; }

	//What the pointer says the button does.
	protected abstract string ButtonToolTip { get; }

	//What pressing it does.
	protected abstract void OnButtonPressed();

	//The panel a popup should hang off.
	protected Panel ButtonPanel => _button;

	public override void Rebuild()
	{
		_button.Icon = ButtonIcon;
		_button.ToolTip = ButtonToolTip;

		if ( Property is not null && Property.TryGetAttribute<PlaceholderAttribute>( out var placeholder ) )
			_entry.Placeholder = placeholder.Value;

		SyncDisplay();
		ApplyDisabled();
	}

	public override void Tick()
	{
		base.Tick();

		SyncDisplay();
	}

	protected override void OnReadOnlyChanged() => ApplyDisabled();

	//Writes a value the button worked out, as one edit.
	protected void Assign( string value )
	{
		if ( IsReadOnly || Property is null ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.As.String = value;
		Property.Parent?.NoteFinishEdit( Property );
	}

	//What is in the field right now, typed or not.
	protected string Current => Property?.As.String ?? "";

	void OnEdited( string text )
	{
		if ( IsReadOnly || Property is null ) return;

		Property.As.String = text;
	}

	void SyncDisplay()
	{
		if ( Property is null || _entry.HasFocus ) return;

		_entry.SetClass( "multiple", Property.IsMultipleDifferentValues );
		_entry.Value = Property.IsMultipleDifferentValues ? "Multiple Values" : Current;
	}

	void ApplyDisabled()
	{
		if ( _entry is null ) return;

		_entry.Disabled = IsReadOnly;
		_button.Disabled = IsReadOnly;
	}
}
