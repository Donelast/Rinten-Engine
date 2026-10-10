using Editor.Inspectors;
using Rinten.UI;

using UILabel = Rinten.UI.Label;
using System.Text.Json.Serialization;

namespace Editor.MdlEditor;

/// <summary>The graph's parameter list, the bottom half of the preview column; values drive the preview live</summary>
[StyleSheet( "AnimGraphParameterList.scss" )]
public class ParameterList : Panel
{
	private readonly Window _window;
	private readonly ParameterListPanel _panel;
	private readonly List<IParameterRow> _rowWidgets = new();
	private readonly HashSet<string> _collapsedGroups;
	private bool? _sortAscending;
	private bool _hasVisibleParameters;
	private Dictionary<IParameter, int> _usageCounts;

	private const string CollapsedGroupsCookie = "animgraph.parameter-groups.collapsed";

	//What the filter box has in it.
	internal string Filter => _panel?.Filter ?? "";

	internal Window Window => _window;

	//No parent: the preview column is what puts this somewhere, and it takes it back off and puts it in again whenever the model half borrows the preview.
	public ParameterList( Window window )
	{
		_window = window;
		_collapsedGroups = EditorCookie.Get<List<string>>( CollapsedGroupsCookie, [] ).ToHashSet( StringComparer.OrdinalIgnoreCase );

		AddClass( "parameter-list-host" );

		_panel = AddChild( new ParameterListPanel( this ) );
	}

	//Sort the list by name, or put it back the way the graph has it.
	internal void ToggleSort()
	{
		_sortAscending = !(_sortAscending ?? false);
		Rebuild();
	}

	internal void ResetAllPreviewsFromToolbar() => ResetAllPreviews();

	internal void RandomizeAllPreviews() => ApplyAllPreviews( p => p.RandomizePreview() );

	internal void DeleteUnusedFromToolbar() => DeleteUnused();

	//Selection is a class on the row now, so the rows are asked to read it again rather than repainted.
	internal void UpdateSelection()
	{
		foreach ( var row in _rowWidgets )
			row.ReadSelection();
	}

	internal void RefreshPreviewValues()
	{
		foreach ( var row in _rowWidgets )
			row.RefreshPreview();
	}

	internal Menu CreateMenu() => new();

	private void ApplyAllPreviews( Action<Parameter> action )
	{
		foreach ( var parameter in _window.Graph?.Parameters ?? [] )
		{
			action( parameter );
			_window.SetPreviewParameter( parameter );
		}

		Rebuild();
	}

	private void ResetAllPreviews()
	{
		var parameters = _window.Graph?.Parameters ?? [];

		foreach ( var parameter in parameters )
		{
			parameter.ResetPreview();
			_window.SetPreviewParameter( parameter );
		}

		Rebuild();
	}

	internal void ResetPreview( Parameter parameter )
	{
		parameter.ResetPreview();
		_window.SetPreviewParameter( parameter );
		Rebuild();
	}

	private List<IParameter> UnusedParameters()
	{
		var graph = _window.Graph;
		if ( graph is null )
			return [];

		var parameterNodes = graph.AllNodes.OfType<Nodes.ParameterNode>().ToArray();
		var selectors = graph.AllNodes.OfType<Nodes.IDBasedSelectorNode>().ToArray();
		return graph.AllParameters
			.Where( parameter => !parameterNodes.Any( node => node.ParameterIdentifier == parameter.Identifier
				|| string.Equals( node.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase ) ) )
			.Where( parameter => !selectors.Any( node => node.ParameterIdentifier == parameter.Identifier
				|| string.Equals( node.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase ) ) )
			.ToList();
	}

	private void DeleteUnused()
	{
		var unused = UnusedParameters();
		if ( unused.Count == 0 )
			return;

		var confirm = new PopupWindow( "Delete Unused Parameters",
			$"Delete {unused.Count} unused parameter{(unused.Count == 1 ? "" : "s")}?\n{string.Join( ", ", unused.Select( p => p.Name ) )}",
			"Cancel", new Dictionary<string, Action>
			{
				["Delete"] = () =>
				{
					_window.PushGraphUndo( "Delete Unused Parameters" );
					foreach ( var parameter in unused.OfType<VirtualParameter>() )
						_window.OnVirtualParameterRemoved( parameter.Identifier );
					_window.Graph.Parameters.RemoveAll( p => unused.Contains( p ) );
					_window.Graph.VirtualParameters.RemoveAll( p => unused.Contains( p ) );
					_window.PushGraphRedo();
				}
			} );

		confirm.Show();
	}

	private string EmptyHint => (_window.Graph?.Parameters.Count ?? 0) + (_window.Graph?.VirtualParameters.Count ?? 0) > 0
		? "No matching parameters"
		: "No parameters\nClick + to add one";

	private IEnumerable<IGrouping<string, IParameter>> FilteredGroups()
	{
		var parameters = (_window.Graph?.Parameters.Cast<IParameter>() ?? [])
			.Concat( _window.Graph?.VirtualParameters ?? [] );
		var filter = Filter.Trim();

		var result = parameters.Where( p => string.IsNullOrEmpty( filter )
			|| p.Name.Contains( filter, StringComparison.OrdinalIgnoreCase )
			|| GroupTitle( GroupName( p ) ).Contains( filter, StringComparison.OrdinalIgnoreCase ) );

		result = _sortAscending switch
		{
			true => result.OrderBy( p => p.Name ),
			false => result.OrderByDescending( p => p.Name ),
			null => result,
		};

		return result
			.GroupBy( GroupName, StringComparer.OrdinalIgnoreCase )
			.OrderBy( group => string.IsNullOrEmpty( group.Key ) ? 0 : 1 )
			.ThenBy( group => group.Key, StringComparer.OrdinalIgnoreCase );
	}

