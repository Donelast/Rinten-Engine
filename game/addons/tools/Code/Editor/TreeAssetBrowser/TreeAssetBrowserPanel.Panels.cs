using Rinten.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Editor.TreeBrowser;

public partial class TreeAssetBrowserPanel
{
	const float DefaultTreeWidth = 220f;
	const int LayoutVersion = 3;

	string CookiePrefix => Options.ResolveCookiePrefix( InstanceId );

	string BrowserStateCookieKey => $"{CookiePrefix}.State";
	string TypeFilterCookieKey => $"{CookiePrefix}.TypeFilter";
	string ScopeCookieKey => $"{CookiePrefix}.Scope";
	string SectionCookieKey => $"{CookiePrefix}.Section";

	readonly List<IBrowserPanel> _panels = new();
	readonly List<BrowserPanelSplitter> _splitters = new();

	AssetTypeFilter _typeFilter;
	IAssetFilter _filter;
	AssetTreeScope? _scope;
	AssetTreeSection? _section;

	int _activeTabIndex;
	bool _isTabMode;
	bool _layoutRestorePending;
	bool _deferredTabMode;
	List<float> _deferredWidths;
	bool _panelsMounted;
	BrowserSettingsPopup _settingsPopup;

	/// <summary>The asset types the user has narrowed this browser down to.</summary>
	public AssetTypeFilter TypeFilter => _typeFilter ??= CreateTypeFilter();

	/// <summary>What the browser's own limits and the user's chosen types allow through, together.</summary>
	public IAssetFilter Filter => _filter ??= new CompositeAssetFilter( Options, TypeFilter );

	/// <summary>Project content or mounted games.</summary>
	public AssetTreeScope Scope => _scope ??= RestoreScope();

	AssetTypeFilter CreateTypeFilter()
	{
		var filter = AssetTypeFilter.Load( TypeFilterCookieKey );
		filter.Changed += OnTypeFilterChanged;
		return filter;
	}

	void OnTypeFilterChanged()
	{
		foreach ( var panel in _panels )
			panel.FilterChanged();

		StateHasChanged();
	}

	AssetTreeScope RestoreScope()
	{
		var saved = ProjectCookie.Get( ScopeCookieKey, nameof( AssetTreeScope.Project ) );
		return saved == nameof( AssetTreeScope.Mounts ) && Options.EnableMounts
			? AssetTreeScope.Mounts
			: AssetTreeScope.Project;
	}

	/// <summary>Switches every panel over to the other body of content.</summary>
	public void SetScope( AssetTreeScope scope )
	{
		if ( Scope == scope ) return;

		_scope = scope;
		ProjectCookie.Set( ScopeCookieKey, scope.ToString() );

		foreach ( var panel in _panels )
			panel.SetScope( scope );

		StateHasChanged();
	}

	/// <summary>Which section the strip has picked. See AssetTreeSection.</summary>
	public AssetTreeSection Section => Project.Current is null ? RestoreSection() : _section ??= RestoreSection();

	AssetTreeSection RestoreSection()
		=> AssetTreeSections.Restore( ProjectCookie.Get( SectionCookieKey, "" ), Options );

	/// <summary>The sections this project has, which is what the strip draws.</summary>
	public IReadOnlyList<AssetTreeSections.Entry> Sections => AssetTreeSections.For( Options );

	/// <summary>Points every panel at another section of the content.</summary>
	public void SetSection( AssetTreeSection section )
	{
		if ( Section == section ) return;

		_section = section;
		ProjectCookie.Set( SectionCookieKey, section.ToString() );

		foreach ( var panel in _panels )
			panel.SetSection( section );

		StateHasChanged();
	}

