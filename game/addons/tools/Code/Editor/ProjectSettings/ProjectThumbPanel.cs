namespace Editor.ProjectSettingsUI;

[StyleSheet( "ProjectThumbPanel.cs.scss" )]
public class ProjectThumbPanel : Panel
{
	public Project Project { get; set; }
	public Action Changed { get; set; }

	string thumbPath;

	public ProjectThumbPanel()
	{
	}

	public override void Tick()
	{
		base.Tick();

		var path = Project?.GetThumbPath();
		if ( path == thumbPath ) return;

		thumbPath = path;
		SetClass( "has-thumb", !string.IsNullOrEmpty( path ) );

		if ( string.IsNullOrEmpty( path ) )
		{
			Style.BackgroundImage = null;
			return;
		}

		Style.Set( $"background-image: url( {path} );" );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );
		if ( Project is null ) return;
		if ( !ProjectThumb.Pick( Project ) ) return;
		Changed?.Invoke();
	}
}
