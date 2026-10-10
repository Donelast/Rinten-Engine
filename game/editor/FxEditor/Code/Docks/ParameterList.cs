using Editor.Inspectors;
using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.FxEditor;

/// <summary>Parameters dock: one row per open value with default and use count; renames inline</summary>
public sealed class ParameterList : PanelDockHost
{
	readonly ParticleEditorWindow _window;
	readonly ParameterListPanel _panel;

	//The kinds somebody has folded shut, kept between sessions.
	readonly HashSet<ParticleParameterKind> _shut;

	const string ShutCookie = "fxeditor.parameters.shut";

	bool? _sortAscending;

	ParticleDefinition _definition;

	//No parent: the dock manager is what puts a dock somewhere, and a parent handed over here is replaced the moment the dock is opened.
	public ParameterList( ParticleEditorWindow window ) : base( null )
	{
		_window = window;
		_shut = EditorCookie.Get<List<ParticleParameterKind>>( ShutCookie, [] ).ToHashSet();

		Name = "Parameters";
		WindowTitle = "Parameters";
		SetWindowIcon( "tune" );
		MinimumWidth = 180;

		_panel = new ParameterListPanel( this );

		SetContent( _panel );
	}

	internal ParticleEditorWindow Window => _window;
	internal ParticleDefinition Definition => _definition;

	/// <summary>What the list is built from.</summary>
	public void SetDefinition( ParticleDefinition definition )
	{
		_definition = definition;

		Rebuild();
	}

	/// <summary>Something changed under the rows: build them again.</summary>
	public void Refresh() => Rebuild();

	/// <summary>The selection moved: the rows read it again rather than being rebuilt.</summary>
	public void NoteSelection() => _panel?.ReadSelection();

	/// <summary>The preview's parameters moved: every row reads its value again.</summary>
	public void RefreshValues() => _panel?.RefreshValues();

	internal string Filter => _panel?.Filter ?? "";

	internal void Rebuild() => _panel?.Rebuild();

	//Sort the rows by name, or put them back the way the file has them.
	internal void ToggleSort()
	{
		_sortAscending = !( _sortAscending ?? false );
		Rebuild();
	}

	internal bool IsShut( ParticleParameterKind kind ) => _shut.Contains( kind );

	internal void ToggleShut( ParticleParameterKind kind )
	{
		if ( !string.IsNullOrWhiteSpace( Filter ) ) return;

		if ( !_shut.Add( kind ) )
			_shut.Remove( kind );

		EditorCookie.Set( ShutCookie, _shut.OrderBy( x => x ).ToList() );
		Rebuild();
	}

	void Open( ParticleParameterKind kind )
	{
		if ( _shut.Remove( kind ) )
			EditorCookie.Set( ShutCookie, _shut.OrderBy( x => x ).ToList() );
	}

	//What is listed

	//The kinds, each with the parameters of that kind that pass the filter.
	internal IEnumerable<(ParticleParameterKind Kind, List<ParticleDefinition.Parameter> Parameters)> Groups()
	{
		if ( _definition is null ) yield break;

		var filter = Filter.Trim();

		foreach ( var kind in Kinds )
		{
			IEnumerable<ParticleDefinition.Parameter> parameters = ParametersOf( _definition, kind );

			if ( !string.IsNullOrEmpty( filter ) )
				parameters = parameters.Where( p => ( p.Name ?? "" ).Contains( filter, StringComparison.OrdinalIgnoreCase ) );

			parameters = _sortAscending switch
			{
				true => parameters.OrderBy( p => p.Name, StringComparer.OrdinalIgnoreCase ),
				false => parameters.OrderByDescending( p => p.Name, StringComparer.OrdinalIgnoreCase ),
				null => parameters,
			};

			var list = parameters.ToList();

			//While filtering, a kind with nothing matching is not a heading over nothing. Unfiltered, every kind shows so the plus has somewhere to point.
			if ( list.Count == 0 && !string.IsNullOrEmpty( filter ) ) continue;

			yield return (kind, list);
		}
	}