	private IEnumerable<IParameter> DisplayedParameters()
	{
		var filtering = !string.IsNullOrWhiteSpace( Filter );
		foreach ( var group in FilteredGroups() )
		{
			if ( !filtering && _collapsedGroups.Contains( group.Key ) )
				continue;

			foreach ( var parameter in group )
				yield return parameter;
		}
	}

	internal static string GroupName( IParameter parameter ) => NormalizeGroup( parameter.Group );
	internal static string GroupTitle( string group ) => string.IsNullOrEmpty( group ) ? "General" : group;

	public void Rebuild()
	{
		if ( _panel is null )
			return;

		var rows = _panel.Rows;

		rows.DeleteChildren( true );
		_rowWidgets.Clear();

		var groups = FilteredGroups().ToList();
		_hasVisibleParameters = groups.Any();

		var filtering = !string.IsNullOrWhiteSpace( Filter );

		foreach ( var group in groups )
		{
			var collapsed = !filtering && _collapsedGroups.Contains( group.Key );

			rows.AddChild( new ParameterGroupHeader( this, group.Key, group.Count(), collapsed, !filtering ) );

			if ( collapsed )
				continue;

			var body = rows.AddChild<Panel>( "group-body" );

			foreach ( var parameter in group )
			{
				IParameterRow row = parameter switch
				{
					Parameter control => body.AddChild( new ParameterRow( _window, this, control ) ),
					VirtualParameter virtualParameter => body.AddChild( new VirtualParameterRow( _window, this, virtualParameter ) ),
					_ => throw new NotSupportedException()
				};

				_rowWidgets.Add( row );
			}
		}

		_panel.SetEmpty( !_hasVisibleParameters, EmptyHint );

		UpdateUnusedButton();
	}

	internal void Remove( Parameter parameter )
	{
		var parameterNodes = _window.Graph.AllNodes.OfType<Nodes.ParameterNode>()
			.Where( node => node.ParameterIdentifier == parameter.Identifier
				|| string.Equals( node.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase ) )
			.ToArray();
		var selectors = _window.Graph.AllNodes.OfType<Nodes.IDBasedSelectorNode>()
			.Where( node => node.ParameterIdentifier == parameter.Identifier
				|| string.Equals( node.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase ) )
			.ToArray();

		void Delete()
		{
			_window.PushGraphUndo( "Remove Parameter" );

			foreach ( var node in parameterNodes )
			{
				node.ParameterIdentifier = "";
				node.ParameterName = "";
				node.Update();
			}

			_window.Graph.Parameters.Remove( parameter );

			foreach ( var selector in _window.Graph.AllNodes.OfType<Nodes.IDBasedSelectorNode>() )
				selector.ClearParameterIfMissing();

			_window.PushGraphRedo();
		}

		var referenceCount = parameterNodes.Length + selectors.Length;
		if ( referenceCount == 0 )
		{
			Delete();
			return;
		}

		var usages = new List<string>();
		if ( parameterNodes.Length > 0 )
			usages.Add( $"{parameterNodes.Length} parameter node{(parameterNodes.Length == 1 ? "" : "s")}" );
		if ( selectors.Length > 0 )
			usages.Add( $"{selectors.Length} ID selector{(selectors.Length == 1 ? "" : "s")}" );

		var confirm = new PopupWindow(
			"Delete Parameter",
			$"Delete '{parameter.Name}'?\n\nUsed by {string.Join( " and ", usages )}. These references will be cleared.",
			"Cancel",
			new Dictionary<string, Action> { ["Delete"] = Delete } );
		confirm.Show();
	}

	internal void SynchronizeIDSelectors( Parameter parameter, bool resetOptions = false )
	{
		var disconnected = 0;
		foreach ( var selector in _window.Graph.AllNodes.OfType<Nodes.IDBasedSelectorNode>()
			.Where( node => node.ParameterIdentifier == parameter.Identifier
				|| string.Equals( node.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase ) ) )
		{
			disconnected += selector.Options.Count( option => option.Pose.IsValid
				&& (resetOptions
					|| parameter.Type != AnimParameterType.ID
					|| !parameter.Options.Contains( option.Id, StringComparer.Ordinal )) );

			if ( parameter.Type != AnimParameterType.ID )
			{
				selector.ClearParameter();
				continue;
			}

			if ( resetOptions )
			{
				selector.ResetOptionsFromParameter();
				continue;
			}

			if ( selector.SynchronizeOptionsFromParameter() )
				selector.Update();
		}

		if ( disconnected > 0 )
			Log.Warning( $"AnimGraph: removing ID options disconnected {disconnected} selector pose{(disconnected == 1 ? "" : "s")}" );
	}

	internal static bool AffectsIDSelectors( SerializedProperty property )
	{
		return property is null || ParameterPropertyName( property ) is nameof( Parameter.Name )
				or nameof( Parameter.Type )
				or nameof( Parameter.EnumType )
				or nameof( Parameter.EnumTypeName )
				or nameof( Parameter.ExpectedValues );
	}

	internal static bool ResetsIDSelectorOptions( SerializedProperty property )
	{
		return ParameterPropertyName( property ) is nameof( Parameter.EnumType ) or nameof( Parameter.EnumTypeName );
	}

	private static string ParameterPropertyName( SerializedProperty property )
	{
		return property?.Parent?.ParentProperty?.Name ?? property?.Name;
	}

