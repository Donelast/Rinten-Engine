using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.UiEditor;

/// <summary>Dock for the document's parameters and design-time values, saved beside the razor</summary>
[SkipHotload]
public sealed class Data : PanelDockHost
{
	readonly DataPanel _panel;

	public Data( UiEditorWindow window ) : base( null )
	{
		Name = "Data";
		WindowTitle = "Data";
		SetWindowIcon( "data_object" );
		MinimumWidth = 220;

		_panel = new DataPanel( window );

		SetContent( _panel );
	}

	public void SetDocument( RazorDocument document ) => _panel.Rebuild();

	public void NoteItemsChanged( string member ) => _panel.NoteItemsChanged( member );

	/// <summary>Open a list's items under its row, or close them.</summary>
	public void ToggleItems( string member ) => _panel.ToggleItems( member );
}

//The rows, one per member, and the field that adds one.
[StyleSheet( "Data.cs.scss" )]
internal sealed class DataPanel : EditorPanel
{
	readonly UiEditorWindow _window;
	readonly Panel _rows;

	//The type the rows were built for, so a hotload's new type builds them again.
	TypeDescription _builtFor;
	object _builtOn;

	public DataPanel( UiEditorWindow window )
	{
		_window = window;

		AddClass( "ui-data" );

		var bar = AddChild<PanelToolbar>();

		bar.AddIcon( "add", "Add a [Parameter] to the document's @code", () => _adder.SetClass( "hidden", !_adder.HasClass( "hidden" ) ) );
		bar.AddStretch();

		//The row that adds a property: a name, a type, and Add. Shown by the plus, gone again once it has added.
		_adder = AddChild<Panel>( "adder hidden" );

		var name = _adder.AddChild<TextField>( "name field" );
		name.Placeholder = "Name";

		var typeButton = _adder.AddChild<UILabel>( "choice" );
		typeButton.Tokenize = false;
		typeButton.Text = _newType;
		typeButton.AddEventListener( "onclick", () =>
		{
			var menu = new Menu( null );

			foreach ( var t in NewTypes )
			{
				var chosen = t;
				menu.AddOption( t, null, () => { _newType = chosen; typeButton.Text = chosen; } );
			}

			menu.OpenAtCursor();
		} );

		void Add()
		{
			if ( _window.AddProperty( name.Value, _newType ) )
			{
				name.Value = "";
				_adder.SetClass( "hidden", true );
			}
		}

		var go = _adder.AddChild<UILabel>( "go" );
		go.Tokenize = false;
		go.Text = "Add";
		go.AddEventListener( "onclick", Add );

		name.Submitted = _ => Add();

		var well = AddChild<Panel>( "well" );
		_rows = well.AddChild<Panel>( "rows" );

		Rebuild();
	}

	readonly Panel _adder;
	string _newType = "string";

	//The types a new property is offered in; anything else is typed into the code.
	static readonly string[] NewTypes = { "string", "int", "float", "bool", "Color", "Vector2", "List<string>", "List<float>", "List<int>" };

	public void Rebuild()
	{
		_rows.DeleteChildren( true );

		var document = _window.Document;
		var instance = _window.Instance;

		_builtFor = document?.Type;
		_builtOn = instance;

		if ( document?.Type is not { } type || instance is null )
		{
			var note = _rows.AddChild<UILabel>( "note" );
			note.Text = document is null ? "No file open" : "No instance yet - is the file compiled?";
			return;
		}

		var any = false;

		foreach ( var member in DesignData.Members( type ) )
		{
			any = true;

			var row = _rows.AddChild( new DataRow( _window, instance, member.name, member.type, member.get, member.set, member.sample ) );

			//A list opens into its items, each with its fields, under the row.
			if ( DesignData.IsList( member.type, out var itemType ) )
			{
				var block = _rows.AddChild( new ItemsBlock( _window, member.name, itemType ) );
				row.Expandable( () => block.Toggle(), block.IsOpen );
			}
		}

		if ( !any )
		{
			var note = _rows.AddChild<UILabel>( "note" );
			note.Text = "Nothing to set - add a [Parameter] above, or in @code.";
		}
	}