	internal static readonly ParticleParameterKind[] Kinds =
	[
		ParticleParameterKind.Float,
		ParticleParameterKind.Vector,
		ParticleParameterKind.Color,
	];

	internal static IEnumerable<ParticleDefinition.Parameter> ParametersOf( ParticleDefinition definition, ParticleParameterKind kind ) => kind switch
	{
		ParticleParameterKind.Vector => definition.VectorParameters,
		ParticleParameterKind.Color => definition.ColorParameters,
		_ => definition.FloatParameters,
	};

	internal static ParticleParameterKind KindOf( ParticleDefinition.Parameter parameter ) => parameter switch
	{
		ParticleDefinition.VectorParameter => ParticleParameterKind.Vector,
		ParticleDefinition.ColorParameter => ParticleParameterKind.Color,
		_ => ParticleParameterKind.Float,
	};

	internal int Count => _definition is null ? 0
		: _definition.FloatParameters.Count + _definition.VectorParameters.Count + _definition.ColorParameters.Count;

	internal string EmptyHint => Count > 0
		? "No matching parameters"
		: "No parameters\nClick + to add one";

	//Editing, through the window

	//Everything that can be added, under the button that asked.
	internal void OpenAddMenu( Panel anchor = null )
	{
		var menu = new Menu();

		AddOptions( menu );

		var under = anchor is { IsValid: true }
			? new Vector2( anchor.Box.Rect.Left * anchor.ScaleFromScreen, anchor.Box.Rect.Bottom * anchor.ScaleFromScreen )
			: _panel.ScreenRect.BottomLeft;

		menu.OpenAt( under );
	}

	internal void AddOptions( Menu menu )
	{
		foreach ( var kind in Kinds )
			menu.AddOption( $"{ParameterUI.TypeName( kind )} Parameter", ParameterUI.Icon( kind ), () => Add( kind ) );
	}

	internal void Add( ParticleParameterKind kind )
	{
		_panel?.ClearFilter();
		Open( kind );

		var parameter = _window.AddParameter( kind );
		if ( parameter is null ) return;

		//Built here rather than left to the edit: the window rebuilds the list on every edit, but the rename below wants the row to be there now.
		Rebuild();
		BeginRename( parameter );
	}

	internal void Rename( ParticleDefinition.Parameter parameter, string name ) => _window.RenameParameter( parameter, name );

	internal void Duplicate( ParticleDefinition.Parameter parameter )
	{
		var copy = _window.DuplicateParameter( parameter );
		if ( copy is null ) return;

		Rebuild();
		BeginRename( copy );
	}

	internal void Remove( ParticleDefinition.Parameter parameter )
	{
		var uses = _window.ParameterUses( parameter ).ToList();

		if ( uses.Count == 0 )
		{
			_window.RemoveParameter( parameter );
			return;
		}

		var confirm = new PopupWindow( "Delete Parameter",
			$"Delete '{parameter.Name}'?\n\nRead by {uses.Count} module{( uses.Count == 1 ? "" : "s" )}: " +
			$"{string.Join( ", ", uses.Select( use => use.Name ).Distinct() )}. They will go back to their own values.",
			"Cancel",
			new Dictionary<string, Action> { ["Delete"] = () => _window.RemoveParameter( parameter ) } );

		confirm.Show();
	}

	internal void BeginRename( ParticleDefinition.Parameter parameter ) => _panel?.RowOf( parameter )?.StartRename();
}

//Icons, names and colours for the three kinds, in one place.
internal static class ParameterUI
{
	public static string Icon( ParticleParameterKind kind ) => kind switch
	{
		ParticleParameterKind.Vector => "open_with",
		ParticleParameterKind.Color => "palette",
		_ => "looks_one",
	};

	public static string TypeName( ParticleParameterKind kind ) => kind switch
	{
		ParticleParameterKind.Vector => "Vector",
		ParticleParameterKind.Color => "Color",
		_ => "Float",
	};

