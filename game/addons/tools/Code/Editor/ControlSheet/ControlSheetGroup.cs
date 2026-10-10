using Rinten.UI;

using Rinten.UI;

namespace Editor;

/// <summary>Represents a group of related controls within a control sheet, optionally with a collapsible header and property name display.</summary>
public class ControlSheetGroup : Widget
{
	internal List<SerializedProperty> properties;
	List<ControlSheetRow> rows = new();
	string groupCookie;
	Widget Body;
	GroupHeader headerWidget;
	Panel connectorVertical;
	Panel connectorCorner;

	bool hasHeader;

	public bool ShowPropertyNameInTooltip = false;

	public ControlSheetGroup( string groupName, SerializedProperty[] props, bool showPropertyNameInTooltip = false )
	{
		if ( !string.IsNullOrWhiteSpace( groupName ) )
		{
			groupCookie = $"controlsheetgroup.{groupName}";
		}

		ShowPropertyNameInTooltip = showPropertyNameInTooltip;
		VerticalSizeMode = SizeMode.CanGrow;
		HorizontalSizeMode = SizeMode.Flexible;

		Layout = Layout.Column();
		Layout.Margin = new Margin( 0, 0, 1, 0 );

		bool closed = props.SelectMany( x => x.GetAttributes<GroupAttribute>() ).Any( x => x.StartFolded );
		properties = props.ToList();

		if ( groupCookie is not null )
		{
			closed = EditorCookie.Get( groupCookie, closed );
		}

		visibilityDebounce = 10;

		if ( !string.IsNullOrWhiteSpace( groupName ) )
		{
			headerWidget = new GroupHeader( this );

			Layout.Add( headerWidget );

			headerWidget.Title = groupName;

			var toggleGroup = props.FirstOrDefault( x => x.HasAttribute<ToggleGroupAttribute>() && x.Name == groupName );
			if ( toggleGroup is not null )
			{
				toggleGroup.TryGetAttribute<ToggleGroupAttribute>( out var toggleAttr );
				if ( toggleGroup is not null )
				{
					properties.Remove( toggleGroup );

					var enabler = ControlWidget.Create( toggleGroup );

					headerWidget.Title = toggleAttr.Label ?? groupName;
					headerWidget.ToolTip = toggleGroup.Description;
					headerWidget.AddToggle( toggleGroup, enabler );

					if ( !toggleGroup.As.Bool ) closed = true;
				}
			}
		}

		hasHeader = headerWidget.IsValid();

		Body = new Widget();
		Body.Hidden = true;
		Body.VerticalSizeMode = SizeMode.CanGrow;
		Body.HorizontalSizeMode = SizeMode.Flexible;

		Body.Layout = Layout.Column();
		Body.Layout.Margin = new Margin( hasHeader ? 4 : 0, 0, 0, 0 );
		Body.Layout.Spacing = 0;

		Layout.Add( Body );

		if ( hasHeader )
		{
			firstOpen = () => BuildContents();
			headerWidget.OnToggled += SetVisible;

			if ( !closed )
				OpenInitially();
		}
		else
		{
			BuildContents();
			Body.Hidden = false;
		}

		//If none of our properties are visible, hide the whole group
		if ( !properties.Any( x => x.ShouldShow() ) )
		{
			Visible = false;
		}
	}

	Action firstOpen;

	void OpenInitially()
	{
		firstOpen?.Invoke();
		firstOpen = null;

		Body.Hidden = false;
		headerWidget?.SetExpanded( true );
	}

	void SetVisible( bool visible )
	{
		if ( !IsValid ) return;

		using var x = SuspendUpdates.For( Parent );

		firstOpen?.Invoke();
		firstOpen = null;

		Body.Hidden = !visible;

		UpdateGeometry();
		Parent?.UpdateGeometry();

		if ( groupCookie is not null )
		{
			EditorCookie.Set( groupCookie, !visible );
		}
	}

	private void BuildContents()
	{
		if ( !IsValid || Body is not { IsValid: true } || Body.Layout is null )
			return;

		foreach ( var prop in properties )
		{
			if ( prop.HasAttribute<HideAttribute>() )
				continue;

			var row = ControlSheetRow.Create( prop, ShowPropertyNameInTooltip );
			if ( row.IsValid() )
			{
				rows.Add( row );
				Body.Layout.Add( row );
				row.UpdateVisibility();
			}
		}

		var widest = 0.0f;
		foreach ( var row in rows )
			widest = MathF.Max( widest, row.LabelWantedWidth );

		foreach ( var row in rows )
			row.FitLabelColumn( widest );
	}

