namespace Editor.Preferences;

//One page in the sidebar: its icon, its name, and whether it is the one on show.
sealed class PreferencesNavItemPanel : Panel
{
	public PreferencesPageDescriptor Page { get; }

	public PreferencesNavItemPanel( PreferencesPageDescriptor page, Action<PreferencesPageDescriptor> onPressed )
	{
		Page = page;

		AddClass( "nav-item" );
		AddClass( "pressable" );

		var icon = AddChild<Rinten.UI.Label>( "icon" );
		icon.AddClass( "material-icon" );
		icon.Text = page.Icon;

		var title = AddChild<Rinten.UI.Label>( "title" );
		title.Text = page.Title;

		if ( !string.IsNullOrEmpty( page.Description ) )
			Tooltip = page.Description;

		AddEventListener( "onclick", _ => onPressed?.Invoke( page ) );
	}

	public void SetSelected( bool selected ) => SetClass( "is-selected", selected );
}