	public bool RestoreBrowserState()
	{
		var state = ProjectCookie.Get<BrowserState>( BrowserStateCookieKey, null );
		if ( state?.PanelTypes is null || state.PanelTypes.Count == 0 )
			return false;

		if ( state.LayoutVersion < LayoutVersion )
			return false;

		foreach ( var panelType in state.PanelTypes )
		{
			switch ( panelType )
			{
				case "Tree":
					AddTreePanel( saveState: false );
					break;
				case "IconGrid":
					AddIconGridPanel( saveState: false );
					break;
			}
		}

		_activeTabIndex = Math.Clamp( state.ActiveTabIndex, 0, Math.Max( 0, _panels.Count - 1 ) );

		_layoutRestorePending = true;
		_deferredTabMode = state.IsTabMode && _panels.Count > 1;
		_deferredWidths = state.IsTabMode ? null : state.PanelWidths;

		return _panels.Count > 0;
	}

	public void SaveBrowserState()
	{
		var state = new BrowserState
		{
			LayoutVersion = LayoutVersion,
			IsTabMode = _isTabMode,
			ActiveTabIndex = _activeTabIndex,
			PanelTypes = _panels.Select( GetPanelTypeName ).ToList(),
			PanelWidths = _panels.Select( p => p.Root.Box.Rect.Width ).ToList()
		};

		ProjectCookie.Set( BrowserStateCookieKey, state );

		if ( Options.IsPickerMode )
			return;

		var savedIds = ProjectCookie.Get<List<string>>( TreeAssetBrowser.AllBrowserIdsCookieKey, new List<string>() );
		if ( !savedIds.Contains( InstanceId ) )
		{
			savedIds.Add( InstanceId );
			ProjectCookie.Set( TreeAssetBrowser.AllBrowserIdsCookieKey, savedIds );
		}
	}

	public void CreateDefaultPanels()
	{
		AddTreePanel( saveState: false );
		AddIconGridPanel( saveState: false );
		PinDefaultTreeWidth();
		SaveBrowserState();
	}

	public void AddTreePanel( bool saveState = true )
	{
		var panelId = $"{InstanceId}_Panel{_panels.Count}";
		var panel = new AssetBrowserPanel { PanelId = panelId, Options = Options, Filter = Filter };
		panel.SetScope( Scope );
		panel.SetSection( Section );
		WireTreePanel( panel );
		AddPanelToContainer( panel );

		_activeTabIndex = _panels.Count - 1;
		UpdatePanelButtons();

		if ( _isTabMode )
			SelectTab( _activeTabIndex );

		if ( saveState )
			SaveBrowserState();
	}

	public void AddIconGridPanel( bool saveState = true )
	{
		var panel = new IconGridPanel { Options = Options, Filter = Filter };
		WireGridPanel( panel );

		AddPanelToContainer( panel );

		_activeTabIndex = _panels.Count - 1;
		UpdatePanelButtons();

		if ( _isTabMode )
			SelectTab( _activeTabIndex );

		if ( saveState )
			SaveBrowserState();
	}

	void WireTreePanel( AssetBrowserPanel panel )
	{
		panel.OnCloseRequested = () => RemovePanel( panel );
		panel.OnMoveLeftRequested = () => MovePanelLeft( panel );
		panel.OnMoveRightRequested = () => MovePanelRight( panel );
		panel.OnAssetSelected = asset =>
		{
			OnAssetSelected?.Invoke( asset );
			TreeAssetBrowser.RaiseAssetHighlighted( asset );
		};
		panel.OnAssetHighlighted = HighlightAsset;
		panel.OnAssetActivated = ActivateAsset;
		panel.OnFileSelected = path => OnFileSelected?.Invoke( path );
		panel.OnFolderSelected = path => OnFolderSelected?.Invoke( path );
		panel.OnFolderClicked = OnFolderSelectedInTree;
		panel.OnSectionSelected = OnSectionSelectedInTree;
		panel.OnBrowseLocationSelected = OnBrowseLocationSelectedInTree;
	}

	void WireGridPanel( IconGridPanel panel )
	{
		panel.OnCloseRequested = () => RemovePanel( panel );
		panel.OnMoveLeftRequested = () => MovePanelLeft( panel );
		panel.OnMoveRightRequested = () => MovePanelRight( panel );
		panel.OnAssetHighlighted = HighlightAsset;
		panel.OnAssetActivated = ActivateAsset;
		panel.OnFileActivated = path => OnFileSelected?.Invoke( path );
		panel.OnSelectionChanged = assets => OnSelectionChanged?.Invoke( assets );
		panel.HasSectionAbove = path => FindSectionAbove( path ) is not null;
		panel.OnLeaveRoot = path =>
		{
			if ( FindSectionAbove( path ) is { } section )
				OnSectionSelectedInTree( section );
		};
	}

