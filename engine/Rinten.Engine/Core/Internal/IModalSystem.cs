namespace Rinten.Modals;

public interface IModalSystem
{
	internal static IModalSystem Current { get; set; }

	public bool HasModalsOpen();
	public void CloseAll( bool immediate = false );
	public void Game( string packageIdent );
	public void Map( string packageIdent );
	public void Package( string packageIdent, string page );
	public void Organization( Package.Organization org );
	public void Review( Package package );
	public void Report( string packageIdent );
	public void PackageSelect( string query, Action<Package> onPackageSelected, Action<string> onFilterChanged = null );
	public void MapSelect( Action<string> onMapSelected, string selected = null );
	public void Settings( string page = "" );
	public void CreateGame( in CreateGameOptions options );
	public void PlayerList();
	public void Notice( string title, string message, string icon );
	public void ServiceConnector();

	/// <summary>The menu that is shown when escape is pressed while playing.</summary>
	public void PauseMenu();

	//Shows the benchmark results panel after a local benchmark run completes.

	public bool IsModalOpen { get; }
	public bool IsPauseMenuOpen { get; }
}

/// <summary>Passed to IModalSystem.CreateGame</summary>
public struct CreateGameOptions
{
	public CreateGameOptions( Package package, Action<CreateGameResults> onComplete = null )
	{
		Package = package;
		OnComplete = onComplete;
	}

	public Package Package { get; set; }
	public Action<CreateGameResults> OnComplete { get; set; }
}

public struct CreateGameResults
{
	public CreateGameResults()
	{
	}

	public Dictionary<string, string> GameSettings { get; set; } = new();

	[Obsolete( "Use Map instead" )]
	public string MapIdent => Map;

	/// <summary>The user selected map. This can be an ident of a package, or path to a mount scene.</summary>
	public string Map { get; set; }

	public int MaxPlayers { get; set; }

	public string ServerName { get; set; }

	public Rinten.Network.LobbyPrivacy Privacy { get; set; }
}