	internal void Duplicate( Parameter parameter )
	{
		var copy = parameter.Duplicate();
		copy.Name = UniqueName( parameter.Name );

		_window.PushGraphUndo( "Duplicate Parameter" );
		_window.Graph.Parameters.Insert( _window.Graph.Parameters.IndexOf( parameter ) + 1, copy );
		_window.PushGraphRedo();

		BeginRename( copy );
	}

	internal void Remove( VirtualParameter parameter )
	{
		_window.PushGraphUndo( "Remove Virtual Parameter" );
		_window.OnVirtualParameterRemoved( parameter.Identifier );
		_window.Graph.VirtualParameters.Remove( parameter );
		_window.PushGraphRedo();
	}

	internal void Duplicate( VirtualParameter parameter )
	{
		var copy = parameter.Duplicate();
		copy.Name = UniqueName( parameter.Name );

		_window.PushGraphUndo( "Duplicate Virtual Parameter" );
		_window.Graph.AddVirtualParameter( copy, _window.Graph.VirtualParameters.IndexOf( parameter ) + 1 );
		_window.PushGraphRedo();

		BeginRename( copy );
	}

	internal void Rename( IParameter parameter, string name )
	{
		name = name?.Trim();

		if ( string.IsNullOrEmpty( name ) || name == parameter.Name )
			return;

		if ( _window.Graph.AllParameters.Any( p => p != parameter && p.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) )
			return;

		var oldName = parameter.Name;
		_window.PushGraphUndo( "Rename Parameter" );

		foreach ( var node in _window.Graph.AllNodes.OfType<Nodes.ParameterNode>()
			.Where( reference => reference.ParameterIdentifier == parameter.Identifier
				|| string.Equals( reference.ParameterName, oldName, StringComparison.OrdinalIgnoreCase ) ) )
		{
			node.ParameterIdentifier = parameter.Identifier;
			node.ParameterName = name;
			node.Update();
		}

		parameter.Name = name;

		foreach ( var selector in _window.Graph.AllNodes.OfType<Nodes.IDBasedSelectorNode>()
			.Where( node => node.ParameterIdentifier == parameter.Identifier
				|| string.Equals( node.ParameterName, oldName, StringComparison.OrdinalIgnoreCase ) ) )
		{
			selector.ParameterIdentifier = parameter.Identifier;
			selector.ParameterName = name;
			selector.SynchronizeOptionsFromParameter();
			selector.Update();
		}

		if ( parameter is VirtualParameter virtualParameter )
			_window.OnVirtualParameterRenamed( virtualParameter );
		_window.PushGraphRedo();
		_window.OnParameterSelected( parameter );
	}

	internal void BeginRename( IParameter parameter )
		=> _rowWidgets.FirstOrDefault( r => r.Parameter == parameter )?.StartRename();

	internal void ToggleGroup( string group )
	{
		if ( !string.IsNullOrWhiteSpace( Filter ) )
			return;

		if ( !_collapsedGroups.Add( group ) )
			_collapsedGroups.Remove( group );

		SaveCollapsedGroups();
		Rebuild();
	}

	internal void MoveToGroup( IParameter parameter, string group )
	{
		group = NormalizeGroup( group );
		if ( GroupName( parameter ).Equals( group, StringComparison.OrdinalIgnoreCase ) )
			return;

		_window.PushGraphUndo( "Move Parameter" );
		parameter.Group = group;
		ExpandGroup( group );
		PruneCollapsedGroups();
		_window.PushGraphRedo();
		_window.OnParameterSelected( parameter );
	}

	internal void RenameGroup( string group )
	{
		if ( string.IsNullOrEmpty( group ) )
			return;

		OpenGroupDialog( "Rename Parameter Group", group, newName =>
		{
			newName = NormalizeGroup( newName );
			if ( newName.Equals( group, StringComparison.OrdinalIgnoreCase ) )
				return;

			_window.PushGraphUndo( "Rename Parameter Group" );
			foreach ( var parameter in _window.Graph.AllParameters.Where( p => GroupName( p ).Equals( group, StringComparison.OrdinalIgnoreCase ) ) )
				parameter.Group = newName;

			if ( _collapsedGroups.Remove( group ) && !string.IsNullOrEmpty( newName ) )
				_collapsedGroups.Add( newName );
			PruneCollapsedGroups();

			_window.PushGraphRedo();
		} );
	}

	internal void ClearGroup( string group )
	{
		if ( string.IsNullOrEmpty( group ) )
			return;

		_window.PushGraphUndo( "Remove Parameter Group" );
		foreach ( var parameter in _window.Graph.AllParameters.Where( p => GroupName( p ).Equals( group, StringComparison.OrdinalIgnoreCase ) ) )
			parameter.Group = "";
		_collapsedGroups.Remove( group );
		SaveCollapsedGroups();
		_window.PushGraphRedo();
	}

	internal void AddGroupOptions( Menu menu, IParameter parameter )
	{
		var groups = menu.AddMenu( "Move to Group", "folder" );
		groups.AddOption( "General", string.IsNullOrEmpty( GroupName( parameter ) ) ? "check" : "", () => MoveToGroup( parameter, "" ) );

		foreach ( var group in ExistingGroups() )
			groups.AddOption( group, GroupName( parameter ).Equals( group, StringComparison.OrdinalIgnoreCase ) ? "check" : "", () => MoveToGroup( parameter, group ) );

		groups.AddSeparator();
		groups.AddOption( "New Group…", "create_new_folder", () => OpenGroupDialog( "New Parameter Group", "", group => MoveToGroup( parameter, group ) ) );
	}