	AssetTreeItem FindSectionAbove( string path ) =>
		_panels.OfType<AssetBrowserPanel>().FirstOrDefault()?.Tree?.FindSectionContaining( path );

	void HighlightAsset( Asset asset ) => OnAssetHighlighted?.Invoke( asset );

	void ActivateAsset( Asset asset )
	{
		OnAssetActivated?.Invoke( asset );
		Options.Activation.Activate( asset );
	}

	void AddPanelToContainer( IBrowserPanel panel )
	{
		_panels.Add( panel );
		SyncLayoutToggle();

		if ( panelsHost is not null )
		{
			_panelsMounted = false;
			TryMountPanels();
		}
	}

	public void EnsurePanelsMounted() => TryMountPanels();

	void TryMountPanels()
	{
		if ( _panels.Count == 0 || panelsHost is null )
			return;

		//A hotload hands us a fresh, empty host while we still think we are mounted.
		if ( _panelsMounted && !panelsHost.HasChildren )
			_panelsMounted = false;

		if ( !_panelsMounted )
		{
			RebuildPanelsLayout();

			if ( !panelsHost.HasChildren )
				return;

			_panelsMounted = true;
		}

		ApplyDeferredLayout();
	}

	void ApplyDeferredLayout()
	{
		if ( !_layoutRestorePending )
			return;

		_layoutRestorePending = false;

		if ( _deferredTabMode )
			SwitchToTabMode();
		else if ( _deferredWidths is not null )
			RestorePanelWidths( _deferredWidths );
	}

	//Every panel but the last keeps the width it was given; the last takes the rest.
	static void ConfigurePanelLayout( Panel panel, bool flex )
	{
		if ( panel is null || !panel.IsValid() ) return;

		panel.SetClass( "hidden", false );
		panel.SetClass( "fixed", !flex );
	}

	public void RemovePanel( IBrowserPanel panel )
	{
		if ( _panels.Count <= 1 ) return;

		var index = _panels.IndexOf( panel );
		if ( index < 0 ) return;

		_panels.RemoveAt( index );
		panel.Root.Delete( true );

		RebuildPanelsLayout();

		if ( _panels.Count == 1 )
			ClearFixedWidth( _panels[0].Root );

		if ( _activeTabIndex >= _panels.Count )
			_activeTabIndex = _panels.Count - 1;

		if ( _isTabMode )
		{
			for ( var i = 0; i < _panels.Count; i++ )
				SetPanelVisible( _panels[i].Root, i == _activeTabIndex );

			foreach ( var splitter in _splitters )
				splitter.SetClass( "hidden", true );

			RebuildTabBar();

			if ( _panels.Count <= 1 )
				SwitchToNormalMode();
		}

		UpdatePanelButtons();
		SaveBrowserState();
	}

	public void MovePanelLeft( IBrowserPanel panel ) => SwapAdjacent( panel, -1 );

	public void MovePanelRight( IBrowserPanel panel ) => SwapAdjacent( panel, 1 );

	void SwapAdjacent( IBrowserPanel panel, int direction )
	{
		var index = _panels.IndexOf( panel );
		var other = index + direction;
		if ( index < 0 || other < 0 || other >= _panels.Count ) return;

		(_panels[index], _panels[other]) = (_panels[other], _panels[index]);
		RebuildPanelsLayout();
		UpdatePanelButtons();

		if ( _isTabMode )
			RebuildTabBar();

		SaveBrowserState();
	}