	public void ToggleItems( string member )
	{
		foreach ( var block in _rows.Children.OfType<ItemsBlock>() )
			if ( block.Member == member ) block.Toggle();

		Rebuild();
	}

	/// <summary>A list changed under a row: its items are shown again.</summary>
	public void NoteItemsChanged( string member )
	{
		foreach ( var block in _rows.Children.OfType<ItemsBlock>() )
			if ( block.Member == member ) block.Rebuild();

		foreach ( var row in _rows.Children.OfType<DataRow>() )
			row.Refresh();
	}

	/// <summary>A hotload gives the document a new type and a new instance; the rows follow.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( !ReferenceEquals( _builtFor, _window.Document?.Type ) || !ReferenceEquals( _builtOn, _window.Instance ) )
			Rebuild();
	}
}

//One member: its name, its type, its value as a field - or, for a list, how many samples it holds.
internal sealed class DataRow : Panel
{
	readonly UiEditorWindow _window;
	readonly object _instance;
	readonly string _name;
	readonly Type _type;
	readonly Func<object, object> _get;
	readonly Action<object, object> _set;
	readonly DesignDataAttribute _sample;

	//What keeps a written value: the design value of this member, or - for a field of a list's item - the whole list.
	readonly Action<object> _keep;

	readonly TextField _field;
	readonly UILabel _check;
	readonly UILabel _choice;
	readonly UILabel _reference;
	readonly Panel _swatch;
	readonly bool _isList;
	readonly Type _item;

	//A game object or a component: something the scene gives, not a value to type. A resource is typed, by its path.
	static bool IsReference( Type type )
		=> type.IsAssignableTo( typeof( GameObject ) ) || type.IsAssignableTo( typeof( Rinten.Component ) );

	static bool IsResource( Type type ) => type.IsAssignableTo( typeof( Resource ) );

	string _shown = "";

	readonly UILabel _chevron;

	/// <summary>Give the row a chevron that opens something under it.</summary>
	public void Expandable( Func<bool> toggle, bool open )
	{
		_chevron.SetClass( "hidden", false );
		_chevron.Text = open ? "expand_more" : "chevron_right";
		_chevron.AddEventListener( "onclick", () => _chevron.Text = toggle() ? "expand_more" : "chevron_right" );
	}

	public void Refresh() => Read();

	public DataRow( UiEditorWindow window, object instance, string name, Type type, Func<object, object> get, Action<object, object> set, DesignDataAttribute sample, Action<object> keep = null )
	{
		_window = window;
		_instance = instance;
		_name = name;
		_type = type;
		_get = get;
		_set = set;
		_sample = sample;
		_keep = keep ?? ( value => { _window.SetDesignValue( _name, value, _type ); _window.CanvasPanel?.Rerender(); } );
		_isList = DesignData.IsList( type, out _item );

		AddClass( "data-row" );

		_chevron = AddChild<UILabel>( "material-icon chevron hidden" );
		_chevron.Tokenize = false;

		var label = AddChild<UILabel>( "name" );
		label.Tokenize = false;
		label.Text = name;
		label.Tooltip = TypeName( type );

		//The widget that fits the type: a box to tick, a menu of names, a swatch beside a
		//colour, a reference that is only looked at, and a field for everything that is typed.
		if ( type == typeof( bool ) )
		{
			_check = AddChild<UILabel>( "material-icon check" );
			_check.Tokenize = false;
			_check.AddEventListener( "onclick", () => Put( !( _get( _instance ) is true ) ) );
		}
		else if ( type.IsEnum )
		{
			_choice = AddChild<UILabel>( "choice" );
			_choice.Tokenize = false;
			_choice.AddEventListener( "onclick", OpenChoices );
		}
		else if ( IsReference( type ) )
		{
			_reference = AddChild<UILabel>( "reference" );
			_reference.Tokenize = false;
			_reference.Tooltip = "A scene reference - set in the scene, not here";
		}
		else
		{
			if ( type == typeof( Color ) )
			{
				_swatch = AddChild<Panel>( "swatch" );
				_swatch.AddEventListener( "onclick", OpenColour );
			}

			_field = AddChild<TextField>( "value field" );
			_field.Placeholder = _isList ? "count" : IsResource( type ) ? "path, like scenes/main.scene" : TypeName( type );
			_field.Submitted = _ => Commit();
			_field.AddEventListener( "onblur", Commit );
		}

		var kind = AddChild<UILabel>( "kind" );
		kind.Tokenize = false;
		kind.Text = _isList ? $"[{TypeName( _item )}]" : TypeName( type );

		//Back to what the code says, and nothing kept.
		var reset = AddChild<UILabel>( "material-icon reset" );
		reset.Tokenize = false;
		reset.Text = "restart_alt";
		reset.Tooltip = "Forget the design value";
		reset.AddEventListener( "onclick", Reset );

		Read();
	}

