namespace Editor.ProjectSettingPages.Categories.Settings;

public sealed class ResourcesSettings
{
	[Title( "Extra Resource Paths" ), TextArea, WideMode]
	public string Paths { get; set; }

	public void Load( Project project )
	{
		Paths = project.Config.Resources ?? "";
	}

	public void Apply( Project project )
	{
		project.Config.Resources = Paths;
	}
}
