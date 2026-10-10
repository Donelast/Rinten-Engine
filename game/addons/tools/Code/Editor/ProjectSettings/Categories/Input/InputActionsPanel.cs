using Editor.Inspectors;
using Editor.ProjectSettingsUI;

namespace Editor.ProjectSettingPages.Categories;

[StyleSheet( "InputActionsPanel.cs.scss" )]
sealed class InputActionsPanel : Panel
{
	public InputCategory Category { get; set; }

	readonly Panel list;
	readonly Panel footer;

	public InputActionsPanel()
	{
		AddClass( "input-actions" );
		list = AddChild<Panel>( "list" );
		footer = AddChild<Panel>( "footer" );
		footer.AddClass( "input-footer" );
	}

	public void Rebuild()
	{
		list.DeleteChildren();
		footer.DeleteChildren();

		string lastGroup = null;
		foreach ( var group in Category.InputSettings.Actions.GroupBy( x => x.GroupName ) )
		{
			var groupPanel = list.AddChild<InputGroupPanel>();
			groupPanel.Setup( Category, group.Key, group );
			groupPanel.StateCookieName = $"inputpage.category.{group.Key}";
			lastGroup = group.Key;
		}

		var entry = footer.AddChild<TextField>( "entry" );
		entry.Placeholder = "Add New Action...";

		var addButton = footer.AddChild<Panel>( "add" );
		addButton.AddClass( "btn good" );
		var addLabel = addButton.AddChild<Rinten.UI.Label>();
		addLabel.Text = "Add";

		var maxAmount = 64;
		var canAdd = Category.InputSettings.Actions.Count < maxAmount;
		addButton.SetClass( "disabled", !canAdd );
		addButton.Tooltip = canAdd ? null : "We only support a limit of 64 input actions to keep things speedy.";

		void AddNew()
		{
			if ( !canAdd ) return;
			var name = string.IsNullOrEmpty( entry.Value ) ? $"Action {Category.InputSettings.Actions.Count}" : entry.Value;
			Category.AddAction( new InputAction { Name = name, GroupName = lastGroup ?? "Other" } );
			entry.Value = "";
		}

		entry.Submitted += _ => AddNew();
		addButton.AddEventListener( "onclick", _ => AddNew() );

		var clearButton = footer.AddChild<Panel>( "clear" );
		clearButton.AddClass( "btn" );
		var clearLabel = clearButton.AddChild<Rinten.UI.Label>();
		clearLabel.Text = "Clear";
		clearButton.AddEventListener( "onclick", _ =>
		{
			Dialog.AskConfirm( () =>
			{
				Category.InputSettings.Actions = new();
				AddNew();
			}, "Are you sure you want to clear all input actions?", "Clear Input Actions", "Yes", "Cancel" );
		} );

		var resetButton = footer.AddChild<Panel>( "reset" );
		resetButton.AddClass( "btn" );
		var resetLabel = resetButton.AddChild<Rinten.UI.Label>();
		resetLabel.Text = "Reset to Default";
		resetButton.AddEventListener( "onclick", _ =>
		{
			Category.InputSettings.Actions = new();
			Category.CreateDefaultActions();
			Rebuild();
			Category.StateHasChanged();
		} );
	}
}

[StyleSheet( "InputActionsPanel.cs.scss" )]
sealed class InputGroupPanel : Panel
{
	public string StateCookieName
	{
		set
		{
			if ( string.IsNullOrEmpty( value ) ) return;
			expanded = EditorCookie.Get( value, true );
			SyncExpanded();
			cookieName = value;
		}
	}

	string cookieName;
	bool expanded = true;
	InputCategory category;
	readonly Panel header;
	readonly Panel body;

	public InputGroupPanel()
	{
		AddClass( "input-group" );
		header = AddChild<Panel>( "header" );
		header.AddClass( "group-header" );
		body = AddChild<Panel>( "body" );
	}

