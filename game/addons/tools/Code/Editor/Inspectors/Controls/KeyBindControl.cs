namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>The key or combo an action watches for, caught by pressing it.</summary>
[StyleSheet( "KeyBindControl.cs.scss" )]
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( KeyBindAttribute )] )]
public sealed class KeyBindControl : BaseControl, IWantsRawKeys
{
	readonly Rinten.UI.Label _label;

	bool _trapping;
	string _shown;

	public override bool SupportsMultiEdit => true;

	/// <summary>While trapping, every key is this control's - including the editor's own shortcuts.</summary>
	public bool WantsRawKeys => _trapping;

	/// <summary>Ctrl+D is a binding here, not "duplicate the selection".</summary>
	public bool WantsModifiedKeys => _trapping;

	public KeyBindControl()
	{
		ShowBackground = false;
		AddClass( "keybind" );

		AcceptsFocus = true;

		_label = AddChild<Rinten.UI.Label>( "value" );
		_label.Tokenize = false;
	}

	public override void Rebuild()
	{
		_shown = null;
		SyncDisplay();
	}

	public override void Tick()
	{
		base.Tick();

		SetClass( "trapping", _trapping );
		SyncDisplay();
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( IsReadOnly ) return;

		//A press while trapping is the binding itself, not a way out of it.
		if ( _trapping )
		{
			Assign( WithModifiers( MouseName( e.MouseButton ), Modifiers() ) );
			e.StopPropagation();
			return;
		}

		_trapping = true;
		_shown = null;
		Focus();
		e.StopPropagation();
	}

	public override void OnMouseWheel( Vector2 value )
	{
		if ( !_trapping )
		{
			base.OnMouseWheel( value );
			return;
		}

		//Positive is scrolling down, per Panel.OnMouseWheel.
		Assign( WithModifiers( value.y > 0 ? "MWHEELDOWN" : "MWHEELUP", Modifiers() ) );
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( !_trapping ) return;

		e.StopPropagation = true;

		//Bound on release, the way the widget did: a modifier held down on the way in is part of the combo rather than a binding of its own.
		if ( e.Pressed ) return;

		//Escape unbinds: there is no key that means "no key", so the way out of catching is also the way to clear what was caught.
		if ( string.Equals( e.Button, "escape", StringComparison.OrdinalIgnoreCase )
			|| string.Equals( e.Button, "esc", StringComparison.OrdinalIgnoreCase ) )
		{
			Assign( "" );
			return;
		}

		var name = KeyName( e.Button );
		if ( string.IsNullOrEmpty( name ) ) return;

		Assign( WithModifiers( name, e.KeyboardModifiers ) );
	}

	protected override void OnBlur( PanelEvent e )
	{
		base.OnBlur( e );

		Stop();
	}

	//The bind system splits on + and compares names without case, so this spells them its way.
	static string KeyName( string button )
	{
		var name = button?.ToUpperInvariant();

		return name switch
		{
			"LEFT" => "LEFTARROW",
			"RIGHT" => "RIGHTARROW",
			"UP" => "UPARROW",
			"DOWN" => "DOWNARROW",
			_ => name
		};
	}

	static string MouseName( MouseButtons button ) => button switch
	{
		MouseButtons.Middle => "MOUSE3",
		MouseButtons.Right => "MOUSE2",
		MouseButtons.Back => "MOUSE4",
		MouseButtons.Forward => "MOUSE5",
		_ => "MOUSE1"
	};

	static string WithModifiers( string name, KeyboardModifiers modifiers )
	{
		if ( name.Contains( "CTRL" ) || name.Contains( "ALT" ) || name.Contains( "SHIFT" ) )
			return name;

		if ( modifiers.Contains( KeyboardModifiers.Shift ) ) name = "SHIFT+" + name;
		if ( modifiers.Contains( KeyboardModifiers.Alt ) ) name = "ALT+" + name;
		if ( modifiers.Contains( KeyboardModifiers.Ctrl ) ) name = "CTRL+" + name;

		return name;
	}

	//A mouse event carries no modifiers, so they are asked of the keyboard itself.
	static KeyboardModifiers Modifiers() => Application.KeyboardModifiers;

	void Stop()
	{
		if ( !_trapping ) return;

		_trapping = false;
		SetClass( "trapping", false );
	}

	void Assign( string value )
	{
		Stop();

		if ( IsReadOnly || Property is null ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.As.String = value;
		Property.Parent?.NoteFinishEdit( Property );
	}

	void SyncDisplay()
	{
		if ( Property is null ) return;

		var text = _trapping
			? "PRESS KEY..."
			: Property.IsMultipleDifferentValues ? "Multiple Values" : Bound();

		if ( _shown == text ) return;

		_shown = text;
		_label.Text = text;
		_label.SetClass( "multiple", !_trapping && Property.IsMultipleDifferentValues );
		_label.SetClass( "unbound", !_trapping && string.IsNullOrEmpty( Property.As.String ) );
	}

	string Bound()
	{
		var value = Property.As.String;

		return string.IsNullOrEmpty( value ) ? "Unbound" : value;
	}
}