	RealTimeSince visibilityDebounce = 0;


	[EditorEvent.Frame]
	public void UpdateVisibility()
	{
		if ( !IsValid )
			return;

		if ( Parent is null )
			return;

		if ( visibilityDebounce < 0.2f )
			return;

		visibilityDebounce = Random.Shared.Float( 0, 0.1f );

		Visible = properties?.Any( x => x.ShouldShow() ) ?? false;

		if ( !Visible )
			return;

		bool bChanged = false;

		foreach ( var r in rows )
		{
			bChanged = r.UpdateVisibility() || bChanged;
		}
	}

	public override void SyncSurfaceContent()
	{
		if ( ContentPanel is null ) return;

		var show = hasHeader && !Body.Hidden;

		if ( connectorVertical is null )
		{
			connectorVertical = new Panel();
			connectorVertical.AddClass( "group-connector" );
			connectorVertical.AddClass( "vertical" );
			ContentPanel.AddChild( connectorVertical );
		}

		if ( connectorCorner is null )
		{
			connectorCorner = new Panel();
			connectorCorner.AddClass( "group-connector" );
			connectorCorner.AddClass( "corner" );
			ContentPanel.AddChild( connectorCorner );
		}

		connectorVertical.SetClass( "hidden", !show );
		connectorCorner.SetClass( "hidden", !show );
	}

	protected override void OnPaint()
	{
		if ( !Paint.Snapshot ) return;

		if ( hasHeader && !Body.Hidden )
		{
			var r = LocalRect.Shrink( 8, 4, 0, 0 );
			r.Width = 3;

			Paint.SetBrushAndPen( Theme.WindowBackground );
			Paint.DrawRect( r, 5 );

			r = LocalRect.Shrink( 10, 0, 0, 0 );
			r.Top = r.Bottom - 3;
			r.Width = 8;

			Paint.SetBrushAndPen( Theme.WindowBackground );
			Paint.DrawRect( r, 5 );
		}
	}
}


class GroupHeader : Widget
{
	ControlSheetGroup groupControl;
	Layout toggleLayout;
	ControlWidget toggleControl;
	SerializedProperty toggleProperty;
	Label titleLabel;
	Panel chevronPlate;
	Panel prefabBar;
	bool state = false;

	public GroupHeader( ControlSheetGroup group ) : base( null )
	{
		FixedHeight = Theme.RowHeight;
		VerticalSizeMode = SizeMode.CanGrow;
		HorizontalSizeMode = SizeMode.Flexible;
		Cursor = CursorShape.Finger;
		Layout = Layout.Row();
		Layout.Spacing = 5;
		Layout.Margin = new Margin( 16, 0, 0, 0 );
		Layout.AddSpacingCell( 10 );

		groupControl = group;
		toggleLayout = Layout.AddColumn();
		toggleLayout.Alignment = TextFlag.CenterVertically;

		titleLabel = Layout.Add( new Label( "" ) );
		titleLabel.Alignment = TextFlag.LeftCenter;

		Layout.AddStretchCell();
	}

	public string Title
	{
		get => titleLabel?.Text ?? "";
		set
		{
			if ( titleLabel.IsValid() )
				titleLabel.Text = value;
		}
	}


