namespace Editor.Inspectors;

using Editor;
using Rinten.UI;

[StyleSheet( "ComponentSheetHeaderPanel.cs.scss" )]
public class ComponentSheetHeaderPanel : Panel
{
	public SerializedObject TargetObject { get; }

	public string Title
	{
		get;
		set
		{
			field = value;
			if ( titleLabel.IsValid() ) titleLabel.Text = field;
		}
	}

	public bool Expanded { get; set; } = true;

	ComponentSheetPanel sheet;
	Rinten.UI.Label chevronLabel;
	Rinten.UI.Label iconLabel;
	BoolControl enabledSwitch;
	ToolbarButton menuButton;
	Rinten.UI.Label titleLabel;
	Menu headerMenu;
	RealTimeSince sinceMenuClosed = 1;

	Vector2? pressAt;
	bool pendingClick;
	bool dragStarted;

	const float DragThreshold = 20f;

	public ComponentSheetHeaderPanel( SerializedObject target, ComponentSheetPanel parent )
	{
		TargetObject = target;
		sheet = parent;
		Expanded = parent.Expanded;
		AddClass( "component-sheet-header inspector-card-header" );

		chevronLabel = AddChild<Rinten.UI.Label>( "chevron" );
		chevronLabel.AddClass( "material-icon" );
		chevronLabel.Text = GetChevronIcon();

		iconLabel = AddChild<Rinten.UI.Label>( "icon" );
		iconLabel.AddClass( "material-icon" );
		iconLabel.Text = GetComponentIcon();

		var enabled = target.GetProperty( "Enabled" );
		if ( enabled is not null )
		{
			enabledSwitch = AddChild<BoolControl>( "enabled" );
			enabledSwitch.Property = enabled;
		}

		titleLabel = AddChild<Rinten.UI.Label>( "title" );
		titleLabel.Text = target.TypeTitle.AutoLocalize();

		menuButton = AddChild<ToolbarButton>( "menu-button" );
		menuButton.Icon = "more_horiz";
	}

	public override void Tick()
	{
		base.Tick();
		SetClass( "collapsed", !Expanded );
		SetClass( "dragging", dragStarted && EditorDrag.Active );
		if ( chevronLabel.IsValid() )
			chevronLabel.Text = GetChevronIcon();
		if ( iconLabel.IsValid() )
			iconLabel.Text = GetComponentIcon();

		//The button can come up anywhere once the drag is going, so the header may never hear its own mouse-up.
		if ( dragStarted && !EditorDrag.Active )
			dragStarted = false;

		if ( !pendingClick || dragStarted || pressAt is null ) return;
		if ( (Application.MouseButtons & MouseButtons.Left) == 0 ) return;
		if ( TargetObject.IsMultipleTargets ) return;

		if ( pressAt.Value.Distance( Application.CursorPosition ) > DragThreshold )
			StartHeaderDrag();
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		var target = e.Target as Panel;

		if ( e.MouseButton == MouseButtons.Left && EyeDropperTool.TargetProperty is not null )
		{
			if ( target?.IsAncestor( menuButton ) != true && target?.IsAncestor( enabledSwitch ) != true )
			{
				var component = GetComponent();
				if ( component.IsValid() )
				{
					EyeDropperTool.Select( component );
					e.StopPropagation();
					return;
				}
			}
		}

		if ( e.MouseButton == MouseButtons.Right )
		{
			if ( target?.IsAncestor( enabledSwitch ) == true )
				return;

			ShowHeaderMenu( atCursor: true );
			e.StopPropagation();
			return;
		}

		if ( e.MouseButton != MouseButtons.Left )
			return;

		if ( target?.IsAncestor( menuButton ) == true )
		{
			ToggleHeaderMenu();
			e.StopPropagation();
			return;
		}

		if ( target?.IsAncestor( enabledSwitch ) == true )
			return;

		pendingClick = true;
		dragStarted = false;
		pressAt = Application.CursorPosition;
		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		if ( dragStarted )
		{
			dragStarted = false;
			pendingClick = false;
			pressAt = null;
			return;
		}

		if ( !pendingClick ) return;

		pendingClick = false;
		pressAt = null;

		sheet.SetExpanded( !sheet.Expanded );
		e.StopPropagation();
	}

	//Carried from the header itself, as a tree row is: the component list's sheets and the hierarchy's rows are panels, and they hear a panel's drag.
	//It used to be started from the scene tree's widget, which put the drag on whichever window the tree was in and nothing in the inspector took it.
	void StartHeaderDrag()
	{
		if ( !GetComponent().IsValid() ) return;

		pendingClick = false;
		pressAt = null;
		dragStarted = true;

		var data = new DragData
		{
			Object = TargetObject,
			Text = TargetObject.TypeTitle.AutoLocalize()
		};

		EditorDrag.Begin( this, data, null );

		if ( !EditorDrag.Active )
			dragStarted = false;
	}

	string GetChevronIcon() => Expanded ? "expand_more" : "chevron_right";

	//The button that opens the menu closes it again. A press anywhere takes the open menus away before the panel hears it, so by the time
	//this runs the menu it would be closing has usually gone already - hence the moment it went, rather than only whether it is still there.
	void ToggleHeaderMenu()
	{
		if ( headerMenu is { IsValid: true } || sinceMenuClosed < 0.1f )
		{
			CloseHeaderMenu();
			return;
		}

		ShowHeaderMenu();
	}

	void CloseHeaderMenu()
	{
		headerMenu?.Close();
		headerMenu = null;
		sinceMenuClosed = 0;
	}

	void ShowHeaderMenu( bool atCursor = false )
	{
		CloseHeaderMenu();

		Menu menu;

		if ( sheet.IsGameTransformSheet )
		{
			menu = sheet.BuildCustomHeaderMenu();
			if ( menu is null ) return;
		}
		else
		{
			var component = GetComponent();
			if ( !component.IsValid() ) return;

			menu = new Menu();
			ComponentSheetHeaderMenu.Fill( menu, sheet, component, TargetObject );
		}

		menu.AboutToHide += () =>
		{
			if ( headerMenu != menu ) return;

			headerMenu = null;
			sinceMenuClosed = 0;
		};

		headerMenu = menu;

		//Anchored, so the menu goes on the overlay of the window the header is in rather than on whichever window happens to be last in the list.
		if ( atCursor )
			menu.OpenAtCursor( this );
		else
			menu.OpenBelowRight( menuButton, 4f );
	}

	static string DefaultComponentIcon()
	{
		var td = EditorTypeLibrary.GetType( typeof( Component ) );
		return td?.Icon ?? "extension";
	}

	string GetComponentIcon()
	{
		var component = GetComponent();
		if ( !component.IsValid() ) return DefaultComponentIcon();
		var td = EditorTypeLibrary.GetType( component.GetType() );
		return td?.Icon ?? DefaultComponentIcon();
	}

	public Component GetComponent() => TargetObject?.Targets?.FirstOrDefault() as Component;
}
