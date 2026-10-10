namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>Picks a type, and under it the generic arguments that type turns out to want.</summary>
[StyleSheet( "TypeControl.cs.scss" )]
[CustomEditor( typeof( Type ) )]
public sealed class TypeControl : BaseControl
{
	TypeField _field;

	public override bool SupportsMultiEdit => true;

	public TypeControl()
	{
		ShowBackground = false;
		MultipleRows = true;

	}

	public override void Rebuild()
	{
		DeleteChildren();
		_field = null;

		if ( Property is null ) return;

		_field = AddChild<TypeField>();
		_field.Constraint = TypeConstraint.For( Property );
		_field.ValueChanged = Assign;
		_field.Value = Current;
		_field.MultipleValues = Property.IsMultipleDifferentValues;
		_field.ReadOnly = IsReadOnly;
	}

	public override void Tick()
	{
		base.Tick();

		if ( Property is null || !_field.IsValid() ) return;

		_field.MultipleValues = Property.IsMultipleDifferentValues;

		//Somebody else may have written the property - undo, a script, another inspector.
		if ( !Property.IsMultipleDifferentValues )
			_field.Value = Current;
	}

	protected override void OnReadOnlyChanged()
	{
		if ( _field.IsValid() )
			_field.ReadOnly = IsReadOnly;
	}

	public override void OnPropertyContextMenu( Menu menu )
	{
		if ( Property is null ) return;

		var type = Current;

		menu.AddOption( "Copy Type Name", "content_copy",
			() => EditorUtility.Clipboard.Copy( TypeOptions.DisplayName( type ) ) ).Enabled = type is not null;

		menu.AddOption( "Clear", "backspace", () => Assign( null ) ).Enabled = !IsReadOnly && type is not null;
	}

	Type Current => Property?.GetValue<Type>();

	void Assign( Type type )
	{
		if ( IsReadOnly || Property is null ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue( type );
		Property.Parent?.NoteFinishEdit( Property );
	}
}

//An argument nobody has filled in stays the definition's own parameter, so Dictionary<string, ?> is a type in its own right and is stored as one.
/// <summary>One type, and a row of its own for each generic argument it takes.</summary>
public sealed class TypeField : Panel
{
	TypeFieldRow _row;
	Rinten.UI.Label _value;
	Panel _arguments;
	TypePickerPopup _popup;

	Type _type;
	bool _multipleValues;
	bool _readOnly;

	/// <summary>What this field's type is allowed to be.</summary>
	public TypeConstraint Constraint { get; set; }

	/// <summary>Called with the new type whenever somebody changes one, at any depth.</summary>
	public Action<Type> ValueChanged { get; set; }

	/// <summary>The generic parameter this field fills in, shown beside it.</summary>
	public string ParameterName { get; set; }

	public TypeField()
	{
		AddClass( "type-field" );
	}

	public Type Value
	{
		get => _type;
		set
		{
			if ( _type == value ) return;

			_type = value;
			Build();
		}
	}

	/// <summary>Whether the things being edited don't agree on a type.</summary>
	public bool MultipleValues
	{
		get => _multipleValues;
		set
		{
			if ( _multipleValues == value ) return;

			_multipleValues = value;
			SyncDisplay();
		}
	}

	public bool ReadOnly
	{
		get => _readOnly;
		set
		{
			if ( _readOnly == value ) return;

			_readOnly = value;
			SetClass( "readonly", value );

			foreach ( var child in _arguments?.ChildrenOfType<TypeField>() ?? [] )
				child.ReadOnly = value;
		}
	}

	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );

		if ( firstTime && !_row.IsValid() )
			Build();
	}

	public override void Tick()
	{
		base.Tick();

		if ( _popup is not null && !_popup.IsValid() )
			_popup = null;

		SetClass( "open", _popup is not null );
	}

	void Build()
	{
		DeleteChildren();

		_row = AddChild<TypeFieldRow>( "row" );
		_row.MouseDown = OnRowMouseDown;

		if ( !string.IsNullOrEmpty( ParameterName ) )
		{
			var name = _row.AddChild<Rinten.UI.Label>( "name" );
			name.Tokenize = false;
			name.Text = ParameterName;
		}

		var field = _row.AddChild<Panel>( "field" );

		_value = field.AddChild<Rinten.UI.Label>( "value" );
		_value.Tokenize = false;

		field.AddChild<IconPanel>( "indicator" ).Text = "expand_more";

		_arguments = AddChild<Panel>( "arguments" );

		BuildArguments();
		SyncDisplay();
	}

	//A row per generic argument, each picking a type of its own.
	void BuildArguments()
	{
		var arguments = TypeOptions.ArgumentsOf( _type );

		_arguments.SetClass( "hidden", arguments.Length == 0 );

		if ( arguments.Length == 0 ) return;

		var definition = _type.GetGenericTypeDefinition();
		var names = TypeOptions.ParameterNamesOf( _type );

		for ( var i = 0; i < arguments.Length; i++ )
		{
			var index = i;
			var field = _arguments.AddChild<TypeField>();

			field.ParameterName = names[i];
			field.Constraint = TypeConstraint.For( definition, index );
			field.ValueChanged = type => SetArgument( index, type );
			field.Value = arguments[i];
			field.ReadOnly = _readOnly;
		}
	}

	void SetArgument( int index, Type argument )
	{
		var updated = TypeOptions.WithArgument( _type, index, argument );

		if ( updated == _type ) return;

		Value = updated;
		ValueChanged?.Invoke( updated );
	}

	void SyncDisplay()
	{
		if ( !_value.IsValid() ) return;

		_value.SetClass( "multiple", _multipleValues );
		_value.Text = _multipleValues ? "Multiple Values" : TypeOptions.DisplayName( _type );
		_value.SetClass( "none", !_multipleValues && _type is null );
	}

	void OnRowMouseDown( MousePanelEvent e )
	{
		if ( _readOnly || e.MouseButton != MouseButtons.Left ) return;

		OpenPicker();
	}

	//A generic argument answers for itself; the outermost field is the property, so the property menu opens instead.
	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );

		if ( !Ancestors.OfType<TypeField>().Any() ) return;

		OpenContextMenu();
		e.StopPropagation();
	}

	void OpenPicker()
	{
		if ( _popup is { IsValid: true } )
		{
			_popup.Delete();
			_popup = null;
			return;
		}

		_popup = TypePickerPopup.Open( _row, _type, Constraint, Pick );
	}

	void OpenContextMenu()
	{
		var menu = new Menu();

		menu.AddOption( "Copy Type Name", "content_copy",
			() => EditorUtility.Clipboard.Copy( TypeOptions.DisplayName( _type ) ) ).Enabled = _type is not null;

		menu.AddOption( "Clear", "backspace", () => Pick( null ) ).Enabled = !_readOnly && _type is not null;

		menu.OpenAtCursor( this );
	}

	void Pick( Type type )
	{
		if ( _readOnly ) return;
		if ( _type == type ) return;

		Value = type;
		ValueChanged?.Invoke( type );
	}

	//The clickable half of a field, which keeps a press to the field it was made on rather than letting it reach the one above.
	private sealed class TypeFieldRow : Panel
	{
		public Action<MousePanelEvent> MouseDown { get; set; }

		protected override void OnMouseDown( MousePanelEvent e )
		{
			base.OnMouseDown( e );

			MouseDown?.Invoke( e );
			e.StopPropagation();
		}
	}
}
