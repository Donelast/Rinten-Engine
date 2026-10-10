namespace Editor.Preferences;

//The shortcuts of one group, under a heading that folds them away.
sealed class KeybindGroupPanel : Panel
{
	readonly Rinten.UI.Label chevron;
	readonly Rinten.UI.Label title;
	readonly Panel body;

	string cookie;
	bool expanded = true;

	public KeybindGroupPanel()
	{
		AddClass( "keybind-group" );

		var header = AddChild<Panel>( "header" );
		header.AddClass( "pressable" );

		chevron = header.AddChild<Rinten.UI.Label>( "chevron" );
		chevron.AddClass( "material-icon" );

		title = header.AddChild<Rinten.UI.Label>( "title" );

		body = AddChild<Panel>( "body" );

		header.AddEventListener( "onclick", _ => Toggle() );
	}

	public void Setup( ShortcutGroup group )
	{
		title.Text = group.Title;

		//Folded or not is remembered per group, so the one somebody works in stays open.
		cookie = $"keybinds.category.{group.Title}";
		expanded = EditorCookie.Get( cookie, true );

		body.DeleteChildren( true );

		var row = 0;

		foreach ( var binding in group.Bindings )
			body.AddChild( new KeybindRowPanel( binding, row++ ) );

		Sync();
	}

	/// <summary>Every row reads its shortcut again - something else may have taken its keys.</summary>
	public void Refresh()
	{
		foreach ( var row in body.Children.OfType<KeybindRowPanel>() )
			row.Refresh();
	}

	void Toggle()
	{
		expanded = !expanded;

		if ( !string.IsNullOrEmpty( cookie ) )
			EditorCookie.Set( cookie, expanded );

		Sync();
	}

	void Sync()
	{
		chevron.Text = expanded ? "expand_more" : "chevron_right";
		body.SetClass( "hidden", !expanded );
	}
}