	public static string GroupTitle( ParticleParameterKind kind ) => kind switch
	{
		ParticleParameterKind.Vector => "Vectors",
		ParticleParameterKind.Color => "Colors",
		_ => "Floats",
	};

	public static Color Color( ParticleParameterKind kind ) => kind switch
	{
		ParticleParameterKind.Vector => global::Color.Parse( "#e5c07b" ).Value,
		ParticleParameterKind.Color => global::Color.Parse( "#c678dd" ).Value,
		_ => global::Color.Parse( "#98c379" ).Value,
	};
}

//The toolbar, and the well the groups scroll in.
[StyleSheet( "ParameterList.cs.scss" )]
internal sealed class ParameterListPanel : EditorPanel
{
	readonly ParameterList _list;
	readonly TextField _filter;
	readonly PanelToolbarButton _add;
	readonly Panel _well;
	readonly Panel _rows;
	readonly UILabel _empty;

	readonly List<ParameterRow> _rowPanels = new();

	public string Filter => _filter?.Value ?? "";

	public ParameterListPanel( ParameterList list )
	{
		_list = list;

		AddClass( "parameter-list" );

		var toolbar = AddChild( new PanelToolbar() );

		_filter = toolbar.AddFilter( "Filter parameters...", _ => _list.Rebuild() );

		toolbar.AddIcon( "swap_vert", "Sort alphabetically", _list.ToggleSort );
		toolbar.AddSeparator();
		_add = toolbar.AddIcon( "add",
			"<b>Add Parameter</b><p>A value the effect leaves open, for a module to read and for whoever plays the effect to set.</p>",
			() => _list.OpenAddMenu( _add ) );

		_well = AddChild<Panel>( "well" );

		//A click on the empty list is the same as reaching for the plus.
		_well.AddEventListener( "onclick", () =>
		{
			if ( _empty.HasClass( "hidden" ) ) return;
			_list.OpenAddMenu( _add );
		} );

		_rows = _well.AddChild<Panel>( "rows" );

		_empty = _well.AddChild<UILabel>( "empty hidden" );
	}

	public void ClearFilter()
	{
		if ( _filter.IsValid() )
			_filter.Value = "";
	}

	public void ReadSelection()
	{
		foreach ( var row in _rowPanels )
			row.ReadSelection();
	}

	public void RefreshValues()
	{
		foreach ( var row in _rowPanels )
			row.RefreshValue();
	}

	public ParameterRow RowOf( ParticleDefinition.Parameter parameter )
		=> _rowPanels.FirstOrDefault( row => ReferenceEquals( row.Parameter, parameter ) );

	public void Rebuild()
	{
		_rows.DeleteChildren( true );
		_rowPanels.Clear();

		var filtering = !string.IsNullOrWhiteSpace( Filter );
		var any = false;

		foreach ( var (kind, parameters) in _list.Groups() )
		{
			var shut = !filtering && _list.IsShut( kind );

			_rows.AddChild( new ParameterGroupHeader( _list, kind, parameters.Count, shut, !filtering ) );

			if ( shut ) continue;

			var body = _rows.AddChild<Panel>( "group-body" );

			foreach ( var parameter in parameters )
			{
				any = true;
				_rowPanels.Add( body.AddChild( new ParameterRow( _list, parameter ) ) );
			}
		}

		//Headings over nothing are the whole list when the effect has no parameters at all: then the hint says so instead.
		var empty = !any && ( filtering || _list.Count == 0 );

		_rows.SetClass( "hidden", empty && !filtering );
		_empty.Text = _list.EmptyHint;
		_empty.SetClass( "hidden", !empty );
	}

	//A right-click on the well itself offers what the plus does.
	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton != MouseButtons.Right ) return;

		var menu = new Menu();

		_list.AddOptions( menu );

		menu.OpenAtCursor();
		e.StopPropagation();
	}
}