	public void Setup( InputCategory category, string groupName, IEnumerable<InputAction> actions )
	{
		this.category = category;
		header.DeleteChildren();

		var chevron = header.AddChild<Rinten.UI.Label>( "chevron" );
		chevron.AddClass( "material-icon" );
		chevron.Text = expanded ? "expand_more" : "chevron_right";

		var title = header.AddChild<Rinten.UI.Label>( "title" );
		title.Text = groupName;

		body.DeleteChildren();
		foreach ( var action in actions )
		{
			var row = body.AddChild<InputActionRowPanel>();
			row.Setup( category, action );
		}

		header.AddEventListener( "onclick", _ =>
		{
			expanded = !expanded;
			if ( !string.IsNullOrEmpty( cookieName ) )
				EditorCookie.Set( cookieName, expanded );
			SyncExpanded();
		} );

		SyncExpanded();
	}

	void SyncExpanded()
	{
		body.SetClass( "hidden", !expanded );
		var chevron = header.Children.FirstOrDefault( c => c.ElementName == "chevron" ) as Rinten.UI.Label;
		if ( chevron is not null )
			chevron.Text = expanded ? "expand_more" : "chevron_right";
	}
}

[StyleSheet( "InputActionsPanel.cs.scss" )]
sealed class InputActionRowPanel : Panel
{
	InputCategory category;
	InputAction action;
	int rowIndex;

	public void Setup( InputCategory category, InputAction action )
	{
		this.category = category;
		this.action = action;
		AddClass( "input-action-row" );
		Rebuild();
	}

	public override void Tick()
	{
		base.Tick();
		//GetChildIndex rather than Children.ToList().IndexOf: same answer, and it does not build a list of every sibling to find one of them.
		rowIndex = Parent?.GetChildIndex( this ) ?? -1;
		SetClass( "alt", rowIndex % 2 == 0 );
	}

	void Rebuild()
	{
		DeleteChildren();

		var name = string.IsNullOrEmpty( action.Title ) ? action.Name : action.Title;
		var nameLabel = AddChild<Rinten.UI.Label>( "name" );
		nameLabel.Text = name;

		var bindings = AddChild<Panel>( "bindings" );

		if ( !string.IsNullOrEmpty( action.KeyboardCode ) )
			AddBinding( bindings, action.KeyboardCode.ToUpperInvariant(), "keyboard" );

		if ( action.GamepadCode != GamepadCode.None )
			AddBinding( bindings, GetFriendlyGamepadCode( action.GamepadCode ), "sports_esports" );
	}

	static void AddBinding( Panel parent, string text, string icon )
	{
		var row = parent.AddChild<Panel>( "binding" );
		var textLabel = row.AddChild<Rinten.UI.Label>( "text" );
		textLabel.Text = text;
		var iconLabel = row.AddChild<Rinten.UI.Label>( "icon" );
		iconLabel.AddClass( "material-icon" );
		iconLabel.Text = icon;
	}

	static string GetFriendlyGamepadCode( GamepadCode value )
	{
		return DisplayInfo.ForEnumValues<GamepadCode>()
			.FirstOrDefault( x => x.value.Equals( value ) )
			.info.Name;
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );
		ShowEditor();
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );
		var menu = new Menu();
		menu.AddOption( "Edit", "edit", ShowEditor );
		menu.AddOption( "Duplicate", "file_copy", () => category.AddAction( new InputAction( action ) ) );
		menu.AddOption( "Delete", "delete", () => category.RemoveAction( action ) );
		menu.OpenAtCursor();
	}

	void ShowEditor()
	{
		var dialog = new Dialog( null );
		dialog.DeleteOnClose = true;
		dialog.Layout = Layout.Column();
		dialog.Layout.Margin = 16;
		dialog.Window.Size = new( 400, 270 );

		var sheet = new PanelControlSheetHost( dialog );
		sheet.SetObject( action.GetSerialized() );
		dialog.Layout.Add( sheet );
		dialog.Layout.AddStretchCell( 1 );

		var row = dialog.Layout.AddRow();
		row.Spacing = 8;
		row.AddStretchCell();
		row.Add( new Button( "Delete", "delete" ) { Clicked = () => { category.RemoveAction( action ); dialog.Close(); } } );
		row.Add( new Button.Primary( "Done", "done" ) { MouseClick = () => { category.actionsPanel?.Rebuild(); dialog.Close(); } } );

		dialog.Show();
	}
}
