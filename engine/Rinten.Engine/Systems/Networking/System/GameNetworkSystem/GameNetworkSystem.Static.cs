using NativeEngine;
using Rinten.Engine;
using System.Threading;

namespace Rinten.Network;

public abstract partial class GameNetworkSystem
{
	//Local network ids: server id must exist before networking; host assigns client ids in handshake

	/// <summary>True if we can be considered the host of this session. Either we're not connected to a server, or we are and we are the host.</summary>
	[System.Obsolete( "Moved to Networking.IsHost" )]
	public static bool IsHost => Networking.IsHost;

	/// <summary>True if we're connected to a server and not the host.</summary>
	[System.Obsolete( "Moved to Networking.IsClient" )]
	public static bool IsClient => Networking.IsClient;

	/// <summary>True if we're currently connecting to the server</summary>
	[System.Obsolete( "Moved to Networking.IsConnecting" )]
	public static bool IsConnecting => Networking.IsConnecting;

	/// <summary>True if we're currently connected etc</summary>
	[System.Obsolete( "Moved to Networking.IsActive" )]
	public static bool IsActive => Networking.IsActive;

	[System.Obsolete( "Moved to Networking.CreateLobby" )]
	public static void CreateLobby() => Networking.CreateLobby( new() );

	[Obsolete( "Moved to Networking.Disconnect" )]
	public static void Disconnect() => Networking.Disconnect();

	[Obsolete( "Moved to Networking.Connect" )]
	public static void Connect( string target ) => Networking.Connect( target );

	internal static string CurrentSceneName { get; set; }

	//Called after we've loaded a new scene.
	internal static void OnLoadedScene( string sceneName )
	{
		if ( Networking.System is null )
			return;

		if ( Networking.IsConnecting )
			return;

		Networking.System.OnSceneLoaded();
		CurrentSceneName = sceneName;
	}
}