//The bar over a kind of parameter: how many there are, and a fold.
internal sealed class ParameterGroupHeader : PanelFoldHeader
{
	readonly ParameterList _list;
	readonly ParticleParameterKind _kind;
	readonly bool _collapsible;

	public ParameterGroupHeader( ParameterList list, ParticleParameterKind kind, int count, bool shut, bool collapsible )
	{
		_list = list;
		_kind = kind;
		_collapsible = collapsible;

		AddClass( "parameter-group" );
		SetClass( "fixed", !collapsible );

		Title = ParameterUI.GroupTitle( kind );
		Icon = ParameterUI.Icon( kind );
		Expanded = !shut;

		SyncContentVisible( !shut );

		var badge = AddChild<UILabel>( "count" );
		badge.Text = count.ToString();

		Tooltip = "Right-click to add";

		OnExpandedChanged = _ => _list.ToggleShut( _kind );
	}

	//While a filter is on, every kind is open and stays open.
	protected override bool IsFoldingClick( Panel target ) => _collapsible;

	protected override void OnContextMenu()
	{
		var menu = new Menu();

		menu.AddOption( $"Add {ParameterUI.TypeName( _kind )} Parameter", "add", () => _list.Add( _kind ) );

		menu.OpenAtCursor();
	}
}

//One parameter: the pill saying what it is, its value beside it, and the cross that takes it away.
internal sealed class ParameterRow : Panel
{
	readonly ParameterList _list;

	public ParticleDefinition.Parameter Parameter { get; }
	public ParticleParameterKind Kind { get; }

	readonly Panel _pill;
	readonly UILabel _icon;
	readonly UILabel _name;
	readonly UILabel _usage;
	readonly UILabel _type;
	readonly TextField _rename;

	BaseControl _value;

	public ParameterRow( ParameterList list, ParticleDefinition.Parameter parameter )
	{
		_list = list;
		Parameter = parameter;
		Kind = ParameterList.KindOf( parameter );

		AddClass( "parameter-row" );

		_pill = AddChild<Panel>( "pill" );

		_icon = _pill.AddChild<UILabel>( "material-icon type-icon" );
		_name = _pill.AddChild<UILabel>( "name" );
		_usage = _pill.AddChild<UILabel>( "usage hidden" );
		_type = _pill.AddChild<UILabel>( "type" );

		_name.Tokenize = false;

		_rename = _pill.AddChild<TextField>();
		_rename.AddClass( "rename" );
		_rename.AddClass( "hidden" );
		_rename.Submitted = _ => FinishRename();

		//Clicking away is finishing too - without this the box stays open over the row for good.
		_rename.AddEventListener( "onblur", FinishRename );

		ReadParameter();
		BuildValue();
		BuildActions();
	}

	void ReadParameter()
	{
		_icon.Text = ParameterUI.Icon( Kind );
		_name.Text = Parameter.Name;
		_type.Text = ParameterUI.TypeName( Kind );

		//The kind's colour is the row's own, which is the one thing a stylesheet has nothing to say about.
		var colour = ParameterUI.Color( Kind );

		_icon.Style.FontColor = colour;
		_type.Style.FontColor = colour.WithAlpha( 0.7f );
		_pill.Style.BackgroundColor = colour.WithAlpha( 0.1f );
		_pill.Style.BorderColor = colour.WithAlpha( 0.8f );

		Tooltip = $"{ParameterUI.TypeName( Kind )} parameter - double-click to rename";

		ReadUsage();
		ReadSelection();
	}

	void ReadUsage()
	{
		var usage = _list.Window.ParameterUses( Parameter ).Count();

		_usage.SetClass( "hidden", usage <= 0 );
		_usage.Text = usage > 0 ? $"×{usage}" : "";
	}

	public void ReadSelection()
	{
		var selected = _list.Window.IsSelected( Parameter );

		SetClass( "selected", selected );

		if ( _pill.IsValid() )
			_pill.Style.BackgroundColor = ParameterUI.Color( Kind ).WithAlpha( selected ? 0.25f : 0.1f );
	}

