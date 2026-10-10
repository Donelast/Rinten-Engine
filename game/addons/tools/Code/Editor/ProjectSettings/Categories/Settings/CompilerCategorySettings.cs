namespace Editor.ProjectSettingPages.Categories.Settings;

public sealed class CompilerCategorySettings
{
	Compiler.Configuration configuration;

	public string DefineConstants
	{
		get => configuration.DefineConstants;
		set => configuration.DefineConstants = value;
	}

	public bool Nullables
	{
		get => configuration.Nullables;
		set => configuration.Nullables = value;
	}

	public string RootNamespace
	{
		get => configuration.RootNamespace;
		set => configuration.RootNamespace = value;
	}

	public string NoWarn
	{
		get => configuration.NoWarn;
		set => configuration.NoWarn = value;
	}

	public string WarningsAsErrors
	{
		get => configuration.WarningsAsErrors;
		set => configuration.WarningsAsErrors = value;
	}

	public bool Unsafe
	{
		get => configuration.Unsafe;
		set => configuration.Unsafe = value;
	}

	public CompilerReleaseMode ReleaseMode
	{
		get => (CompilerReleaseMode)configuration.ReleaseMode;
		set => configuration.ReleaseMode = value == CompilerReleaseMode.Debug ? Compiler.ReleaseMode.Debug : Compiler.ReleaseMode.Release;
	}

	public string ProjectFilename { get; set; }

	public enum CompilerReleaseMode
	{
		Debug,
		Release
	}

	public void Load( Project project )
	{
		if ( !project.Config.TryGetMeta( "Compiler", out configuration ) )
			configuration = new Compiler.Configuration();

		configuration.Clean();
		ProjectFilename = project.Config.Metadata.TryGetValue( "CsProjName", out var value ) ? value.ToString() : default;
	}

	public void Apply( Project project )
	{
		project.Config.SetMeta( "Compiler", configuration );
		project.Config.Metadata["CsProjName"] = ProjectFilename;
	}
}
