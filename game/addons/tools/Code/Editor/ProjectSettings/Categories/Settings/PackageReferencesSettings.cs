namespace Editor.ProjectSettingPages.Categories.Settings;

public sealed class PackageReferencesSettings
{
	public List<string> PackageReferences { get; set; } = [];

	public void Load( Project project )
	{
		project.Config.PackageReferences ??= [];
		PackageReferences.Clear();
		PackageReferences.AddRange( project.Config.PackageReferences );
	}

	public void Apply( Project project )
	{
		project.Config.PackageReferences = [..PackageReferences];
	}
}