	void RebuildPanelsLayout()
	{
		if ( panelsHost is null )
			return;

		foreach ( var splitter in _splitters )
			splitter.Delete( true );
		_splitters.Clear();

		foreach ( var child in panelsHost.Children.ToArray() )
			child.Parent = null;

		for ( var i = 0; i < _panels.Count; i++ )
		{
			var root = _panels[i].Root;
			if ( root is null || !root.IsValid() )
				continue;

			if ( i > 0 )
			{
				var left = _panels[i - 1].Root;
				if ( left is null || !left.IsValid() )
					continue;

				var splitter = new BrowserPanelSplitter();
				splitter.Init( left, root, SaveBrowserState );
				panelsHost.AddChild( splitter );
				_splitters.Add( splitter );

				if ( _isTabMode )
					splitter.SetClass( "hidden", true );
			}

			ConfigurePanelLayout( root, flex: i == _panels.Count - 1 );
			panelsHost.AddChild( root );
		}

		UpdatePanelButtons();
	}

	void UpdatePanelButtons()
	{
		var showClose = ShowPanelControls && _panels.Count > 1;

		for ( var i = 0; i < _panels.Count; i++ )
		{
			var browserPanel = _panels[i];
			if ( _isTabMode )
			{
				browserPanel.ShowCloseButton = false;
				browserPanel.ShowMoveLeftButton = false;
				browserPanel.ShowMoveRightButton = false;
			}
			else
			{
				browserPanel.ShowCloseButton = showClose;
				browserPanel.ShowMoveLeftButton = showClose && i > 0;
				browserPanel.ShowMoveRightButton = showClose && i < _panels.Count - 1;
			}
		}
	}

	void OnFolderSelectedInTree( string folderPath )
	{
		foreach ( var panel in _panels.OfType<IconGridPanel>() )
			panel.ShowFolder( folderPath );
	}

	void OnBrowseLocationSelectedInTree( AssetBrowser.Location location )
	{
		foreach ( var panel in _panels.OfType<IconGridPanel>() )
			panel.ShowBrowseLocation( location );
	}

	void OnSectionSelectedInTree( AssetTreeItem section )
	{
		var treePanel = _panels.OfType<AssetBrowserPanel>().FirstOrDefault()?.Tree;
		if ( treePanel is null || section is null ) return;

		var items = treePanel.GetBrowsableChildren( section );
		foreach ( var panel in _panels.OfType<IconGridPanel>() )
			panel.ShowTreeItems( section.Label, items );
	}

	public void NavigateToFile( string path )
	{
		_panels.OfType<AssetBrowserPanel>().FirstOrDefault()?.Reveal( path );
	}

	/// <summary>Points the tree and every grid at a folder.</summary>
	public void NavigateToFolder( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return;

		_panels.OfType<AssetBrowserPanel>().FirstOrDefault()?.Reveal( path );
		OnFolderSelectedInTree( path );
	}

	/// <summary>Opens the folder an asset lives in and picks the asset out of it.</summary>
	public void FocusOnAsset( Asset asset )
	{
		if ( asset is null ) return;

		var path = asset.GetSourceFile( true ) ?? asset.AbsolutePath;
		if ( string.IsNullOrEmpty( path ) ) return;

		foreach ( var panel in _panels )
			panel.Reveal( path );
	}

	/// <summary>Assets picked out in the grids, or the tree when no grid is showing.</summary>
	public IReadOnlyList<Asset> GetSelectedAssets()
	{
		foreach ( var panel in _panels.OfType<IconGridPanel>() )
		{
			var assets = panel.GetSelectedAssets();
			if ( assets.Count > 0 )
				return assets;
		}

		return [];
	}

	public void RefreshAll()
	{
		SyncScopeTabs();
		SyncSection();

		foreach ( var panel in _panels )
			panel.Refresh();
	}

	//Points the panels at the section saved for this project, where they were built before it was open and so were given the fallback.
	void SyncSection()
	{
		if ( Project.Current is null ) return;

		var section = RestoreSection();
		_section = section;

		foreach ( var panel in _panels )
			panel.SetSection( section );

		StateHasChanged();
	}

	public void ShowAddPanelMenu()
	{
		if ( !ShowPanelControls ) return;

		var menu = new ContextMenu( Host );
		menu.AddOption( "Tree View", "account_tree", () => AddTreePanel() );
		menu.AddOption( "Icon Grid", "grid_view", () => AddIconGridPanel() );
		menu.OpenAtCursor( this );
	}