	private IEnumerable<string> ExistingGroups() => _window.Graph.AllParameters
		.Select( GroupName )
		.Where( group => !string.IsNullOrEmpty( group ) )
		.Distinct( StringComparer.OrdinalIgnoreCase )
		.OrderBy( group => group, StringComparer.OrdinalIgnoreCase );

	private void OpenGroupDialog( string title, string initialValue, Action<string> accepted )
	{
		Dialog.AskString(
			name => accepted( name.Trim() ),
			"Group name:",
			okay: "Apply",
			initialName: initialValue,
			title: title,
			minLength: 1
		);
	}

	//Everything that can be added, under the button that asked - or under the dock, where the empty list itself was clicked.
	internal void OpenAddMenu( Panel anchor = null )
	{
		var menu = CreateMenu();
		AddControlOptions( menu, "" );
		menu.AddSeparator();
		var virtuals = menu.AddMenu( "Virtual Parameter", "add_circle" );
		AddVirtualOptions( virtuals, "" );
		AddGroupedOptions( menu, false );

		var under = anchor is { IsValid: true }
			? new Vector2( anchor.Box.Rect.Left * anchor.ScaleFromScreen, anchor.Box.Rect.Bottom * anchor.ScaleFromScreen )
			: _panel.ScreenRect.BottomLeft;

		menu.OpenAt( under );
	}

	private void AddGroupedOptions( Menu menu, bool isVirtual )
	{
		var groups = ExistingGroups().ToList();
		if ( groups.Count == 0 )
			return;

		menu.AddSeparator();
		var grouped = menu.AddMenu( "Add to Group", "folder" );
		foreach ( var group in groups )
		{
			var groupMenu = grouped.AddMenu( group, "folder" );
			if ( isVirtual )
				AddVirtualOptions( groupMenu, group );
			else
				AddControlOptions( groupMenu, group );
		}
	}

	internal void AddControlOptions( Menu menu, string group )
	{
		foreach ( var type in AnimParameterUI.ControlTypes )
			menu.AddOption( AnimParameterUI.TypeName( type ), AnimParameterUI.Icon( type ), () => AddControl( type, group ) );
	}

	internal void AddVirtualOptions( Menu menu, string group )
	{
		foreach ( var type in AnimParameterUI.VirtualTypes )
			menu.AddOption( AnimParameterUI.TypeName( type ), AnimParameterUI.Icon( type ), () => AddVirtual( type, group ) );
	}

	internal void AddControl( AnimParameterType type, string group = "" )
	{
		group = NormalizeGroup( group );
		var parameter = new Parameter { Name = UniqueName( type.ToString() ), Type = type, Group = group };

		_panel?.ClearFilter();
		ExpandGroup( group );

		_window.PushGraphUndo( "Add Parameter" );
		_window.Graph.Parameters.Add( parameter );
		_window.PushGraphRedo();

		//Rebuilt explicitly: setting the filter from code raises no event, so no row would appear
		Rebuild();

		_window.OnParameterSelected( parameter );
		BeginRename( parameter );
	}

	internal void AddVirtual( AnimParameterType type, string group = "" )
	{
		group = NormalizeGroup( group );
		VirtualParameter parameter = type switch
		{
			AnimParameterType.Bool => new BoolVirtualParameter(),
			AnimParameterType.ID => new IDVirtualParameter(),
			AnimParameterType.Float => new FloatVirtualParameter(),
			AnimParameterType.Vector => new VectorVirtualParameter(),
			AnimParameterType.Target => new TargetVirtualParameter(),
			AnimParameterType.BoneMask => new BoneMaskVirtualParameter(),
			_ => throw new ArgumentOutOfRangeException( nameof( type ) )
		};
		parameter.Name = UniqueName( type.ToString() );
		parameter.Group = group;

		_panel?.ClearFilter();
		ExpandGroup( group );

		_window.PushGraphUndo( "Add Virtual Parameter" );
		_window.Graph.AddVirtualParameter( parameter );
		_window.PushGraphRedo();

		//Rebuilt explicitly: setting the filter from code raises no event, so no row would appear
		Rebuild();

		_window.OnParameterSelected( parameter );
		BeginRename( parameter );
	}

	private string UniqueName( string baseName ) => _window.Graph.UniqueParameterName( baseName );

	private static string NormalizeGroup( string group )
	{
		group = group?.Trim() ?? "";
		return group.Equals( "General", StringComparison.OrdinalIgnoreCase ) ? "" : group;
	}

	private void ExpandGroup( string group )
	{
		if ( _collapsedGroups.Remove( group ) )
			SaveCollapsedGroups();
	}

