using Rinten.Internal;

namespace Editor.Inspectors;

/// <summary>Feature tabs for razor <see cref="ControlSheet"/> (panel path).</summary>
[StyleSheet( "ControlSheetFeatureTabs.cs.scss" )]
public class ControlSheetFeatureTabs : Panel
{
	readonly List<TabEntry> _tabs = new();
	Panel _tabBar;
	Panel _content;
	ToolbarButton _addButton;
	string _stateCookie;
	TabEntry _selected;
	Func<SerializedProperty, bool> _propertyFilter;

	public void SetStateCookie( string cookie )
	{
		_stateCookie = cookie;
		RestoreSelection();
	}

	public void SetPropertyFilter( Func<SerializedProperty, bool> filter ) => _propertyFilter = filter;

	public ControlSheetFeatureTabs()
	{
		AddClass( "feature-tabs" );

		//How the tab bar is laid out belongs in the sheet: it wraps, and a style on the panel itself would outrank what the sheet says.
		_tabBar = AddChild<Panel>( "tabbar" );

		_addButton = _tabBar.AddChild<ToolbarButton>( "add" );
		_addButton.Icon = "add";
		_addButton.ToolTip = "Add Feature";
		_addButton.OnPressed = OpenAddFeaturesMenu;
		_addButton.SetClass( "hidden", true );

		_content = AddChild<Panel>( "content" );
	}

	public void AddFeature( IControlSheet.Feature feature )
	{
		var title = string.IsNullOrEmpty( feature.Name ) ? "General" : feature.Name;
		var entry = new TabEntry { Feature = feature };

		var tab = _tabBar.AddChild<FeatureTabButton>();
		tab.AddClass( "feature-tab" );
		tab.Tint = Theme.GetTint( feature.Tint );
		tab.Icon = feature.Icon;
		tab.Text = title;
		tab.Tooltip = BuildTooltip( title, feature.Description );
		tab.OnPressed = () => Select( entry );

		_tabBar.SetChildIndex( tab, _tabBar.ChildrenCount - 2 );

		entry.Tab = tab;
		entry.Panel = _content.AddChild<FeaturePropertyPanel>();
		entry.Panel.PropertyFilter = _propertyFilter;
		entry.Panel.SetClass( "hidden", true );
		entry.NeedsBuild = true;

		if ( feature.EnabledProperty is not null && !feature.EnabledProperty.GetValue<bool>() )
		{
			tab.SetClass( "hidden", true );
			entry.Hidden = true;
		}

		_tabs.Add( entry );
		UpdateAddButton();

		if ( _selected is null && !entry.Hidden )
			Select( entry );

		BuildPendingPanels();
	}

	static string BuildTooltip( string title, string description )
	{
		if ( string.IsNullOrWhiteSpace( description ) ) return title;
		return $"<b>{title}</b><br/><br/>{description}";
	}

	void BuildPendingPanels()
	{
		foreach ( var entry in _tabs )
		{
			if ( !entry.NeedsBuild || entry.Panel is null || !entry.Panel.IsValid() ) continue;
			entry.Panel.Build( entry.Feature );
			entry.NeedsBuild = false;
		}
	}

	void Select( TabEntry entry )
	{
		if ( entry.Hidden ) return;

		if ( _selected is not null )
		{
			_selected.Tab?.SetClass( "selected", false );
			_selected.Panel.SetClass( "hidden", true );
		}

		_selected = entry;
		entry.Tab.SetClass( "selected", true );
		entry.Panel.SetClass( "hidden", false );
		SaveSelection();
	}

	void SelectFirstVisible()
	{
		var entry = _tabs.FirstOrDefault( x => !x.Hidden );
		if ( entry is not null )
			Select( entry );
		else
			_selected = null;
	}

	void SaveSelection()
	{
		if ( string.IsNullOrEmpty( _stateCookie ) || _selected is null ) return;
		EditorCookie.Set( $"featuretab.{_stateCookie}", _selected.Feature.Name );
	}

	void RestoreSelection()
	{
		if ( string.IsNullOrEmpty( _stateCookie ) ) return;
		var name = EditorCookie.Get<string>( $"featuretab.{_stateCookie}", null );
		if ( string.IsNullOrWhiteSpace( name ) ) return;
		var entry = _tabs.FirstOrDefault( x => x.Feature.Name == name && !x.Hidden );
		if ( entry is not null ) Select( entry );
	}

	void UpdateAddButton()
	{
		var canAdd = _tabs.Any( x =>
			x.Feature.EnabledProperty is not null && !x.Feature.EnabledProperty.GetValue<bool>() );

		_addButton.SetClass( "hidden", !canAdd );
	}

	void OpenAddFeaturesMenu()
	{
		var menu = new Menu();

		var added = 0;

		foreach ( var entry in _tabs )
		{
			var enabledProp = entry.Feature.EnabledProperty;
			if ( enabledProp is null || enabledProp.GetValue<bool>() ) continue;

			var title = enabledProp.DisplayName;
			var icon = null as string;

			if ( enabledProp.TryGetAttribute<FeatureEnabledAttribute>( out var attribute ) )
			{
				title = attribute.Title;
				icon = attribute.Icon;
			}

			menu.AddOption( title, icon, () => EnableFeature( entry ) );
			added++;
		}

		if ( added == 0 )
		{
			var none = menu.AddOption( "No features available" );
			none.Enabled = false;
		}

		menu.OpenAtCursor( this );
	}

	void EnableFeature( TabEntry entry )
	{
		var enabledProp = entry.Feature.EnabledProperty;
		if ( enabledProp is null || enabledProp.GetValue<bool>() ) return;

		enabledProp.SetValue( true );
		entry.Hidden = false;
		entry.Tab.SetClass( "hidden", false );
		UpdateAddButton();
		Select( entry );
	}

	public override void Tick()
	{
		base.Tick();

		BuildPendingPanels();

		foreach ( var entry in _tabs )
		{
			var enabledProp = entry.Feature.EnabledProperty;
			if ( enabledProp is null ) continue;

			var enabled = enabledProp.GetValue<bool>();
			if ( enabled == !entry.Hidden ) continue;

			entry.Hidden = !enabled;
			entry.Tab.SetClass( "hidden", !enabled );

			if ( !enabled && _selected == entry )
				SelectFirstVisible();
		}

		UpdateAddButton();
	}

	sealed class TabEntry
	{
		public IControlSheet.Feature Feature;
		public FeatureTabButton Tab;
		public FeaturePropertyPanel Panel;
		public bool Hidden;
		public bool NeedsBuild;
	}

	sealed class FeatureTabButton : Panel
	{
		public Action OnPressed;
		public Color Tint { get; set; } = Theme.TextControl;

		IconPanel _icon;
		Rinten.UI.Label _label;

		public string Text
		{
			get => _label?.Text;
			set
			{
				_label ??= AddChild<Rinten.UI.Label>( "label" );
				_label.Text = value;
			}
		}

		public string Icon
		{
			get => _icon?.Text;
			set
			{
				if ( string.IsNullOrWhiteSpace( value ) )
				{
					if ( _icon is not null )
						_icon.Delete();
					_icon = null;
					return;
				}

				_icon ??= AddChild<IconPanel>( "icon" );
				_icon.Text = value;
				_icon.Style.FontColor = Tint;
			}
		}

		protected override void OnClick( MousePanelEvent e )
		{
			base.OnClick( e );
			OnPressed?.Invoke();
		}
	}
}