	internal void AddToggle( SerializedProperty toggleProp, ControlWidget controlWidget )
	{
		controlWidget.FixedHeight = 17;
		controlWidget.FixedWidth = 17;
		controlWidget.Tint = Theme.Green;

		toggleLayout.Add( controlWidget );
		toggleControl = controlWidget;
		toggleProperty = toggleProp;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( e.Button == MouseButtons.Left )
		{
			Toggle();
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		base.OnContextMenu( e );

		var menu = new ContextMenu( this );

		{
			menu.AddOption( $"Copy {Title} Properties", "content_copy", () =>
			{
				ClipboardTools.CopyProperties( Title, groupControl.properties );
			} );
			var pasteOption = menu.AddOption( $"Paste {Title} Properties", "content_paste", () =>
			{
				ClipboardTools.PasteProperties( Title, groupControl.properties );
			} );
			pasteOption.Enabled = ClipboardTools.CanPasteProperties( Title, groupControl.properties );
			menu.AddOption( "Reset all to Default", "restart_alt", () =>
			{
				foreach ( var prop in groupControl.properties )
				{
					prop.SetValue( prop.GetDefault() );
				}
			} );

			menu.AddSeparator();

			var editedComponents = groupControl.properties.First().Parent?.Targets?.OfType<Component>().Where( x => x.IsValid() ) ?? Enumerable.Empty<Component>();

			var arePropertiesModified = groupControl.properties.Any( prop => EditorUtility.Prefabs.IsPropertyOverridden( prop ) );

			var prefabName = EditorUtility.Prefabs.GetOuterMostPrefabName( editedComponents.First() );

			var revertAllActionName = $"Revert all {Title} instance changes";

			menu.AddOption( revertAllActionName, "history", () =>
			{
				var session = SceneEditorSession.Resolve( editedComponents.FirstOrDefault() );
				using ( session.UndoScope( revertAllActionName ).WithComponentChanges( editedComponents ).Push() )
				{
					groupControl.properties.ForEach( prop => EditorUtility.Prefabs.RevertPropertyChange( prop ) );
				}
			} ).Enabled = arePropertiesModified;

			var applyAllActionName = $"Apply all {Title} instance changes to prefab \"{prefabName}\"";

			menu.AddOption( applyAllActionName, "update", () =>
			{
				var session = SceneEditorSession.Resolve( editedComponents.FirstOrDefault() );
				using ( session.UndoScope( applyAllActionName ).WithComponentChanges( editedComponents ).Push() )
				{
					groupControl.properties.ForEach( prop => EditorUtility.Prefabs.ApplyPropertyChange( prop ) );
				}
			} ).Enabled = arePropertiesModified;

			//check toggle group property
			if ( toggleProperty != null )
			{
				menu.AddSeparator();

				var isToggleGroupPropModified = EditorUtility.Prefabs.IsPropertyOverridden( toggleProperty );

				var revertToggleActionName = $"Revert Toggle State instance change";

				menu.AddOption( revertToggleActionName, "history", () =>
				{
					var session = SceneEditorSession.Resolve( editedComponents.FirstOrDefault() );
					using ( session.UndoScope( revertToggleActionName ).WithComponentChanges( editedComponents ).Push() )
					{
						EditorUtility.Prefabs.RevertPropertyChange( toggleProperty );
					}
				} ).Enabled = isToggleGroupPropModified;

				var applyToggleActionName = $"Apply Toggle State instance change to prefab \"{prefabName}\"";

				menu.AddOption( applyToggleActionName, "update", () =>
				{
					var session = SceneEditorSession.Resolve( editedComponents.FirstOrDefault() );
					using ( session.UndoScope( applyToggleActionName ).WithComponentChanges( editedComponents ).Push() )
					{
						EditorUtility.Prefabs.ApplyPropertyChange( toggleProperty );
					}

				} ).Enabled = isToggleGroupPropModified;
			}
		}

		if ( toggleProperty is not null && CodeEditor.CanOpenFile( toggleProperty.SourceFile ) )
		{
			menu.AddSeparator();
			menu.AddOption( "Jump to code", "code", action: () => CodeEditor.OpenFile( toggleProperty.SourceFile, toggleProperty.SourceLine ) );
		}

		menu.OpenAtCursor();
	}

	public override void SyncSurfaceContent()
	{
		if ( ContentPanel is null ) return;

		if ( chevronPlate is null )
		{
			chevronPlate = new Panel();
			chevronPlate.AddClass( "group-chevron" );
			ContentPanel.AddChild( chevronPlate );
		}

		if ( prefabBar is null )
		{
			prefabBar = new Panel();
			prefabBar.AddClass( "group-prefab-bar" );
			prefabBar.AddClass( "hidden" );
			ContentPanel.AddChild( prefabBar );
		}

		chevronPlate.SetClass( "expanded", state );
		chevronPlate.SetClass( "collapsed", !state );
		chevronPlate.SetClass( "hovered", IsUnderMouse );

		var showPrefab = toggleProperty is not null
			&& EditorUtility.Prefabs.IsPropertyOverridden( toggleProperty );
		prefabBar.SetClass( "hidden", !showPrefab );
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		e.Accepted = false;
	}

	public void Toggle()
	{
		state = !state;
		OnToggled?.Invoke( state );

		if ( CookieName is not null )
		{
			ProjectCookie.Set( CookieName, state );
		}
	}

	internal void SetExpanded( bool expanded )
	{
		state = expanded;
	}

	public Action<bool> OnToggled;

	string _cookieName;

	public string CookieName
	{
		get
		{
			return _cookieName;
		}

		set
		{
			_cookieName = value;

			var newState = ProjectCookie.Get<bool>( _cookieName, state );
			if ( newState == state ) return;

			Toggle();
		}
	}
}