	private void PruneCollapsedGroups()
	{
		var groups = _window.Graph.AllParameters.Select( GroupName ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		_collapsedGroups.RemoveWhere( group => !groups.Contains( group ) );
		SaveCollapsedGroups();
	}

	private void SaveCollapsedGroups()
		=> EditorCookie.Set( CollapsedGroupsCookie, _collapsedGroups.OrderBy( x => x ).ToList() );

	internal void OnGraphEdited()
	{
		_usageCounts = null;

		if ( _window.Graph is null )
			return;

		var parameters = DisplayedParameters().ToList();
		var stale = _rowWidgets.Count != parameters.Count;

		for ( var i = 0; !stale && i < _rowWidgets.Count; i++ )
		{
			var p = parameters[i];
			stale = _rowWidgets[i].Parameter != p || _rowWidgets[i].BuiltGroup != GroupName( p );
			if ( !stale && p is Parameter control && _rowWidgets[i] is ParameterRow row )
			{
				var range = control.HasRange ? new Vector2( control.Min, control.Max ) : (Vector2?)null;
				stale = row.BuiltType != control.Type || row.BuiltRange != range || row.BuiltOptions != ParameterRow.OptionsKey( control );
			}
		}

		if ( stale )
		{
			Rebuild();
			return;
		}

		UpdateUnusedButton();
		UpdateSelection();
	}

	private void UpdateUnusedButton()
		=> _panel?.SetCanDeleteUnused( _window.Graph is { } graph && graph.AllParameters.Any( p => UsageCount( p ) == 0 ) );

	//How many parameter nodes reference this parameter, cached per graph edit so row paints don't walk the whole graph.
	internal int UsageCount( IParameter parameter )
	{
		_usageCounts ??= BuildUsageCounts();
		return _usageCounts.GetValueOrDefault( parameter );
	}

	private Dictionary<IParameter, int> BuildUsageCounts()
	{
		var counts = new Dictionary<IParameter, int>();
		if ( _window.Graph is not { } graph )
			return counts;

		var byIdentifier = new Dictionary<string, IParameter>();
		var byName = new Dictionary<string, IParameter>();

		foreach ( var parameter in graph.AllParameters )
		{
			byIdentifier.TryAdd( parameter.Identifier, parameter );
			byName.TryAdd( parameter.Name, parameter );
		}

		foreach ( var node in graph.AllNodes.OfType<Nodes.ParameterNode>() )
		{
			if ( (node.ParameterIdentifier is { } id && byIdentifier.TryGetValue( id, out var parameter ))
				|| (node.ParameterName is { } name && byName.TryGetValue( name, out parameter )) )
			{
				counts[parameter] = counts.GetValueOrDefault( parameter ) + 1;
			}
		}

		return counts;
	}
}

internal static class AnimParameterUI
{
	public const float RowSpacing = 4f;

	public static IReadOnlyList<AnimParameterType> ControlTypes { get; } =
	[
		AnimParameterType.Bool,
		AnimParameterType.ID,
		AnimParameterType.Float,
		AnimParameterType.Vector,
		AnimParameterType.Target
	];

	public static IReadOnlyList<AnimParameterType> VirtualTypes { get; } = Enum.GetValues<AnimParameterType>();

	public static string Icon( AnimParameterType type ) => type switch
	{
		AnimParameterType.Float => "pin",
		AnimParameterType.Bool => "toggle_on",
		AnimParameterType.ID => "label",
		AnimParameterType.Target => "my_location",
		AnimParameterType.BoneMask => "blur_on",
		_ => "open_with",
	};

	public static Color Color( AnimParameterType type ) => type switch
	{
		AnimParameterType.Bool => global::Color.Parse( "#e06c75" ).Value,
		AnimParameterType.ID => global::Color.Parse( "#c678dd" ).Value,
		AnimParameterType.Float => global::Color.Parse( "#98c379" ).Value,
		AnimParameterType.Vector => global::Color.Parse( "#e5c07b" ).Value,
		AnimParameterType.Target => global::Color.Parse( "#56b6c2" ).Value,
		AnimParameterType.BoneMask => global::Color.Parse( "#d19a66" ).Value,
		_ => Theme.TextControl,
	};

	public static string TypeName( AnimParameterType type ) => type == AnimParameterType.BoneMask ? "Bone Mask" : type.ToString();
}

/// <summary>Carried in drag data when dragging a parameter onto the graph.</summary>
public record ParameterDragData( IParameter Parameter );


internal interface IParameterRow
{
	IParameter Parameter { get; }
	string BuiltGroup { get; }
	void StartRename();
	void RefreshPreview();

	//Read whether this row is the one selected, and say so.
	void ReadSelection();
}

//The toolbar, and the well the groups scroll in.
[StyleSheet( "AnimGraphParameterList.scss" )]
internal sealed class ParameterListPanel : EditorPanel
{
	private readonly ParameterList _list;
	private readonly TextField _filter;
	private readonly PanelToolbarButton _deleteUnused;
	private readonly PanelToolbarButton _add;
	private readonly Panel _well;
	private readonly UILabel _empty;

	/// <summary>The column the groups are built into.</summary>
	public Panel Rows { get; }

	public string Filter => _filter?.Value ?? "";

	public ParameterListPanel( ParameterList list )
	{
		_list = list;

		AddClass( "parameter-list" );

		var toolbar = AddChild( new PanelToolbar() );

		_filter = toolbar.AddFilter( "Filter parameters...", _ => _list.Rebuild() );

		toolbar.AddIcon( "swap_vert", "Sort alphabetically", _list.ToggleSort );
		toolbar.AddSeparator();
		toolbar.AddIcon( "restart_alt", "Reset all previews to start values", _list.ResetAllPreviewsFromToolbar );
		toolbar.AddIcon( "casino", "Randomize all previews", _list.RandomizeAllPreviews );
		_deleteUnused = toolbar.AddIcon( "delete_sweep", "Delete all unused parameters", _list.DeleteUnusedFromToolbar );
		toolbar.AddSeparator();
		_add = toolbar.AddIcon( "add",
			"<b>Add Parameter</b><p>Adds a runtime input supplied by game code, or a virtual value calculated inside this graph.</p>",
			() => _list.OpenAddMenu( _add ) );

		_well = AddChild<Panel>( "well" );

		//A click on the empty list is the same as reaching for the plus.
		_well.AddEventListener( "onclick", () =>
		{
			if ( _empty.HasClass( "hidden" ) ) return;
			_list.OpenAddMenu( _add );
		} );

		Rows = _well.AddChild<Panel>( "rows" );

		_empty = _well.AddChild<UILabel>( "empty hidden" );
	}