	public void ShowSettingsPopup()
	{
		if ( _settingsPopup is { IsValid: true } )
		{
			_settingsPopup.Delete();
			return;
		}

		_settingsPopup = BrowserSettingsPopup.Open( settingsButton );
	}

	public void ToggleLayoutMode()
	{
		if ( !ShowPanelControls || _panels.Count <= 1 ) return;

		if ( _isTabMode )
			SwitchToNormalMode();
		else
			SwitchToTabMode();

		SaveBrowserState();
	}

	void SwitchToTabMode()
	{
		_isTabMode = true;
		if ( tabsHost.IsValid() )
			tabsHost.SetClass( "hidden", false );

		foreach ( var splitter in _splitters )
			splitter.SetClass( "hidden", true );

		for ( var i = 0; i < _panels.Count; i++ )
		{
			ClearFixedWidth( _panels[i].Root );
			SetPanelVisible( _panels[i].Root, i == _activeTabIndex );
		}

		RebuildTabBar();
		UpdatePanelButtons();
		SyncLayoutToggle();
	}

	void SwitchToNormalMode()
	{
		_isTabMode = false;
		if ( tabsHost.IsValid() )
			tabsHost.SetClass( "hidden", true );

		foreach ( var splitter in _splitters )
			splitter.SetClass( "hidden", false );

		foreach ( var panel in _panels )
			SetPanelVisible( panel.Root, true );

		UpdatePanelButtons();
		SyncLayoutToggle();
	}

	void RebuildTabBar()
	{
		if ( !tabsHost.IsValid() ) return;

		tabsHost.DeleteChildren( true );

		for ( var i = 0; i < _panels.Count; i++ )
		{
			var index = i;
			var panel = _panels[i];
			var tab = new BrowserTab
			{
				Icon = GetPanelTabIcon( panel ),
				Title = GetPanelTabName( panel ),
				Active = index == _activeTabIndex,
				ShowClose = _panels.Count > 1,
				OnSelected = () => SelectTab( index ),
				OnCloseRequested = () => RemovePanel( panel )
			};
			tabsHost.AddChild( tab );
		}
	}

	void SelectTab( int index )
	{
		if ( index < 0 || index >= _panels.Count ) return;

		_activeTabIndex = index;

		for ( var i = 0; i < _panels.Count; i++ )
			SetPanelVisible( _panels[i].Root, i == _activeTabIndex );

		RebuildTabBar();
	}

	static void SetPanelVisible( Panel panel, bool visible ) => panel.SetClass( "hidden", !visible );

	//Drops the width a splitter wrote, so the panel goes back to sharing the row.
	static void ClearFixedWidth( Panel panel )
	{
		panel.Style.Width = Length.Auto;
		panel.SetClass( "fixed", false );
	}

	void PinDefaultTreeWidth()
	{
		if ( _panels.Count == 0 ) return;

		var root = _panels[0].Root;
		root.Style.Width = Length.Pixels( DefaultTreeWidth );
		root.SetClass( "fixed", true );
	}

	void RestorePanelWidths( List<float> widths )
	{
		if ( widths is null || widths.Count != _panels.Count ) return;

		for ( var i = 0; i < _panels.Count - 1; i++ )
		{
			if ( widths[i] <= 0 ) continue;

			var root = _panels[i].Root;
			root.Style.Width = Length.Pixels( widths[i] );
			root.SetClass( "fixed", true );
		}
	}

	static string GetPanelTypeName( IBrowserPanel panel ) => panel switch
	{
		AssetBrowserPanel => "Tree",
		IconGridPanel => "IconGrid",
		_ => "Unknown"
	};

	static string GetPanelTabName( IBrowserPanel panel ) => panel switch
	{
		AssetBrowserPanel => "Tree",
		IconGridPanel => "Grid",
		_ => "Panel"
	};

	static string GetPanelTabIcon( IBrowserPanel panel ) => panel switch
	{
		AssetBrowserPanel => "account_tree",
		IconGridPanel => "grid_view",
		_ => "tab"
	};

	public IEnumerable<IBrowserPanel> Panels => _panels;
}
