using Rinten.UI;

namespace Editor;

/// <summary>Tool mode picker on every scene view bar; tools are listed when the menu opens, after compile</summary>
[StyleSheet( "ToolModePickerPanel.cs.scss" )]
public sealed class ToolModePickerPanel : EditorPanel
{
	private readonly IconPanel icon;
	private readonly Rinten.UI.Label title;

	public ToolModePickerPanel()
	{
		AddClass( "tool-mode-picker" );

		Tooltip = "Tool mode";

		icon = AddChild<IconPanel>( "icon" );
		title = AddChild<Rinten.UI.Label>( "title" );

		var chevron = AddChild<IconPanel>( "chevron" );
		chevron.Text = "arrow_drop_down";
	}

	//The name of the mode, read once a frame; the panel takes its turn only when somebody picks another one - here, from the strip, or by a key.
	protected override int BuildHash() => HashCode.Combine( EditorToolManager.CurrentModeName );

	protected override void OnBuildHashChanged() => Show();

	private static IEnumerable<(TypeDescription Type, EditorToolAttribute Attribute)> Modes()
		=> EditorTypeLibrary.GetTypesWithAttribute<EditorToolAttribute>();

	//Says which mode is on.
	private void Show()
	{
		var current = EditorToolManager.CurrentModeName;
		var type = Modes().FirstOrDefault( x => x.Type.Name == current ).Type;

		ShowIcon( type?.Icon ?? "handyman" );
		title.Text = type?.Title ?? current;
	}

	//A letter of the icon font
	private void ShowIcon( string name )
	{
		icon.Text = name ?? string.Empty;
	}

	//The menu this dropped down, while it is open.
	private ContextMenu menu;

	//When the menu last went, by RealTime.Now.
	private float closedAt = float.MinValue;

	//Whether the press now down is the one that took the menu away.
	private bool pressClosed;

	//A press that just closed the menu (outside-click) must not reopen it on the click
	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		pressClosed = menu is null && RealTime.Now - closedAt < 0.1f;
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		e.StopPropagation();

		if ( pressClosed )
		{
			pressClosed = false;
			return;
		}

		//Still open: nothing closed it on the way down. Clicking again closes it.
		if ( menu is { IsValid: true } )
		{
			menu.Close();
			return;
		}

		Open();
	}

	private void Open()
	{
		var menu = new ContextMenu( HostWidget );
		var offered = 0;

		var groups = Modes()
			.GroupBy( x => string.IsNullOrEmpty( x.Type.Group ) ? "Tools" : x.Type.Group )
			.OrderByDescending( x => x.Key );

		foreach ( var group in groups )
		{
			//Nothing in this group anybody may pick.
			if ( !group.Any( x => !x.Attribute.Hidden ) ) continue;

			menu.AddHeading( group.Key );

			foreach ( var (type, attribute) in group.OrderBy( x => x.Type.Order ) )
			{
				if ( attribute.Hidden ) continue;

				var name = type.Name;

				var option = new Option
				{
					Text = type.Title,
					ShortcutName = attribute.Shortcut,
					Icon = type.Icon,
					Triggered = () => EditorToolManager.CurrentModeName = name
				};

				menu.AddOption( option );
				offered++;
			}
		}

		//A menu with nothing in it opens as nothing, which reads as a button that does not work. Say which it is.
		if ( offered == 0 ) menu.AddHeading( "No tools" );

		//However it goes - a pick, Escape, a press elsewhere or on here.
		menu.AboutToHide += () =>
		{
			if ( this.menu != menu ) return;

			this.menu = null;
			closedAt = RealTime.Now;

			SetClass( "open", false );
		};

		this.menu = menu;
		SetClass( "open", true );

		menu.OpenAt( ScreenRect.BottomLeft, false );
	}
}