	/// <summary>Whether there is nothing to show, and what to say instead.</summary>
	public void SetEmpty( bool empty, string hint )
	{
		if ( !_empty.IsValid() ) return;

		_empty.Text = hint;
		_empty.SetClass( "hidden", !empty );
	}

	public void SetCanDeleteUnused( bool can )
	{
		if ( _deleteUnused.IsValid() )
			_deleteUnused.Enabled = can;
	}

	public void ClearFilter()
	{
		if ( _filter.IsValid() )
			_filter.Value = "";
	}
}

//The bar at the top of a group of parameters: how many are in it, and a place to drop one to move it here.
internal sealed class ParameterGroupHeader : PanelFoldHeader, IEditorDropTarget
{
	private readonly ParameterList _list;
	private readonly string _group;
	private readonly bool _collapsible;

	public ParameterGroupHeader( ParameterList list, string group, int count, bool collapsed, bool collapsible )
	{
		_list = list;
		_group = group;
		_collapsible = collapsible;

		AddClass( "parameter-group" );
		SetClass( "fixed", !collapsible );

		Title = ParameterList.GroupTitle( group );
		Icon = "folder";
		Expanded = !collapsed;

		SyncContentVisible( !collapsed );

		var badge = AddChild<UILabel>( "count" );
		badge.Text = count.ToString();

		Tooltip = collapsible
			? "Click to collapse or expand. Drag parameters here to move them into this group. Right-click for group actions."
			: "Matching groups are expanded while filtering. Drag parameters here to move them into this group. Right-click for group actions.";

		OnExpandedChanged = _ => _list.ToggleGroup( _group );
	}

	//While a filter is on, every matching group is open and stays open - so the bar is not a thing you can fold.
	protected override bool IsFoldingClick( Panel target ) => _collapsible;

	protected override void OnContextMenu()
	{
		var menu = _list.CreateMenu();

		var controls = menu.AddMenu( "Add Control Parameter", "add" );
		_list.AddControlOptions( controls, _group );

		var virtuals = menu.AddMenu( "Add Virtual Parameter", "add_circle" );
		_list.AddVirtualOptions( virtuals, _group );

		if ( !string.IsNullOrEmpty( _group ) )
		{
			menu.AddSeparator();
			menu.AddOption( "Rename Group", "edit", () => _list.RenameGroup( _group ) );
			menu.AddOption( "Remove Group", "folder_off", () => _list.ClearGroup( _group ) );
		}

		menu.OpenAtCursor();
	}

	public bool AcceptEditorDrops => true;

	public DropAction OnEditorDragHover( Widget.DragEvent ev )
	{
		var valid = ev.Data.Object is ParameterDragData data
			&& !ParameterList.GroupName( data.Parameter ).Equals( _group, StringComparison.OrdinalIgnoreCase );

		SetClass( "drop-target", valid );

		return valid ? DropAction.Move : DropAction.Ignore;
	}

	public void OnEditorDragLeave() => SetClass( "drop-target", false );

	public void OnEditorDragDrop( Widget.DragEvent ev )
	{
		SetClass( "drop-target", false );

		if ( ev.Data.Object is ParameterDragData data )
			_list.MoveToGroup( data.Parameter, _group );
	}
}

//One parameter row: shared pill, name, drag and rename; subclasses add the right side
internal abstract class ParameterRowPanel : Panel, IParameterRow
{
	protected readonly Window Window;
	protected readonly ParameterList List;

	public IParameter Parameter { get; }
	public string BuiltGroup { get; }

	//The pill. What a row adds goes beside it, not in it.
	protected Panel Pill { get; }

	private readonly UILabel _kind;
	private readonly UILabel _icon;
	private readonly UILabel _name;
	private readonly UILabel _usage;
	private readonly UILabel _type;
	private readonly TextField _rename;

	private bool _pressed;
	private bool _carrying;
	private Vector2 _pressAt;

	protected ParameterRowPanel( Window window, ParameterList list, IParameter parameter )
	{
		Window = window;
		List = list;
		Parameter = parameter;
		BuiltGroup = ParameterList.GroupName( parameter );

		AddClass( "parameter-row" );
		SetClass( "virtual", parameter.IsVirtual );

		Pill = AddChild<Panel>( "pill" );

		_kind = Pill.AddChild<UILabel>( "kind" );
		_icon = Pill.AddChild<UILabel>( "material-icon type-icon" );
		_name = Pill.AddChild<UILabel>( "name" );
		_usage = Pill.AddChild<UILabel>( "usage hidden" );
		_type = Pill.AddChild<UILabel>( "type" );

		_rename = Pill.AddChild<TextField>();
		_rename.AddClass( "rename" );
		_rename.AddClass( "hidden" );
		_rename.Submitted = _ => FinishRename();

		//Clicking away is finishing too. The widget line edit said so itself - its EditingFinished fired
		//on losing focus as well as on Enter - and without this the box stays open over the row for good.
		_rename.AddEventListener( "onblur", FinishRename );

		var handle = Pill.AddChild<UILabel>( "material-icon handle" );
		handle.Text = "drag_indicator";
	}

