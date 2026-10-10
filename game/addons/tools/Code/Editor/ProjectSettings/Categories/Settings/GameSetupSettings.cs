namespace Editor.ProjectSettingPages.Categories.Settings;

public sealed class GameSetupSettings
{
	/// <summary>The scene the game starts in.</summary>
	[Title( "Start Scene" )]
	public SceneFile StartScene { get; set; }

	/// <summary>The scene a dedicated server starts in. Left empty, it starts in the Start Scene.</summary>
	[Title( "Server Scene" )]
	public SceneFile ServerScene { get; set; }

	/// <summary>A scene loaded additively on top of every scene, unless the scene opts out (Wants System Scene).</summary>
	public SceneFile SystemScene { get; set; }

	[Group( "Features" )]
	public bool UsesStreamerFeatures { get; set; }

	public LaunchModes LaunchMode { get; set; }

	public void Load( Project project )
	{
		StartScene = ResourceLibrary.Get<SceneFile>( project.Config.GetMetaOrDefault( "StartupScene", "start.scene" ) );
		ServerScene = ResourceLibrary.Get<SceneFile>( project.Config.GetMetaOrDefault( "DedicatedServerStartupScene", "" ) );
		LaunchMode = project.Config.GetMetaOrDefault( "LaunchMode", LaunchModes.Normal );
		SystemScene = ResourceLibrary.Get<SceneFile>( project.Config.GetMetaOrDefault( "SystemScene", "" ) );
		UsesStreamerFeatures = project.Config.GetMetaOrDefault( "UsesStreamerFeatures", false );
	}

	public void Apply( Project project )
	{
		//The keys stay what builds and the editor already read.
		project.Config.SetMeta( "StartupScene", StartScene?.ResourcePath ?? null );
		project.Config.SetMeta( "DedicatedServerStartupScene", ServerScene?.ResourcePath ?? null );
		project.Config.SetMeta( "LaunchMode", LaunchMode );

		project.Config.SetMeta( "SystemScene", SystemScene?.ResourcePath ?? null );
		project.Config.SetMeta( "MapStartupScene", null );
		project.Config.SetMeta( "UsesStreamerFeatures", UsesStreamerFeatures ? UsesStreamerFeatures : null );
	}

	public enum LaunchModes
	{
		[Icon( "smart_display" )] Normal,
		[Icon( "wysiwyg" )] Launcher,
		[Icon( "fast_forward" )] QuickPlay,
		[Icon( "dns" )] DedicatedServerOnly
	}
}
