namespace Editor.Inspectors;

using Rinten.UI;

//The input actions an InputActionControl offers, a group at a time.
[StyleSheet( "InputActionPopup.cs.scss" )]
sealed class InputActionPopup : Popup
{
	TextEntry _filter;
	Panel _list;
	string[] _actions;
	Action<string> _picked;

	InputActionGroupPopup _submenu;
	InputActionGroupRow _openRow;

	public static InputActionPopup Open( Panel source, Action<string> picked )
	{
		CloseAll();

		var popup = new InputActionPopup();
		popup.AddClass( "input-action-popup" );
		popup.SetPositioning( source, PositionMode.BelowStretch, 2 );
		popup.CloseWhenParentIsHidden = true;

		popup._picked = picked;
		popup._actions = Rinten.Input.ActionNames.ToArray();
		popup.Build();
		popup.SyncAnchor();

		return popup;
	}

	void Build()
	{
		_filter = AddChild<TextEntry>( "filter" );
		_filter.Placeholder = "Filter Actions..";
		_filter.OnTextEdited = _ => Populate();
		_filter.Focus();

		_list = AddChild<Panel>( "list" );

		Populate();
	}

	static string GroupOf( string action ) => Rinten.Input.GetGroupName( action ) ?? "Other";

	void Populate()
	{
		CloseSubmenu();

		_list.DeleteChildren();

		AddAction( "Set to None", "clear_all", "" );

		var filter = _filter.Value;

		//A search is answered flat: the group a match sits in is not what was asked for.
		if ( !string.IsNullOrWhiteSpace( filter ) )
		{
			foreach ( var action in _actions
				.Where( x => x.Contains( filter, StringComparison.OrdinalIgnoreCase ) )
				.OrderBy( x => x, StringComparer.OrdinalIgnoreCase ) )
			{
				AddAction( action.ToLower(), "gamepad", action );
			}

			return;
		}

		foreach ( var group in _actions
			.GroupBy( GroupOf )
			.OrderBy( x => x.Key, StringComparer.OrdinalIgnoreCase ) )
		{
			var actions = group.OrderBy( x => x, StringComparer.OrdinalIgnoreCase ).ToArray();
			var row = _list.AddChild( new InputActionGroupRow( group.Key ) );

			row.AddEventListener( "onclick", () => OpenGroup( row, actions ) );
		}
	}

	void AddAction( string text, string icon, string value )
	{
		var item = _list.AddChild( new Rinten.UI.Button( text, icon ) );
		item.AddEventListener( "onclick", () => Pick( value ) );
	}

	//Opens the group beside its row, leaving this list up behind it.
	void OpenGroup( InputActionGroupRow row, string[] actions )
	{
		var toggle = _openRow == row && _submenu is { IsValid: true };

		CloseSubmenu();

		if ( toggle ) return;

		_submenu = InputActionGroupPopup.Open( row, actions, Pick );
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

	void Pick( string action )
	{
		CloseAll();
		_picked?.Invoke( action );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		CloseSubmenu();
	}
}

//A group in the action list, with the corners a child popup opens from.
sealed class InputActionGroupRow : Panel
{
	/// <summary>A zero-size corner, so the group's popup opens level with this row's top edge.</summary>
	public Panel LeftAnchor { get; }
	public Panel RightAnchor { get; }

	public InputActionGroupRow( string name )
	{
		AddClass( "group" );

		LeftAnchor = AddChild<Panel>( "anchor" );
		LeftAnchor.AddClass( "left" );

		RightAnchor = AddChild<Panel>( "anchor" );
		RightAnchor.AddClass( "right" );

		var label = AddChild<Rinten.UI.Label>( "name" );
		label.Tokenize = false;
		label.Text = name;

		AddChild<IconPanel>( "chevron" ).Text = "chevron_right";
	}
}

//One group's actions, beside the row that opened it and never over it.
[StyleSheet( "InputActionPopup.cs.scss" )]
sealed class InputActionGroupPopup : Popup
{
	//Same number as the width in the sheet; the popup has to know if it fits on the right.
	const float Width = 220f;

	public static InputActionGroupPopup Open( InputActionGroupRow row, string[] actions, Action<string> picked )
	{
		var toTheRight = FitsRight( row );
		var anchor = toTheRight ? row.RightAnchor : row.LeftAnchor;

		if ( anchor is not { IsValid: true } ) return null;

		var popup = new InputActionGroupPopup();
		popup.AddClass( "input-action-group-popup" );
		popup.SetPositioning( anchor, toTheRight ? PositionMode.Right : PositionMode.Left, 0 );
		popup.AnchorPanel = row;
		popup.CloseWhenParentIsHidden = true;

		foreach ( var action in actions )
		{
			var value = action;
			var item = popup.AddChild( new Rinten.UI.Button( action.ToLower(), "gamepad" ) );
			item.AddEventListener( "onclick", () => picked?.Invoke( value ) );
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
