namespace Editor.ProjectSettingsUI;

sealed class ProjectSettingsBodyHost : PanelWidget
{
	public ProjectSettingsBodyHost( Widget parent, ProjectSettingsPanel panel ) : base( parent )
	{
		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.Flexible;
		SetPanel( panel );
	}
}