	//The value on the instance, as text.
	void Read()
	{
		object value;

		try { value = _get( _instance ); }
		catch { value = null; }

		if ( _isList )
		{
			var count = value is System.Collections.ICollection c ? c.Count : value is System.Collections.IEnumerable e ? e.Cast<object>().Count() : 0;
			_shown = value is null ? "" : count.ToString();
		}
		else
		{
			_shown = Show( value );
		}

		if ( _field is not null ) _field.Value = _shown;

		if ( _check is not null )
		{
			var on = value is true;
			_check.Text = on ? "check_box" : "check_box_outline_blank";
			_check.SetClass( "on", on );
		}

		if ( _choice is not null ) _choice.Text = value?.ToString() ?? "–";
		if ( _reference is not null ) _reference.Text = value is null ? "none" : value is GameObject go ? go.Name : value is Rinten.Component comp ? $"{comp.GameObject?.Name}: {comp.GetType().Name}" : value.ToString();
		if ( _swatch is not null && value is Color colour ) _swatch.Style.BackgroundColor = colour;

		SetClass( "kept", _keepName is not null && ( _window.Design?.Has( _keepName ) ?? false ) );
	}

	//The design value this row's writes end up in; an item's field says its list.
	string _keepName => KeptAs ?? _name;

	public string KeptAs { get; init; }

	//Put a value on the instance, keep it, and show it.
	void Put( object value )
	{
		try
		{
			_set( _instance, value );
		}
		catch ( Exception e )
		{
			Log.Warning( $"UI editor: {_name} cannot take {value}: {e.Message}" );
			return;
		}

		_keep( value );
		Read();
	}

	void OpenChoices()
	{
		var menu = new Menu( null );

		foreach ( var value in Enum.GetValues( _type ) )
		{
			var chosen = value;
			menu.AddOption( value.ToString(), null, () => Put( chosen ) );
		}

		menu.OpenAtCursor();
	}

	void OpenColour()
	{
		var current = _get( _instance ) is Color c ? c : Color.White;

		Inspectors.ColorPickerPopup.Open( this, current, colour => Put( colour ), hasAlpha: true, hdr: false );
	}

	static string Show( object value )
	{
		if ( value is null ) return "";
		if ( value is string s ) return s;
		if ( value is Resource resource ) return resource.ResourcePath ?? "";
		if ( value is bool b ) return b ? "true" : "false";
		if ( value is float f ) return f.ToString( "0.###", System.Globalization.CultureInfo.InvariantCulture );
		if ( value is double d ) return d.ToString( "0.###", System.Globalization.CultureInfo.InvariantCulture );
		if ( value.GetType().IsPrimitive || value is Enum ) return value.ToString();

		try { return Json.Serialize( value ); }
		catch { return value.ToString(); }
	}

