using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Rinten.Diagnostics;
using Rinten.Network;

namespace Rinten.NetworkModules.Steam;

//A lobby that is open: a Steam lobby for others to find, and a peer to peer socket for them to connect to. Steam's lobby is only the way of being
//found - what is searched, listed and invited to. The game itself goes over the socket, to whoever owns the lobby, which is whoever opened it.
internal sealed class SteamListener : NetworkListener
{
	private static readonly Logger log = new( "Steam" );

	private readonly SteamProvider provider;
	private readonly uint socket;
	private readonly ConcurrentQueue<SteamPeer> accepted = new();
	private readonly List<SteamPeer> peers = new();
	private readonly Dictionary<string, string> data = new();

	private readonly int maxPlayers;
	private readonly string name;

	private ulong lobby;
	private string serverName;
	private string mapName;
	private bool disposed;
	private int lastMembers;

	public uint Socket => socket;

	public bool IsFull
	{
		get { lock ( peers ) return peers.Count >= maxPlayers - 1; }
	}

	public SteamListener( SteamProvider provider, LobbyConfig config, string game )
	{
		this.provider = provider;

		socket = SteamApi.ListenP2P();

		if ( socket == 0 )
			throw new InvalidOperationException( "Steam would not open a peer to peer socket" );

		maxPlayers = Math.Max( 2, config.MaxPlayers );
		name = config.Name;
		serverName = config.Name;

		provider.Add( this );

		//Private lobbies are joined by being invited, hidden ones by their id - neither is listed.
		var type = config.Privacy == LobbyPrivacy.Private ? 0 : config.Hidden ? 3 : 2;

		provider.Call( SteamApi.CreateLobby( type, maxPlayers ), 513, result =>
		{
			//LobbyCreated_t: a result, then the lobby's id - four bytes in on the platforms
			//Valve packs by four, eight on the ones it packs by eight. Its size says which.
			if ( result is null || BitConverter.ToInt32( result, 0 ) != 1 )
			{
				log.Warning( "Steam could not create a lobby - the game can still be joined by id." );
				return;
			}

			var at = result.Length >= 16 ? 8 : 4;
			lobby = BitConverter.ToUInt64( result, at );

			if ( disposed )
			{
				SteamApi.LeaveLobby( lobby );
				return;
			}

			SteamApi.SetLobbyData( lobby, "game", game );
			SteamApi.SetLobbyData( lobby, "host", SteamApi.SteamId.ToString() );
			SteamApi.SetLobbyData( lobby, "name", serverName ?? SteamApi.PersonaName ?? "Rinten game" );

			if ( mapName is not null ) SteamApi.SetLobbyData( lobby, "map", mapName );

			foreach ( var (key, value) in data )
				SteamApi.SetLobbyData( lobby, "d_" + key, value );

			provider.SayWhere( lobby, 1 );
			lastMembers = 1;

			log.Info( $"lobby {lobby} is open" );
		} );
	}

	/// <summary>The id of the lobby, once Steam has made it - what a friend is invited to.</summary>
	public ulong Lobby => lobby;

	public void Enqueue( SteamPeer peer )
	{
		accepted.Enqueue( peer );
	}

	public void Joined( SteamPeer peer )
	{
		lock ( peers ) peers.Add( peer );
	}

	public void Left( SteamPeer peer )
	{
		lock ( peers ) peers.Remove( peer );
	}

	public override bool TryAccept( out NetworkPeer peer )
	{
		if ( accepted.TryDequeue( out var next ) )
		{
			peer = next;
			return true;
		}

		peer = null;
		return false;
	}

	/// <summary>Once a second: say how many are in, where the friends list shows the party.</summary>
	public override void Tick()
	{
		if ( lobby == 0 || disposed ) return;

		int members;
		lock ( peers ) members = peers.Count + 1;

		if ( members == lastMembers ) return;

		lastMembers = members;
		provider.SayWhere( lobby, members );
	}

	public override void SetData( string key, string value )
	{
		data[key] = value;

		if ( lobby != 0 ) SteamApi.SetLobbyData( lobby, "d_" + key, value );
	}

	public override void SetServerName( string value )
	{
		serverName = value;

		if ( lobby != 0 ) SteamApi.SetLobbyData( lobby, "name", value ?? "" );
	}

	public override void SetMapName( string value )
	{
		mapName = value;

		if ( lobby != 0 && value is not null ) SteamApi.SetLobbyData( lobby, "map", value );
	}

	public override void Dispose()
	{
		if ( disposed ) return;

		disposed = true;

		SteamPeer[] going;
		lock ( peers ) going = peers.ToArray();

		foreach ( var peer in going )
			peer.Close( 0, "lobby closed" );

		provider.Remove( this );

		SteamApi.CloseListen( socket );

		if ( lobby != 0 )
		{
			SteamApi.LeaveLobby( lobby );
			provider.SayWhere( 0, 0 );
		}
	}
}
