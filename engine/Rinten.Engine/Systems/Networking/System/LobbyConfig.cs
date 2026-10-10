namespace Rinten.Network;

[Expose]
public struct LobbyConfig
{
	/// <summary>Whether to automatically destroy this lobby when the host leaves. This is only applicable to P2P lobbies.</summary>
	public bool DestroyWhenHostLeaves { get; set; }

	/// <summary>Whether to periodically switch to the best possible host candidate. This is only applicable to P2P lobbies.</summary>
	public bool AutoSwitchToBestHost { get; set; }

	/// <summary>Whether to hide this lobby from appearing in the server list. It will still be queryable programatically, so long as the <see cref="Privacy"/> mode allows it.</summary>
	public bool Hidden { get; set; }

	/// <summary>Determines who is able to connect to this lobby. This will be public by default.</summary>
	public LobbyPrivacy Privacy { get; set; }

	/// <summary>The maximum amount of players this lobby can hold. By default, this will be the Max Players set in the current Game Package's project settings.</summary>
	public int MaxPlayers { get; set; }

	/// <summary>The name of this lobby. If this isn't set, a default lobby name will be chosen instead.</summary>
	public string Name { get; set; }

	public LobbyConfig()
	{
		DestroyWhenHostLeaves = ProjectSettings.Networking.DestroyLobbyWhenHostLeaves;
		AutoSwitchToBestHost = ProjectSettings.Networking.AutoSwitchToBestHost;
		MaxPlayers = Application.GamePackage?.GetCachedMeta( "MaxPlayers", 32 ) ?? 32;
	}
}