	//Fills the pill in. Called by the row once it has whatever it needs - a constructor cannot, because Parameter is the subclass's.
	protected void ReadParameter()
	{
		var parameter = Parameter;
		var type = parameter.Type;

		SetClass( "virtual", parameter.IsVirtual );

		_kind.Text = parameter.IsVirtual ? "V" : "C";
		_icon.Text = AnimParameterUI.Icon( type );
		_name.Text = parameter.Name;
		_type.Text = AnimParameterUI.TypeName( type );

		//The type's colour is the parameter's own, which is the one thing a stylesheet has nothing to say about.
		var colour = AnimParameterUI.Color( type );

		_icon.Style.FontColor = colour;
		_type.Style.FontColor = colour.WithAlpha( 0.7f );
		Pill.Style.BackgroundColor = colour.WithAlpha( 0.1f );
		Pill.Style.BorderColor = colour.WithAlpha( 0.8f );

		Tooltip = $"{type} parameter\n{DragHint}\nDouble-click the parameter pill to rename";

		ReadUsage();
		ReadSelection();
	}

	//What the tooltip says dragging the row does.
	protected virtual string DragHint => "Drag onto the graph to create a node";

	public void ReadSelection()
	{
		var selected = Window.IsSelected( Parameter );

		SetClass( "selected", selected );

		if ( Pill.IsValid() )
			Pill.Style.BackgroundColor = AnimParameterUI.Color( Parameter.Type ).WithAlpha( selected ? 0.25f : 0.1f );
	}

	private void ReadUsage()
	{
		var usage = List.UsageCount( Parameter );

		_usage.SetClass( "hidden", usage <= 0 );
		_usage.Text = usage > 0 ? $"×{usage}" : "";
	}

	public virtual void RefreshPreview()
	{
		ReadUsage();
		ReadSelection();
	}

	public void StartRename()
	{
		if ( !_rename.HasClass( "hidden" ) )
			return;

		_rename.Value = Parameter.Name;
		_rename.SetClass( "hidden", false );
		_rename.Focus();
		_rename.SelectAll();
	}

	private void FinishRename()
	{
		if ( _rename.HasClass( "hidden" ) )
			return;

		_rename.SetClass( "hidden", true );

		List.Rename( Parameter, _rename.Value );
	}

	//---- pointer ---------------------------------------------------------

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( !IsUnderPill( e.Target as Panel ) )
			return;

		if ( e.MouseButton == MouseButtons.Left )
		{
			_pressed = true;
			_carrying = false;
			_pressAt = Application.CursorPosition;

			Window.OnParameterSelected( Parameter );
			e.StopPropagation();
		}
		else if ( e.MouseButton == MouseButtons.Right )
		{
			Window.OnParameterSelected( Parameter );
			OpenContextMenu();
			e.StopPropagation();
		}
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		base.OnDoubleClick( e );

		if ( e.MouseButton != MouseButtons.Left ) return;
		if ( !IsUnderPill( e.Target as Panel ) ) return;

		_pressed = false;

		OnActivate();

		e.StopPropagation();
	}

	//What a double-click on the pill does. Renaming, unless a row says otherwise.
	protected virtual void OnActivate() => StartRename();

	public override void Tick()
	{
		base.Tick();

		if ( !_pressed || _carrying ) return;

		if ( ( Application.MouseButtons & MouseButtons.Left ) == 0 )
		{
			_pressed = false;
			return;
		}

		if ( Application.CursorPosition.Distance( _pressAt ) <= 10.0f ) return;

		_carrying = true;

		EditorDrag.Begin( this, new DragData
		{
			Text = Parameter.Name,
			Object = new ParameterDragData( Parameter )
		}, null );
	}

	private bool IsUnderPill( Panel target )
	{
		for ( var p = target; p is not null; p = p.Parent )
		{
			if ( p == _rename ) return false;
			if ( p == Pill ) return true;
			if ( p == this ) return false;
		}

		return false;
	}

	//---- keys ------------------------------------------------------------

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
				OnDeleteKey();
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

	protected abstract void OnDeleteKey();

	protected abstract void OpenContextMenu();
}

//A control parameter: the pill, and the preview value beside it.
internal sealed class ParameterRow : ParameterRowPanel
{
	private readonly Parameter _parameter;

	public AnimParameterType BuiltType { get; }
	public Vector2? BuiltRange { get; }
	public string BuiltOptions { get; }

	private BaseControl _control;
	private BaseControl _targetBone;
	private BaseControl _targetPosition;
	private BaseControl _targetRotation;

	public ParameterRow( Window window, ParameterList list, Parameter parameter )
		: base( window, list, parameter )
	{
		_parameter = parameter;
		parameter.Graph = window.Graph;

		BuiltType = parameter.Type;
		BuiltRange = parameter.HasRange ? new Vector2( parameter.Min, parameter.Max ) : null;
		BuiltOptions = OptionsKey( parameter );

		ReadParameter();

		var so = parameter.GetSerialized();

		so.OnPropertyChanged += p =>
		{
			if ( ParameterList.AffectsIDSelectors( p ) )
			{
				List.SynchronizeIDSelectors( parameter,
					ParameterList.ResetsIDSelectorOptions( p ) );
			}

			//Transient preview scrubs don't touch the document
			if ( !p.HasAttribute<JsonIgnoreAttribute>() )
				Window.MarkDirty();

			Window.SetPreviewParameter( parameter );

			if ( p.Name == nameof( MdlEditor.Parameter.PreviewTargetMode ) )
				ReadTargetControls();
		};

		BuildValue( so );
		BuildActions();
	}

