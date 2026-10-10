namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>A string picked out of a list the control works out for itself.</summary>
public abstract class StringDropdownControl : BaseControl
{
	readonly ControlDropDown _dropdown;

	public override bool SupportsMultiEdit => true;

	protected StringDropdownControl()
	{
		ShowBackground = false;

		_dropdown = AddChild<ControlDropDown>();
		_dropdown.BuildOptions = BuildOptions;
		_dropdown.ValueChanged = OnPicked;
	}

	//Everything the list offers; an option valued "" is what empty reads as.
	protected abstract List<Rinten.UI.Option> BuildOptions();

	//Whether the popup carries a search box, for a list too long to read down.
	protected virtual bool Searchable => false;

	//How wide the popup opens at least, for a control narrower than what is in it. Zero leaves it the width of the control, which is what a
	//sheet row wants. A toolbar is where it is worth setting: the field there is as narrow as the toolbar can afford and the names in it are not.
	protected virtual float PopupMinWidth => 0f;

	public override void Rebuild()
	{
		_dropdown.Searchable = Searchable;
		_dropdown.PopupMinWidth = PopupMinWidth;

		//The list is not built here. Writing the dropdown's value builds it, and so does opening the popup, and BuildOptions
		//is what walks a model's bones or the type library - doing it here as well was doing it twice per rebuild.
		SyncDisplay();
		ApplyDisabled();
	}

	public override void Tick()
	{
		base.Tick();

		SyncDisplay();
	}

	protected override void OnReadOnlyChanged() => ApplyDisabled();

	//Reads what the property currently says, for a control whose value is not the property's own.
	protected virtual string Read() => Property?.As.String;

	//Writes the pick, for a control whose value does not go straight onto the property.
	protected virtual void Assign( string value )
	{
		Property.Parent?.NoteStartEdit( Property );
		Property.As.String = value;
		Property.Parent?.NoteFinishEdit( Property );
	}

	void OnPicked( string value )
	{
		if ( IsReadOnly || Property is null ) return;
		if ( string.Equals( Read(), value, StringComparison.OrdinalIgnoreCase ) ) return;

		Assign( value );
	}

	void SyncDisplay()
	{
		if ( Property is null ) return;

		if ( Property.IsMultipleDifferentValues )
		{
			_dropdown.Text = "Multiple Values";
			_dropdown.SetClass( "multiple", true );
			return;
		}

		_dropdown.SetClass( "multiple", false );

		var value = Read() ?? "";

		//Case-insensitively, because the dropdown matches its options that way: a value differing only in case is already
		//showing the option it means, and writing it back would only have the two disagree again on the next tick.
		if ( !string.Equals( _dropdown.Value as string, value, StringComparison.OrdinalIgnoreCase ) )
			_dropdown.Value = value;
	}

	void ApplyDisabled()
	{
		if ( _dropdown is null ) return;

		_dropdown.Disabled = IsReadOnly;
		_dropdown.AcceptsFocus = !IsReadOnly;
	}
}

/// <summary>The dropdown a <see cref="StringDropdownControl"/> wears.</summary>
[StyleSheet( "ControlDropDown.cs.scss" )]
public sealed class ControlDropDown : DropDown
{
	//How long the search waits after a keystroke before it filters.
	const float FilterDelay = 0.12f;

	//Rows built either side of the visible window, so a scroll has somewhere to land.
	const int Overscan = 4;

	/// <summary>Whether the popup carries a search box, for a list too long to read down.</summary>
	public bool Searchable { get; set; }

	/// <summary>How wide the popup opens at least; zero leaves it the width of the control.</summary>
	public float PopupMinWidth { get; set; }

	//Set from here rather than from the stylesheet, and onto each row: the list builds only the rows the scroll is over, so it has to know where a row will be before there is a row to measure.
	/// <summary>How tall one row of the popup is.</summary>
	public float RowHeight { get; set; } = 22f;

	TextEntry _filter;
	Panel _list;

	//The options the search leaves, which the visible window is cut out of.
	readonly List<Rinten.UI.Option> _rows = new();

	int _first = -1;
	int _count = -1;

	bool _filtering;
	float _filteredAt;

