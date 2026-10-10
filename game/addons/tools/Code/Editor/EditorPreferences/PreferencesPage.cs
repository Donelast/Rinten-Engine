namespace Editor.Preferences;

/// <summary>One page of the editor preferences window.</summary>
public abstract class PreferencesPage : Panel
{
	//Where the page's content goes: bound with @ref by razor, made by hand in C#.
	protected Panel Body { get; set; }

	bool built;

	protected PreferencesPage()
	{
		AddClass( "preferences-page" );
	}

	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );

		BuildOnce();
	}

	void BuildOnce()
	{
		if ( built || Body is null ) return;

		built = true;
		OnBuild();
	}

	//Fills the page, once, as soon as there is a body to fill.
	protected virtual void OnBuild()
	{
	}

	//A sheet of property rows, at the end of the body or of the panel given.
	//Written out in full: from this namespace the plain name finds the widget sheet.
	protected Editor.Inspectors.ControlSheet AddSheet( Panel host = null )
		=> (host ?? Body).AddChild<Editor.Inspectors.ControlSheet>();
}