	/// <summary>Something under the row moved: the usage count reads again. The value control polls itself.</summary>
	public void RefreshValue() => ReadUsage();

	//The default value, edited in the row. Live against the preview while it moves, and one undo entry when it stops.
	void BuildValue()
	{
		var values = AddChild<Panel>( "values" );

		var so = Parameter.GetSerialized();

		//Whatever moved, it was the value: the name is not in this row, and a gradient reports its stops under names of their own.
		so.OnPropertyChanged += _ => _list.Window.OnParameterValueEdited( Parameter, finished: false );
		so.OnPropertyFinishEdit += _ => _list.Window.OnParameterValueEdited( Parameter, finished: true );

		var property = so.GetProperty( nameof( ParticleDefinition.FloatParameter.DefaultValue ) );
		if ( property is null ) return;

		//A float with a range is a slider, the way an animation graph's parameters are - the range is the
		//parameter's own, set in the properties, so it cannot come from an attribute the way a sheet's would.
		_value = Parameter is ParticleDefinition.FloatParameter { HasRange: true } number
			? new SliderControl { Property = property, Min = number.Min, Max = number.Max, ShowTextEntry = true, Step = 0.001f }
			: PanelControlFactory.CreateFor( property );

		if ( _value is null ) return;

		values.AddChild( _value );
		_value.Tooltip = "Default value";
	}

	void BuildActions()
	{
		var actions = AddChild<Panel>( "actions" );

		var remove = actions.AddChild<UILabel>( "material-icon action danger" );
		remove.Text = "close";
		remove.Tooltip = "Delete parameter";
		remove.AddEventListener( "onclick", () => _list.Remove( Parameter ) );
	}

	//Renaming

	public void StartRename()
	{
		if ( !_rename.HasClass( "hidden" ) ) return;

		_rename.Value = Parameter.Name;
		_rename.SetClass( "hidden", false );
		_rename.Focus();
		_rename.SelectAll();
	}

	void FinishRename()
	{
		if ( _rename.HasClass( "hidden" ) ) return;

		_rename.SetClass( "hidden", true );

		_list.Rename( Parameter, _rename.Value );
	}

	//Pointer and keys

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( !IsUnderPill( e.Target as Panel ) ) return;

		if ( e.MouseButton == MouseButtons.Left )
		{
			_list.Window.Select( Parameter );
			e.StopPropagation();
		}
		else if ( e.MouseButton == MouseButtons.Right )
		{
			_list.Window.Select( Parameter );
			OpenContextMenu();
			e.StopPropagation();
		}
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		base.OnDoubleClick( e );

		if ( e.MouseButton != MouseButtons.Left ) return;
		if ( !IsUnderPill( e.Target as Panel ) ) return;

		StartRename();
		e.StopPropagation();
	}

	bool IsUnderPill( Panel target )
	{
		for ( var p = target; p is not null; p = p.Parent )
		{
			if ( p == _rename ) return false;
			if ( p == _pill ) return true;
			if ( p == this ) return false;
		}

		return false;
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( !e.Pressed )
		{
			base.OnButtonTyped( e );
			return;
		}

		switch ( e.Button )
		{
			case "f2":
				StartRename();
				break;
			case "delete":
				_list.Remove( Parameter );
				break;
			case "escape" when !_rename.HasClass( "hidden" ):
				_rename.Value = Parameter.Name;
				_rename.SetClass( "hidden", true );
				break;
			default:
				base.OnButtonTyped( e );
				return;
		}

		e.StopPropagation = true;
	}

	void OpenContextMenu()
	{
		var menu = new Menu();

		menu.AddOption( "Rename", "edit", StartRename, "F2" );
		menu.AddOption( "Duplicate", "content_copy", () => _list.Duplicate( Parameter ) );

		menu.AddSeparator();
		menu.AddOption( "Delete", "delete", () => _list.Remove( Parameter ), "Del" );

		menu.OpenAtCursor();
	}
}