	private void BuildValue( SerializedObject so )
	{
		var values = AddChild<Panel>( "values" );

		if ( Parameter.Type == AnimParameterType.Target )
		{
			values.AddClass( "target" );

			_control = AddControl( values, so, nameof( MdlEditor.Parameter.PreviewTargetMode ), "Target mode" );
			_targetBone = AddControl( values, so, nameof( MdlEditor.Parameter.PreviewBone ), "Target bone" );
			_targetPosition = AddControl( values, so, nameof( MdlEditor.Parameter.PreviewPosition ), "Target position" );
			_targetRotation = AddControl( values, so, nameof( MdlEditor.Parameter.PreviewRotation ), "Target rotation" );

			ReadTargetControls();
			return;
		}

		var property = Parameter.Type switch
		{
			AnimParameterType.Float => nameof( MdlEditor.Parameter.PreviewFloat ),
			AnimParameterType.Bool => nameof( MdlEditor.Parameter.PreviewBool ),
			AnimParameterType.ID => nameof( MdlEditor.Parameter.PreviewID ),
			_ => nameof( MdlEditor.Parameter.PreviewVector ),
		};

		values.SetClass( "checkbox", Parameter.Type == AnimParameterType.Bool );

		_control = AddControl( values, so, property, null );
	}

	//One property editor, panel-native. A float with a range gets a slider because [Range]
	//is what the slider control registers itself for - there is nothing to ask for by hand.
	private static BaseControl AddControl( Panel parent, SerializedObject so, string name, string tooltip )
	{
		var property = so.GetProperty( name );
		if ( property is null ) return null;

		var control = PanelControlFactory.CreateFor( property );
		if ( control is null ) return null;

		parent.AddChild( control );

		if ( !string.IsNullOrEmpty( tooltip ) )
			control.Tooltip = tooltip;

		return control;
	}

	private void ReadTargetControls()
	{
		if ( _targetBone is null ) return;

		var bone = _parameter.PreviewTargetMode == AnimTargetPreviewMode.Bone;
		var transform = _parameter.PreviewTargetMode == AnimTargetPreviewMode.Transform;

		_targetBone.SetClass( "hidden", !bone );
		_targetPosition?.SetClass( "hidden", !transform );
		_targetRotation?.SetClass( "hidden", !transform );
	}

	private void BuildActions()
	{
		var actions = AddChild<Panel>( "actions" );

		var reset = actions.AddChild<UILabel>( "material-icon action" );
		reset.Text = "restart_alt";
		reset.Tooltip = "Reset preview to start value";
		reset.AddEventListener( "onclick", () => List.ResetPreview( _parameter ) );

		var remove = actions.AddChild<UILabel>( "material-icon action danger" );
		remove.Text = "delete";
		remove.Tooltip = "Delete parameter";
		remove.AddEventListener( "onclick", () => List.Remove( _parameter ) );
	}

	internal static string OptionsKey( Parameter parameter )
		=> parameter.Type == AnimParameterType.ID ? string.Join( '\u001F', parameter.Options ) : "";

	protected override void OnDeleteKey() => List.Remove( _parameter );

	protected override void OpenContextMenu()
	{
		var menu = List.CreateMenu();
		menu.AddOption( "Rename", "edit", StartRename, "F2" );
		menu.AddOption( "Duplicate", "content_copy", () => List.Duplicate( _parameter ), "Ctrl+D" );
		List.AddGroupOptions( menu, _parameter );

		menu.AddSeparator();
		menu.AddOption( "Reset Preview To Start Value", "restart_alt", () => List.ResetPreview( _parameter ) );

		if ( Parameter.Type != AnimParameterType.Target && Parameter.Type != AnimParameterType.ID )
			menu.AddOption( "Randomize Preview", "casino", () => ApplyPreview( _parameter.RandomizePreview ) );

		menu.AddSeparator();
		menu.AddOption( "Delete", "delete", () => List.Remove( _parameter ), "Del" );
		menu.OpenAtCursor();
	}

	private void ApplyPreview( Action action )
	{
		action();
		Window.SetPreviewParameter( _parameter );
		List.Rebuild();
	}
}

//A virtual parameter: a value this graph works out for itself.
internal sealed class VirtualParameterRow : ParameterRowPanel
{
	private readonly VirtualParameter _parameter;

	protected override string DragHint => "Drag onto the graph to create a reference";

	public VirtualParameterRow( Window window, ParameterList list, VirtualParameter parameter )
		: base( window, list, parameter )
	{
		_parameter = parameter;

		ReadParameter();

		var actions = AddChild<Panel>( "actions" );

		var remove = actions.AddChild<UILabel>( "material-icon action danger" );
		remove.Text = "delete";
		remove.Tooltip = "Delete virtual parameter";
		remove.AddEventListener( "onclick", () => List.Remove( _parameter ) );
	}

	//A virtual parameter is a graph of its own, so opening it is what a double-click means. Renaming is the name itself, or F2.
	protected override void OnActivate() => Window.NavigateInto( _parameter );

	protected override void OnDeleteKey() => List.Remove( _parameter );

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Pressed && e.Button == "enter" )
		{
			Window.NavigateInto( _parameter );
			e.StopPropagation = true;
			return;
		}

		base.OnButtonTyped( e );
	}

	protected override void OpenContextMenu()
	{
		var menu = List.CreateMenu();
		menu.AddOption( "Edit", "edit", () => Window.NavigateInto( _parameter ), "Enter" );
		menu.AddOption( "Rename", "drive_file_rename_outline", StartRename, "F2" );
		menu.AddOption( "Duplicate", "content_copy", () => List.Duplicate( _parameter ), "Ctrl+D" );
		List.AddGroupOptions( menu, _parameter );
		menu.AddSeparator();
		menu.AddOption( "Delete", "delete", () => List.Remove( _parameter ), "Del" );
		menu.OpenAtCursor();
	}
}