	public override void Open()
	{
		var popup = new ControlDropDownPopup();
		popup.SetPositioning( this, Popup.PositionMode.BelowStretch, 0.0f );
		popup.CloseWhenParentIsHidden = true;
		popup.AddClass( "flat-top" );
		popup.SetClass( "searchable", Searchable );

		if ( PopupMinWidth > 0f )
			popup.Style.MinWidth = PopupMinWidth;

		Popup = popup;

		if ( BuildOptions is not null )
			Options = BuildOptions.Invoke();

		_filter = null;
		_filtering = false;

		if ( Searchable )
		{
			_filter = popup.AddChild<TextEntry>( "filter" );
			_filter.Placeholder = "Search...";
			_filter.OnTextEdited = OnFilterEdited;
			_filter.Focus();
		}

		_list = popup.AddChild<Panel>( "list" );

		Populate();

		popup.SyncAnchor();
	}

	public override void Tick()
	{
		base.Tick();

		ApplyFilter();
		BuildWindow( false );
	}

	//Waits for the typing to stop before filtering. Filtering walks every option and throws the built rows away,
	//so doing it per keystroke means doing it four or five times for a word nobody has finished typing yet.
	void OnFilterEdited( string text )
	{
		_filtering = true;
		_filteredAt = RealTime.Now;
	}

	void ApplyFilter()
	{
		if ( !_filtering ) return;

		if ( !_list.IsValid() )
		{
			_filtering = false;
			return;
		}

		if ( RealTime.Now - _filteredAt < FilterDelay ) return;

		_filtering = false;

		Populate();
	}

	//Works out which options the search leaves, and starts the list again at the top.
	void Populate()
	{
		_rows.Clear();

		var filter = _filter?.Value;

		foreach ( var option in Options )
		{
			if ( AlwaysShown( option ) || Matches( option, filter ) )
				_rows.Add( option );
		}

		if ( _list.IsValid() )
			_list.ScrollOffset = new Vector2( _list.ScrollOffset.x, 0f );

		BuildWindow( true );
	}

	//Virtualized: builds only visible rows, with spacers keeping the full scroll height
	void BuildWindow( bool force )
	{
		if ( !_list.IsValid() ) return;

		var rowHeight = RowHeight * ScaleToScreen;

		if ( rowHeight <= 0f ) return;

		//Before the first layout there is no viewport to measure, so one row is built and the rest follow on the next tick, once there is a height.
		var viewport = _list.Box.Rect.Height;
		var visible = viewport > 0f ? (int)MathF.Ceiling( viewport / rowHeight ) : 1;

		var first = Math.Max( 0, (int)MathF.Floor( _list.ScrollOffset.y / rowHeight ) - Overscan );
		var count = Math.Clamp( visible + Overscan * 2, 0, Math.Max( 0, _rows.Count - first ) );

		if ( !force && first == _first && count == _count ) return;

		_first = first;
		_count = count;

		_list.DeleteChildren();

		AddSpacer( first );

		for ( var i = first; i < first + count; i++ )
		{
			AddRow( _rows[i] );
		}

		AddSpacer( _rows.Count - first - count );
	}

	//The height of the rows that were not built, so the scroll still covers them.
	void AddSpacer( int rows )
	{
		if ( rows <= 0 ) return;

		_list.AddChild<Panel>( "spacer" ).Style.Height = rows * RowHeight;
	}

	void AddRow( Rinten.UI.Option option )
	{
		var row = _list.AddChild( new Rinten.UI.Button( option.Title, option.Icon ) );

		row.Style.Height = RowHeight;
		row.Tooltip = option.Tooltip;
		row.SetClass( "active", Selected is not null && Equals( option.Value, Selected.Value ) );
		row.AddEventListener( "onclick", () => Pick( option ) );
	}

	//The empty option is what the list is cleared with, not something to search for.
	static bool AlwaysShown( Rinten.UI.Option option )
	{
		return option.Value is null || (option.Value is string text && text.Length == 0);
	}

	static bool Matches( Rinten.UI.Option option, string filter )
	{
		if ( string.IsNullOrWhiteSpace( filter ) ) return true;

		return option.Title?.Contains( filter, StringComparison.OrdinalIgnoreCase ) ?? false;
	}

	void Pick( Rinten.UI.Option option )
	{
		Popup?.Delete();

		Select( option );
	}
}

[StyleSheet( "ControlDropDown.cs.scss" )]
public sealed class ControlDropDownPopup : Popup
{
}
