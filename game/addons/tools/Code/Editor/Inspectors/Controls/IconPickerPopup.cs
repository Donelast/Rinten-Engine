namespace Editor.Inspectors;

using Rinten.UI;

//The grid of Material Icons an IconNameControl opens.
[StyleSheet( "IconPickerPopup.cs.scss" )]
sealed class IconPickerPopup : Popup
{
	const int PerPage = 48;

	TextEntry _search;
	Rinten.UI.Label _pageLabel;
	Panel _grid;
	Action<string> _picked;
	string _selected;
	int _page;

	public static IconPickerPopup Open( Panel source, string icon, Action<string> picked )
	{
		CloseAll();

		var popup = new IconPickerPopup();
		popup.AddClass( "icon-picker-popup" );
		popup.SetPositioning( source, PositionMode.BelowLeft, 2 );
		popup.CloseWhenParentIsHidden = true;

		popup._picked = picked;
		popup._selected = icon;
		popup._page = PageOf( icon );
		popup.Build();
		popup.SyncAnchor();

		return popup;
	}

	//The page an icon sits on, or the first when it is not one of ours.
	static int PageOf( string icon )
	{
		var index = string.IsNullOrEmpty( icon ) ? -1 : Array.IndexOf( MaterialIconNames.All, icon );
		return index < 0 ? 0 : index / PerPage;
	}

	void Build()
	{
		_search = AddChild<TextEntry>( "search" );
		_search.Placeholder = "Search";
		_search.OnTextEdited = _ =>
		{
			_page = 0;
			Populate();
		};
		_search.Focus();

		var pager = AddChild<Panel>( "pager" );

		var previous = pager.AddChild<ToolbarButton>();
		previous.Icon = "arrow_back";
		previous.OnPressed = () => Step( -1 );

		_pageLabel = pager.AddChild<Rinten.UI.Label>( "page" );
		_pageLabel.Tokenize = false;

		var next = pager.AddChild<ToolbarButton>();
		next.Icon = "arrow_forward";
		next.OnPressed = () => Step( 1 );

		_grid = AddChild<Panel>( "grid" );

		Populate();
	}

	string[] Matches()
	{
		var filter = _search.Value;

		return string.IsNullOrWhiteSpace( filter )
			? MaterialIconNames.All
			: MaterialIconNames.All.Where( x => x.Contains( filter, StringComparison.OrdinalIgnoreCase ) ).ToArray();
	}

	void Step( int direction )
	{
		var pages = PageCount( Matches().Length );

		_page += direction;

		if ( _page < 0 ) _page = pages - 1;
		if ( _page >= pages ) _page = 0;

		Populate();
	}

	static int PageCount( int count ) => Math.Max( 1, (int)MathF.Ceiling( count / (float)PerPage ) );

	void Populate()
	{
		_grid.DeleteChildren();

		var matches = Matches();
		var pages = PageCount( matches.Length );

		_page = Math.Clamp( _page, 0, pages - 1 );
		_pageLabel.Text = $"Page {_page + 1}/{pages}";

		var first = _page * PerPage;
		var last = Math.Min( first + PerPage, matches.Length );

		for ( var i = first; i < last; i++ )
		{
			var icon = matches[i];
			var button = _grid.AddChild( new Rinten.UI.Button( null, icon ) );
			button.Tooltip = icon;
			button.SetClass( "active", icon == _selected );
			button.AddEventListener( "onclick", () =>
			{
				Delete();
				_picked?.Invoke( icon );
			} );
		}
	}
}
