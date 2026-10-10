namespace Editor.ProjectSettingPages.Categories.Settings;

public sealed class MultiplayerMetaSettings
{
	[Range( 1, 16 )]
	public int MinimumPlayers { get; set; }

	[Range( 1, 16 )]
	public int MaximumPlayers { get; set; }

	public void Load( Project project )
	{
		MinimumPlayers = project.Config.GetMetaOrDefault( "MinPlayers", 1 );
		MaximumPlayers = project.Config.GetMetaOrDefault( "MaxPlayers", 16 );
	}

	public void Apply( Project project )
	{
		project.Config.SetMeta( "MinPlayers", MinimumPlayers );
		project.Config.SetMeta( "MaxPlayers", MaximumPlayers );
	}
}