	void Commit()
	{
		if ( _field is null ) return;

		var text = _field.Value ?? "";
		if ( text == _shown ) return;

		object value;

		try
		{
			if ( _isList )
			{
				var count = int.TryParse( text.Trim(), out var n ) ? Math.Clamp( n, 0, 999 ) : 0;
				value = DesignData.Sample( _type, _sample, count );

				if ( _type.IsArray && value is System.Collections.IList list )
				{
					var array = Array.CreateInstance( _item, list.Count );
					for ( var i = 0; i < list.Count; i++ ) array.SetValue( list[i], i );
					value = array;
				}
			}
			else
			{
				value = Parse( text, _type, _sample );
			}

			_set( _instance, value );
		}
		catch ( Exception e )
		{
			Log.Warning( $"UI editor: {_name} cannot take '{text}': {e.Message}" );
			if ( _field is not null ) _field.Value = _shown;
			return;
		}

		_keep( value );

		Read();
	}

	//Text into a value of the type: words as they are, numbers and the rest as JSON, an empty
	//field as a sample. Text into a value of a type, the way a field takes it - for the console.
	internal static object ParseValue( string text, Type type ) => Parse( text, type, null );

	static object Parse( string text, Type type, DesignDataAttribute sample )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return DesignData.Sample( type, sample, 1 );

		if ( type == typeof( string ) ) return text;
		if ( type.IsEnum ) return Enum.Parse( type, text.Trim(), true );
		if ( type == typeof( bool ) ) return bool.Parse( text.Trim() );

		//A resource by its path in the project, as the game would load it.
		if ( IsResource( type ) )
		{
			var path = text.Trim();
			var found = typeof( ResourceLibrary ).GetMethod( nameof( ResourceLibrary.Get ), new[] { typeof( string ) } )?.MakeGenericMethod( type ).Invoke( null, new object[] { path } );
			if ( found is null ) throw new Exception( $"no {type.Name} at {path}" );
			return found;
		}

		var trimmed = text.Trim();

		if ( type == typeof( float ) ) return float.Parse( trimmed, System.Globalization.CultureInfo.InvariantCulture );
		if ( type == typeof( double ) ) return double.Parse( trimmed, System.Globalization.CultureInfo.InvariantCulture );
		if ( type == typeof( int ) ) return int.Parse( trimmed, System.Globalization.CultureInfo.InvariantCulture );
		if ( type == typeof( long ) ) return long.Parse( trimmed, System.Globalization.CultureInfo.InvariantCulture );

		//Anything else the engine's own converters read: a colour as "#ff0000", a vector as "1,2,3" - quoted when it was not, as JSON wants it.
		var json = trimmed.StartsWith( '{' ) || trimmed.StartsWith( '[' ) || trimmed.StartsWith( '"' ) ? trimmed : $"\"{trimmed.Replace( "\"", "\\\"" )}\"";

		return Json.Deserialize( json, type );
	}

	void Reset()
	{
		//The instance is built again with nothing kept for it, which is the only way to get the code's own default back.
		_window.ClearDesignValue( _name );
		_window.ReloadInstance();
	}

	static string TypeName( Type type )
	{
		if ( type == typeof( string ) ) return "string";
		if ( type == typeof( int ) ) return "int";
		if ( type == typeof( float ) ) return "float";
		if ( type == typeof( bool ) ) return "bool";

		if ( type.IsGenericType )
			return $"{type.Name.Split( '`' )[0]}<{string.Join( ", ", type.GetGenericArguments().Select( TypeName ) )}>";

		return type.Name;
	}
}

//The items of a list member, one after another under its row: a header with its number and what can be done to it, then a row for each
//field of the item - or one row for the item itself when it is a plain value. Open or closed per member, remembered while the editor is up.
internal sealed class ItemsBlock : Panel
{
	static readonly HashSet<string> Open = new( StringComparer.Ordinal );

	readonly UiEditorWindow _window;
	readonly Type _itemType;

	public string Member { get; }

	public bool IsOpen => Open.Contains( Member );

