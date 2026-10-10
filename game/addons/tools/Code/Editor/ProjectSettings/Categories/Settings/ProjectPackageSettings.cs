namespace Editor.ProjectSettingPages.Categories.Settings;

public sealed class ProjectPackageSettings
{
	[Title( "Title" )]
	public string Title { get; set; }

	[Title( "Package Ident" )]
	public string Ident { get; set; }

	[Title( "Organization Ident" )]
	public string Org { get; set; }

	/// <summary>Where the project is on disk. Shown, not edited: moving a project is moving its folder.</summary>
	[Title( "Project Folder" ), ReadOnly]
	public string Folder { get; set; }

	public void Load( Project project )
	{
		Title = project.Config.Title;
		Ident = project.Config.Ident;
		Org = project.Config.Org;
		Folder = project.GetRootPath();
	}

	public void Apply( Project project )
	{
		project.Config.Title = Title;
		project.Config.Ident = Ident;
		project.Config.Org = Org;
	}
}
