namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>Inspector editor for enum properties as a button group or dropdown.</summary>
[StyleSheet( "EnumControl.cs.scss" )]
[CustomEditor( typeof( Enum ) )]
public class EnumControl : BaseControl
{
	public enum EnumDisplayMode
	{
		Auto,
		ButtonGroup,
		Dropdown,
	}

	public override bool SupportsMultiEdit => true;

	public EnumDisplayMode DisplayMode { get; set; } = EnumDisplayMode.Auto;

	Panel _host;
	Panel _input;
	EnumDescription _enumDescription;
	EnumDescription.Entry[] _entries;
	bool _isFlagsMode;
	long? _cachedFlagsValue;

	public EnumControl()
	{
		ShowBackground = false;

		_host = AddChild<Panel>( "host" );
	}

	public override void Rebuild()
	{
		_host.DeleteChildren();
		_input = null;
		_entries = null;
		_enumDescription = null;
		_isFlagsMode = false;
		_cachedFlagsValue = null;

		if ( Property is null )
			return;

		var enumType = Property.NullableType ?? Property.PropertyType;
		if ( !enumType.IsEnum )
			return;

		var options = EditorTypeLibrary.GetEnumDescription( enumType );
		if ( options is null )
		{
			Log.Warning( $"Couldn't get enum description for {enumType}" );
			return;
		}

		_enumDescription = options;

		if ( Property.HasAttribute<FlagsAttribute>() || EditorTypeLibrary.GetType( enumType )?.HasAttribute<FlagsAttribute>() == true )
		{
			_isFlagsMode = true;
			_entries = FlagsEnumHelper.GetFlagEntries( options );
		}
		else
		{
			_entries = options.Where( x => x.Browsable ).ToArray();
		}

		if ( _entries.Length == 0 )
			return;

		if ( ResolveDisplayMode( _entries.Length ) == EnumDisplayMode.Dropdown )
			CreateDropdown();
		else
			CreateButtonGroup();

		_cachedFlagsValue = _isFlagsMode ? GetValue() : null;
		ApplyReadOnlyToHost();
	}

	public override void Tick()
	{
		base.Tick();

		if ( Property is null || _input is null )
			return;

		if ( _isFlagsMode )
		{
			var value = GetValue();
			if ( _cachedFlagsValue == value )
				return;

			_cachedFlagsValue = value;
			SyncFlagsInput();
			return;
		}

		var objectValue = Property.GetValue<object>();

		switch ( _input )
		{
			case ButtonGroup buttonGroup:
				if ( !Equals( buttonGroup.Value, objectValue ) )
					buttonGroup.Value = objectValue;
				break;

			case EnumDropDown dropdown:
				if ( !Equals( dropdown.Value, objectValue ) )
					dropdown.Value = objectValue;
				break;
		}
	}

	protected override void OnReadOnlyChanged()
	{
		ApplyReadOnlyToHost();
	}

	EnumDisplayMode ResolveDisplayMode( int browsableCount )
	{
		if ( DisplayMode != EnumDisplayMode.Auto )
			return DisplayMode;

		if ( Property.HasAttribute<EnumButtonGroupAttribute>() )
			return EnumDisplayMode.ButtonGroup;

		if ( Property.HasAttribute<EnumDropdownAttribute>() )
			return EnumDisplayMode.Dropdown;

		return browsableCount <= 3 ? EnumDisplayMode.ButtonGroup : EnumDisplayMode.Dropdown;
	}

	void CreateDropdown()
	{
		var builtOptions = BuildOptions();

		if ( _isFlagsMode )
		{
			var flagsDropdown = _host.AddChild<EnumFlagsDropDown>();
			flagsDropdown.Bind( _entries, _enumDescription, GetValue, OnFlagEntryClicked );
			_input = flagsDropdown;
			return;
		}

		var dropdown = _host.AddChild<EnumDropDown>();
		dropdown.Options = builtOptions;
		dropdown.Value = Property.GetValue<object>();
		dropdown.ValueChanged = _ => OnDropdownChanged( dropdown );
		_input = dropdown;
	}

	void CreateButtonGroup()
	{
		var builtOptions = BuildOptions();

		if ( _isFlagsMode )
		{
			var flagsButtons = _host.AddChild<EnumFlagsButtonGroup>();
			flagsButtons.AddClass( "flags" );
			flagsButtons.Bind( _entries, GetValue, OnFlagEntryClicked );
			_input = flagsButtons;
			return;
		}

		var buttonGroup = _host.AddChild<ButtonGroup>();
		buttonGroup.Options = builtOptions;
		buttonGroup.Value = Property.GetValue<object>();
		buttonGroup.ValueChanged = OnButtonGroupChanged;
		_input = buttonGroup;
	}