	public ItemsBlock( UiEditorWindow window, string member, Type itemType )
	{
		_window = window;
		Member = member;
		_itemType = itemType;

		AddClass( "items-block" );
		Rebuild();
	}

	public bool Toggle()
	{
		if ( !Open.Remove( Member ) ) Open.Add( Member );
		Rebuild();
		return IsOpen;
	}

	public void Rebuild()
	{
		DeleteChildren( true );
		SetClass( "hidden", !IsOpen );

		if ( !IsOpen ) return;

		var items = _window.ItemsOf( Member );

		if ( items is null )
		{
			var note = AddChild<UILabel>( "note" );
			note.Text = "no instance";
			return;
		}

		var plain = _itemType.IsPrimitive || _itemType == typeof( string ) || _itemType.IsEnum || _itemType == typeof( Color );
		var members = plain ? null : TypeLibrary.GetType( _itemType ) is { } description ? DesignData.Members( description ).ToList() : null;

		for ( var i = 0; i < items.Count; i++ )
		{
			var index = i;
			var item = items[i];

			var head = AddChild<Panel>( "item-head" );

			var number = head.AddChild<UILabel>( "number" );
			number.Tokenize = false;
			number.Text = $"#{index + 1}";

			var words = head.AddChild<UILabel>( "words" );
			words.Tokenize = false;
			words.Text = plain ? "" : Describe( item, members );

			Icon( head, "arrow_upward", "Move up", () => _window.MoveItem( Member, index, -1 ) );
			Icon( head, "arrow_downward", "Move down", () => _window.MoveItem( Member, index, 1 ) );
			Icon( head, "content_copy", "Duplicate", () => _window.DuplicateItem( Member, index ) );
			Icon( head, "close", "Remove", () => _window.RemoveItem( Member, index ) );

			if ( plain )
			{
				AddChild( new DataRow( _window, items, "value", _itemType,
					list => ( (System.Collections.IList)list )[index],
					( list, value ) => ( (System.Collections.IList)list )[index] = value,
					null, value => KeepPlain( index, value ) ) { KeptAs = Member } );
				continue;
			}

			if ( item is null || members is null || members.Count == 0 )
			{
				var note = AddChild<UILabel>( "note" );
				note.Text = item is null ? "null" : $"{_itemType.Name} has no fields to set";
				continue;
			}

			foreach ( var field in members )
			{
				var f = field;
				AddChild( new DataRow( _window, item, f.name, f.type, f.get, f.set, f.sample,
					value => _window.SetItemField( Member, index, it => f.set( it, value ) ) ) { KeptAs = Member } );
			}
		}

		var add = AddChild<UILabel>( "add-item" );
		add.Tokenize = false;
		add.Text = items.Count == 0 ? "+ first item" : "+ item";
		add.AddEventListener( "onclick", () => _window.InsertItem( Member, items.Count ) );
	}

	//A plain item written: the list with that one replaced.
	void KeepPlain( int index, object value )
	{
		if ( _window.ItemsOf( Member ) is not { } items || index >= items.Count ) return;

		items[index] = value;
		_window.KeepItems( Member, items );
	}

	void Icon( Panel head, string icon, string tip, Action run )
	{
		var button = head.AddChild<UILabel>( "material-icon act" );
		button.Tokenize = false;
		button.Text = icon;
		button.Tooltip = tip;
		button.AddEventListener( "onclick", run );
	}

	//A few words that say which item this is: its first string, else its first value.
	static string Describe( object item, List<(string name, Type type, Func<object, object> get, Action<object, object> set, DesignDataAttribute sample)> members )
	{
		if ( item is null ) return "null";

		foreach ( var member in members )
		{
			try
			{
				if ( member.type == typeof( string ) && member.get( item ) is string s && s.Length > 0 ) return s;
			}
			catch { }
		}

		foreach ( var member in members )
		{
			try
			{
				if ( member.get( item ) is { } v ) return v.ToString();
			}
			catch { }
		}

		return "";
	}
}
