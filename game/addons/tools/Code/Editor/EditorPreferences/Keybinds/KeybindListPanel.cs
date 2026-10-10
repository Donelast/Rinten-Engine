namespace Editor.Preferences;

/// <summary>Every editor shortcut there is, grouped, searchable, and editable in place.</summary>
[StyleSheet( "KeybindListPanel.cs.scss" )]
public sealed class KeybindListPanel : Panel
{
	readonly ShortcutCatalog catalog = new();

	string query = "";
	bool stale;

	public KeybindListPanel()
	{
		AddClass( "keybind-list" );

		EditorEvent.Register( this );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		EditorEvent.Unregister( this );
	}

	/// <summary>Only the shortcuts whose name or keys contain this.</summary>
	public string Query
	{
		get => query;
		set
		{
			if ( query == value ) return;

			query = value ?? "";
			Rebuild();
		}
	}

	/// <summary>Reads the shortcuts again and lists them from scratch.</summary>
	public void Rebuild()
	{
		DeleteChildren( true );

		foreach ( var group in catalog.Groups( query ) )
		{
			var panel = AddChild<KeybindGroupPanel>();
			panel.Setup( group );
		}

		stale = false;
	}

	//Noted rather than acted on: the same event is what rebuilds the shortcut entries themselves, and nothing says which handler runs first. Next frame they are certainly rebuilt.
	/// <summary>A binding changed somewhere, so what every row shows may have.</summary>
	[Event( "keybinds.update" )]
	public void OnKeybindsChanged() => stale = true;

	public override void Tick()
	{
		base.Tick();

		if ( !stale ) return;

		stale = false;

		foreach ( var group in Children.OfType<KeybindGroupPanel>() )
			group.Refresh();
	}

	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );

		if ( firstTime ) Rebuild();
	}
}