	List<Option> BuildOptions()
	{
		return _entries
			.Select( x => new Option( x.Title ?? x.Name, x.Icon, x.ObjectValue ) )
			.ToList();
	}

	void OnDropdownChanged( EnumDropDown dropdown )
	{
		if ( IsReadOnly || Property is null || dropdown.Selected is null )
			return;

		Property.SetValue( dropdown.Selected.Value );
	}

	void OnButtonGroupChanged( object value )
	{
		if ( IsReadOnly || Property is null )
			return;

		//Skip writes of an unchanged value (matched by name), as the group reports one when built
		var current = Property.GetValue<object>( null );

		if ( Equals( current, value ) ||
			string.Equals( current?.ToString(), value?.ToString(), System.StringComparison.OrdinalIgnoreCase ) )
			return;

		Property.SetValue( value );
	}

	void OnFlagEntryClicked( EnumDescription.Entry entry )
	{
		if ( IsReadOnly || Property is null )
			return;

		var newValue = FlagsEnumHelper.ToggleFlag( GetValue(), entry );
		FlagsEnumHelper.SetFlagValue( Property, newValue );
		_cachedFlagsValue = newValue;
		SyncFlagsInput();
	}

	void SyncFlagsInput()
	{
		switch ( _input )
		{
			case EnumFlagsButtonGroup flagsButtons:
				flagsButtons.SyncSelection();
				break;

			case EnumFlagsDropDown flagsDropdown:
				flagsDropdown.RefreshDisplay();
				flagsDropdown.SyncPopupSelection();
				break;
		}
	}

	long GetValue()
	{
		var raw = Property.GetValue<object>();
		return raw is null ? 0 : Convert.ToInt64( raw );
	}

	void ApplyReadOnlyToHost()
	{
		if ( _input is ButtonGroup or EnumFlagsButtonGroup )
		{
			foreach ( var button in _input.ChildrenOfType<Button>() )
				button.Disabled = IsReadOnly;
		}

		if ( _input is EnumDropDown dropdown )
		{
			dropdown.Disabled = IsReadOnly;
			dropdown.AcceptsFocus = !IsReadOnly;
		}
		else if ( _input is EnumFlagsDropDown flagsDropdown )
		{
			flagsDropdown.Disabled = IsReadOnly;
			flagsDropdown.AcceptsFocus = !IsReadOnly;
		}
	}
}

static class FlagsEnumHelper
{
	public static EnumDescription.Entry[] GetFlagEntries( EnumDescription description )
	{
		var entries = new List<EnumDescription.Entry>();

		var none = description.GetEntry( 0L );
		if ( none.Name is not null && none.Browsable )
			entries.Add( none );

		foreach ( var entry in description.Unique )
		{
			if ( entry.Browsable )
				entries.Add( entry );
		}

		return entries.ToArray();
	}

	public static void SetFlagValue( SerializedProperty property, long value )
	{
		var enumType = property.NullableType ?? property.PropertyType;
		property.SetValue( Enum.ToObject( enumType, value ) );
	}

	public static bool HasFlag( long value, EnumDescription.Entry entry )
	{
		if ( entry.IntegerValue == 0 )
			return value == 0;

		return (value & entry.IntegerValue) == entry.IntegerValue;
	}

	public static long ToggleFlag( long value, EnumDescription.Entry entry )
	{
		if ( entry.IntegerValue == 0 )
			return 0;

		if ( (value & entry.IntegerValue) != 0 )
			return value & ~entry.IntegerValue;

		return value | entry.IntegerValue;
	}

	public static string GetDisplayText( EnumDescription enumDescription, long value )
	{
		if ( value == 0 )
		{
			var none = enumDescription.GetEntry( 0L );
			if ( none.Name is not null )
				return none.Title ?? none.Name;
		}

		var entries = enumDescription.GetEntries( value );
		return entries.Length > 0
			? string.Join( ", ", entries.Select( x => x.Title ?? x.Name ) )
			: "";
	}

	internal static void SetButtonSelected( Panel panel, bool selected )
	{
		if ( panel is not Button button )
			return;

		button.Active = selected;
		button.SetClass( "active", selected );
	}
}

