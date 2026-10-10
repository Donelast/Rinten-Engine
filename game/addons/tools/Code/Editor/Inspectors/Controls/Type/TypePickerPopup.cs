namespace Editor.Inspectors;

using Rinten.UI;

//The types a TypeField offers, a group at a time.
[StyleSheet( "TypePickerPopup.cs.scss" )]
sealed class TypePickerPopup : Popup
{
	const int MaxSearchResults = 50;

	TextEntry _filter;
	Panel _list;
	IReadOnlyList<TypeOptions.Option> _options;
	Type _current;
	Action<Type> _picked;

	TypeGroupPopup _submenu;
	TypeGroupRow _openRow;

	public static TypePickerPopup Open( Panel source, Type current, TypeConstraint constraint, Action<Type> picked )
	{
		CloseAll();

		var popup = new TypePickerPopup();
		popup.AddClass( "type-picker-popup" );
		popup.SetPositioning( source, PositionMode.BelowStretch, 2 );
		popup.CloseWhenParentIsHidden = true;

		popup._picked = picked;
		popup._current = current;
		popup._options = TypeOptions.For( constraint );
		popup.Build();
		popup.SyncAnchor();

		return popup;
	}

	void Build()
	{
		_filter = AddChild<TextEntry>( "filter" );
		_filter.Placeholder = "Filter Types..";
		_filter.OnTextEdited = _ => Populate();
		_filter.Focus();

		_list = AddChild<Panel>( "list" );

		Populate();
	}

	//The group a type is listed under - the first step of its path.
	static string GroupOf( TypeOptions.Option option )
	{
		var path = option.Path ?? "";
		var slash = path.IndexOf( '/' );

		return slash < 0 ? path : path[..slash];
	}

	void Populate()
	{
		CloseSubmenu();

		_list.DeleteChildren();

		AddType( new TypeOptions.Option( null, "None", null, null, "cancel" ) );

		var filter = _filter.Value;

		//A search is answered flat: the group a match sits in is not what was asked for.
		if ( !string.IsNullOrWhiteSpace( filter ) )
		{
			var matches = _options
				.Where( x => x.Title.Contains( filter, StringComparison.OrdinalIgnoreCase ) )
				.OrderBy( x => x.Title, StringComparer.OrdinalIgnoreCase )
				.ToArray();

			foreach ( var option in matches.Take( MaxSearchResults ) )
			{
				AddType( option );
			}

			if ( matches.Length > MaxSearchResults )
			{
				var more = _list.AddChild<Rinten.UI.Label>( "more" );
				more.Tokenize = false;
				more.Text = $"...and {matches.Length - MaxSearchResults} more";
			}

			return;
		}

		foreach ( var group in _options
			.GroupBy( GroupOf )
			.OrderBy( x => x.Key, StringComparer.OrdinalIgnoreCase ) )
		{
			var options = group
				.OrderBy( x => x.Path, StringComparer.OrdinalIgnoreCase )
				.ThenBy( x => x.Title, StringComparer.OrdinalIgnoreCase )
				.ToArray();

			var row = _list.AddChild( new TypeGroupRow( group.Key, options.Any( x => x.Type == _current ) ) );

			row.AddEventListener( "onclick", () => OpenGroup( row, options ) );
		}
	}

	void AddType( TypeOptions.Option option )
	{
		var item = _list.AddChild( new Rinten.UI.Button( option.Title, option.Icon ) );

		item.Tooltip = TooltipFor( option );
		item.SetClass( "active", option.Type == _current );
		item.AddEventListener( "onclick", () => Pick( option.Type ) );
	}

	internal static string TooltipFor( TypeOptions.Option option )
	{
		if ( option.Type is null ) return null;

		var path = string.IsNullOrEmpty( option.Path ) ? option.Title : $"{option.Path}/{option.Title}";

		return string.IsNullOrEmpty( option.Description ) ? path : $"{path}\n{option.Description}";
	}

	//Opens the group beside its row, leaving this list up behind it.
	void OpenGroup( TypeGroupRow row, TypeOptions.Option[] options )
	{
		var toggle = _openRow == row && _submenu is { IsValid: true };

		CloseSubmenu();

		if ( toggle ) return;

		_submenu = TypeGroupPopup.Open( row, options, _current, Pick );
		_openRow = row;
		row.SetClass( "open", true );
	}

	void CloseSubmenu()
	{
		if ( _submenu is { IsValid: true } )
			_submenu.Delete();

		_openRow?.SetClass( "open", false );

		_submenu = null;
		_openRow = null;
	}

	void Pick( Type type )
	{
		CloseAll();
		_picked?.Invoke( type );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		CloseSubmenu();
	}
}

//A group in the type list, with the corners a child popup opens from.
sealed class TypeGroupRow : Panel
{
	/// <summary>A zero-size corner, so the group's popup opens level with this row's top edge.</summary>
	public Panel LeftAnchor { get; }
	public Panel RightAnchor { get; }

	public TypeGroupRow( string name, bool holdsCurrent )
	{
		AddClass( "group" );
		SetClass( "active", holdsCurrent );

		LeftAnchor = AddChild<Panel>( "anchor" );
		LeftAnchor.AddClass( "left" );

		RightAnchor = AddChild<Panel>( "anchor" );
		RightAnchor.AddClass( "right" );

		var label = AddChild<Rinten.UI.Label>( "name" );
		label.Tokenize = false;
		label.Text = string.IsNullOrEmpty( name ) ? "Other" : name;

		AddChild<IconPanel>( "chevron" ).Text = "chevron_right";
	}
}

//One group's types, beside the row that opened it and never over it.
[StyleSheet( "TypePickerPopup.cs.scss" )]
sealed class TypeGroupPopup : Popup
{
	//Same number as the width in the sheet; the popup has to know if it fits on the right.
	const float Width = 260f;

	public static TypeGroupPopup Open( TypeGroupRow row, TypeOptions.Option[] options, Type current, Action<Type> picked )
	{
		var toTheRight = FitsRight( row );
		var anchor = toTheRight ? row.RightAnchor : row.LeftAnchor;

		if ( anchor is not { IsValid: true } ) return null;

		var popup = new TypeGroupPopup();
		popup.AddClass( "type-group-popup" );
		popup.SetPositioning( anchor, toTheRight ? PositionMode.Right : PositionMode.Left, 0 );
		popup.AnchorPanel = row;
		popup.CloseWhenParentIsHidden = true;

		foreach ( var option in options )
		{
			var type = option.Type;
			var item = popup.AddChild( new Rinten.UI.Button( option.Title, option.Icon ) );

			item.Tooltip = TypePickerPopup.TooltipFor( option );
			item.SetClass( "active", type == current );
			item.AddEventListener( "onclick", () => picked?.Invoke( type ) );
		}

		popup.SyncAnchor();

		return popup;
	}

	static bool FitsRight( Panel row )
	{
		var rect = row.GetScreenPixelRect();
		return rect.Right + Width * row.ScaleToScreen <= Screen.Width;
	}
}