sealed class EnumFlagsButtonGroup : Panel
{
	EnumDescription.Entry[] _entries;
	Func<long> _getValue;
	Action<EnumDescription.Entry> _onClick;

	public EnumFlagsButtonGroup()
	{
		ElementName = "buttongroup";
	}

	public void Bind( EnumDescription.Entry[] entries, Func<long> getValue, Action<EnumDescription.Entry> onClick )
	{
		_entries = entries;
		_getValue = getValue;
		_onClick = onClick;
		Rebuild();
	}

	public void SyncSelection()
	{
		if ( _entries is null || _getValue is null )
			return;

		var value = _getValue();

		for ( var i = 0; i < _entries.Length && i < ChildrenCount; i++ )
		{
			if ( GetChild( i ) is not Button btn )
				continue;

			var selected = FlagsEnumHelper.HasFlag( value, _entries[i] );
			if ( btn.Active == selected )
				continue;

			FlagsEnumHelper.SetButtonSelected( btn, selected );
		}
	}

	void Rebuild()
	{
		DeleteChildren();

		if ( _entries is null )
			return;

		foreach ( var entry in _entries )
		{
			var e = entry;
			var btn = AddChild( new Button( e.Title ?? e.Name, () => _onClick?.Invoke( e ) ) );

			if ( !string.IsNullOrWhiteSpace( e.Icon ) )
				btn.Icon = e.Icon;
		}

		SyncSelection();
	}
}

sealed class EnumDropDown : DropDown
{
	public override void Open()
	{
		Popup = new EnumControlPopup();
		Popup.SetPositioning( this, Popup.PositionMode.BelowStretch, 0.0f );
		Popup.CloseWhenParentIsHidden = true;
		Popup.AddClass( "flat-top" );

		if ( BuildOptions != null )
			Options = BuildOptions.Invoke();

		foreach ( var option in Options )
		{
			var o = Popup.AddOption( option.Title, option.Icon, () => Select( option ) );
			var selected = Selected is not null && Equals( option.Value, Selected.Value );
			FlagsEnumHelper.SetButtonSelected( o, selected );
		}

		Popup.SyncAnchor();
	}
}

sealed class EnumFlagsDropDown : DropDown
{
	EnumDescription.Entry[] _entries;
	EnumDescription _enumDescription;
	Func<long> _getValue;
	Action<EnumDescription.Entry> _onClick;

	public void Bind( EnumDescription.Entry[] entries, EnumDescription enumDescription, Func<long> getValue, Action<EnumDescription.Entry> onClick )
	{
		_entries = entries;
		_enumDescription = enumDescription;
		_getValue = getValue;
		_onClick = onClick;
		Options = entries.Select( x => new Option( x.Title ?? x.Name, x.Icon, x.ObjectValue ) ).ToList();
		RefreshDisplay();
	}

	public void RefreshDisplay()
	{
		if ( _enumDescription is null || _getValue is null )
			return;

		var text = FlagsEnumHelper.GetDisplayText( _enumDescription, _getValue() );
		if ( Text == text )
			return;

		Text = text;
		Icon = null;
	}

	public override void Open()
	{
		Popup = new EnumControlPopup();
		Popup.SetPositioning( this, Popup.PositionMode.BelowStretch, 0.0f );
		Popup.CloseWhenParentIsHidden = true;
		Popup.AddClass( "flat-top" );

		var value = _getValue();

		foreach ( var entry in _entries )
		{
			var e = entry;
			var item = Popup.AddChild( new Button( e.Title ?? e.Name, e.Icon, () =>
			{
				_onClick?.Invoke( e );
				RefreshDisplay();
				SyncPopupSelection();
			} ) );

			FlagsEnumHelper.SetButtonSelected( item, FlagsEnumHelper.HasFlag( value, e ) );
		}

		Popup.SyncAnchor();
	}

	public void SyncPopupSelection()
	{
		if ( !Popup.IsValid() || _entries is null || _getValue is null )
			return;

		var value = _getValue();

		for ( var i = 0; i < _entries.Length && i < Popup.ChildrenCount; i++ )
		{
			if ( Popup.GetChild( i ) is not Button btn )
				continue;

			FlagsEnumHelper.SetButtonSelected( btn, FlagsEnumHelper.HasFlag( value, _entries[i] ) );
		}
	}
}

[StyleSheet( "EnumControlPopup.scss" )]
sealed class EnumControlPopup : Popup
{
}
