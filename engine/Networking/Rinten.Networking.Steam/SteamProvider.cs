using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Threading.Tasks;
using Rinten.Diagnostics;
using Rinten.Network;

namespace Rinten.NetworkModules.Steam;

/// <summary>Steam as a way into a game: lobbies you can list and be invited to, and peer to peer through Valve's relays, so a host behind a router needs no forwarded port.</summary>
public sealed class SteamProvider : NetworkProvider
{
	private static readonly Logger log = new( "Steam" );

	/// <summary>Valve's test app, which anyone can run.</summary>
	public const uint Spacewar = 480;

	private readonly bool available;
	private readonly bool hostLobbies = true;
	private readonly ulong localId;
	private readonly string localName;

	private readonly Dictionary<uint, SteamPeer> byConnection = new();
	private readonly List<SteamPeer> peers = new();
	private readonly Dictionary<uint, SteamListener> listeners = new();
	private readonly Dictionary<ulong, (int Expected, Action<byte[]> Done)> calls = new();

	private readonly Stopwatch watch = Stopwatch.StartNew();
	private double nextMeasure;
	private double clock;
	private string launchJoin;
	private double launchJoinAt;

	public override string Scheme => "steam";

	public override string Title => "Steam";

	public override bool IsAvailable => available;

	public override bool HostsByDefault => hostLobbies;

	public override SteamId? LocalId => available ? new SteamId( localId ) : null;

	public override string LocalName => available ? localName : null;

	public SteamProvider()
	{
		try
		{
			uint appId = Spacewar;

			//No settings and no Steam library are the ordinary case - the engine carries no Steam,
			//and a game that wants it puts both beside itself - so neither is said on every start.
			TryReadSettings( ref appId, ref hostLobbies, ref displayToken );

			if ( !SteamApi.Load( appId, out _ ) )
				return;

			localId = SteamApi.SteamId;
			localName = SteamApi.PersonaName;
			available = localId != 0;

			//Which line of text the friends list shows, so a game's "status" appears there.
			if ( available && !string.IsNullOrWhiteSpace( displayToken ) )
				SteamApi.SetRichPresence( "steam_display", displayToken );

			log.Info( $"signed in as {localName} ({localId}), app {SteamApi.AppId}" );

			ReadLaunchArguments();
		}
		catch ( Exception e )
		{
			log.Warning( e, $"Steam is not available: {e.Message}" );
			available = false;
		}
	}

	private static bool TryReadSettings( ref uint appId, ref bool host, ref string token )
	{
		var read = false;

		var file = Path.Combine( "net", "steam", "steam.json" );

		if ( File.Exists( file ) )
		{
			using var doc = JsonDocument.Parse( File.ReadAllText( file ), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip } );

			if ( doc.RootElement.TryGetProperty( "appId", out var id ) && id.TryGetUInt32( out var parsed ) && parsed != 0 )
				appId = parsed;

			if ( doc.RootElement.TryGetProperty( "host", out var h ) && h.ValueKind is JsonValueKind.True or JsonValueKind.False )
				host = h.GetBoolean();

			if ( doc.RootElement.TryGetProperty( "richPresenceToken", out var t ) && t.ValueKind == JsonValueKind.String )
				token = t.GetString();

			read = true;
		}

		if ( uint.TryParse( Environment.GetEnvironmentVariable( "RINTEN_STEAM_APPID" ), out var env ) && env != 0 )
			appId = env;

		var args = Environment.GetCommandLineArgs();

		for ( var i = 0; i < args.Length - 1; i++ )
		{
			if ( args[i].Equals( "-steamappid", StringComparison.OrdinalIgnoreCase ) && uint.TryParse( args[i + 1], out var flag ) && flag != 0 )
				appId = flag;
		}

		return read;
	}

	//A game launched from a friend's invite is started with +connect_lobby id . It is held
	//for a few seconds before it is acted on: the game has to be up to be joined into.
	private void ReadLaunchArguments()
	{
		var args = Environment.GetCommandLineArgs();

		for ( var i = 0; i < args.Length - 1; i++ )
		{
			if ( !args[i].Equals( "+connect_lobby", StringComparison.OrdinalIgnoreCase ) ) continue;

			if ( ulong.TryParse( args[i + 1], out var lobby ) && lobby != 0 )
			{
				launchJoin = $"lobby/{lobby}";
				launchJoinAt = 3.0;
			}
		}
	}

	//---- rich presence

	//Rich presence token from the app's Steamworks page; #Status expands to %status%
	private string displayToken = "#Status";

	public override void SetRichPresence( string key, string value )
	{
		if ( !available ) return;

		SteamApi.SetRichPresence( key, value );
	}

	public override void ClearRichPresence()
	{
		if ( !available ) return;

		SteamApi.ClearRichPresence();
		SayWhere( 0, 0 );
	}

	//Where the player is, for the friends list and its Join Game button: the keys Steam's own UI looks for. A lobby of 0 says nowhere.
	internal void SayWhere( ulong lobby, int members )
	{
		if ( !available ) return;

		if ( lobby == 0 )
		{
			SteamApi.SetRichPresence( "connect", null );
			SteamApi.SetRichPresence( "steam_player_group", null );
			SteamApi.SetRichPresence( "steam_player_group_size", null );
			return;
		}

		SteamApi.SetRichPresence( "connect", $"+connect_lobby {lobby}" );
		SteamApi.SetRichPresence( "steam_player_group", lobby.ToString() );
		SteamApi.SetRichPresence( "steam_player_group_size", members.ToString() );
	}

	//---- hosting and joining

	public override NetworkListener Listen( LobbyConfig config )
	{
		return new SteamListener( this, config, GameIdent );
	}

	public override NetworkPeer Connect( string target )
	{
		var peer = new SteamPeer( this );

		if ( target.StartsWith( "lobby/", StringComparison.OrdinalIgnoreCase ) && ulong.TryParse( target[6..], out var lobby ) )
		{
			Call( SteamApi.JoinLobby( lobby ), 504, result =>
			{
				//LobbyEnter_t: the lobby, who may chat, whether it is locked, and the answer - 1 is yes.
				var response = result is null ? 0u : BitConverter.ToUInt32( result, 16 );

				if ( response != 1 )
				{
					peer.End( response == 2 ? "the lobby does not exist" : response == 5 ? "the lobby is full" : $"could not enter the lobby (code {response})" );
					return;
				}

				var owner = SteamApi.LobbyOwner( lobby );

				peer.Lobby = lobby;
				Open( peer, owner );

				//In their lobby: friends of this player can join it from here too.
				SayWhere( lobby, SteamApi.LobbyMembers( lobby ) );
			} );

			return peer;
		}

		if ( ulong.TryParse( target, out var id ) && id != 0 )
		{
			Open( peer, id );
			return peer;
		}

		peer.End( $"'{target}' is not a lobby or a Steam id" );
		return peer;
	}

	private void Open( SteamPeer peer, ulong remote )
	{
		var connection = SteamApi.ConnectP2P( remote );

		if ( connection == 0 )
		{
			peer.End( "Steam would not start a connection" );
			return;
		}

		peer.Attach( connection, remote );

		lock ( byConnection )
		{
			byConnection[connection] = peer;
			peers.Add( peer );
		}
	}

	public override Task<List<LobbyInformation>> QueryLobbies( IReadOnlyDictionary<string, string> filters )
	{
		var source = new TaskCompletionSource<List<LobbyInformation>>();

		//What a request asks for is set before it, and used by it once.
		SteamApi.FilterString( "game", GameIdent );

		foreach ( var (key, value) in filters )
			SteamApi.FilterString( "d_" + key, value );

		SteamApi.FilterCount( 50 );
		SteamApi.FilterWorldwide();

		Call( SteamApi.RequestLobbyList(), 510, result =>
		{
			var found = new List<LobbyInformation>();

			if ( result is not null )
			{
				var count = (int)BitConverter.ToUInt32( result, 0 );

				for ( var i = 0; i < count; i++ )
					found.Add( Describe( SteamApi.LobbyByIndex( i ) ) );
			}

			source.TrySetResult( found );
		} );

		return Timed( source.Task );
	}

	private static async Task<List<LobbyInformation>> Timed( Task<List<LobbyInformation>> task )
	{
		if ( await Task.WhenAny( task, Task.Delay( TimeSpan.FromSeconds( 10 ) ) ) == task )
			return await task;

		return new List<LobbyInformation>();
	}

	private static LobbyInformation Describe( ulong lobby )
	{
		var data = new Dictionary<string, string>();

		for ( var i = 0; i < SteamApi.GetLobbyDataCount( lobby ); i++ )
		{
			if ( SteamApi.GetLobbyDataByIndex( lobby, i, out var key, out var value ) && key.StartsWith( "d_", StringComparison.Ordinal ) )
				data[key[2..]] = value;
		}

		return new LobbyInformation
		{
			Provider = "steam",
			Target = $"steam://lobby/{lobby}",
			Name = SteamApi.GetLobbyData( lobby, "name" ),
			Map = SteamApi.GetLobbyData( lobby, "map" ),
			Members = SteamApi.LobbyMembers( lobby ),
			MaxMembers = SteamApi.LobbyLimit( lobby ),
			Data = data,
		};
	}

	//---- what the listeners and peers ask of us

	internal void Add( SteamListener listener )
	{
		lock ( byConnection ) listeners[listener.Socket] = listener;
	}

	internal void Remove( SteamListener listener )
	{
		lock ( byConnection ) listeners.Remove( listener.Socket );
	}

	//Do something with the answer to a call to Steam, when it comes.
	internal void Call( ulong call, int expected, Action<byte[]> done )
	{
		//k_uAPICallInvalid: Steam would not even start it.
		if ( call == 0 )
		{
			done( null );
			return;
		}

		lock ( calls ) calls[call] = (expected, done);
	}

	//A peer is done with - close what it holds and stop collecting for it.
	internal void Forget( SteamPeer peer, int code, string reason )
	{
		lock ( byConnection )
		{
			if ( peer.Connection != 0 ) byConnection.Remove( peer.Connection );
			peers.Remove( peer );
		}

		peer.Listener?.Left( peer );

		if ( peer.Connection != 0 ) SteamApi.CloseConnection( peer.Connection, code, reason ?? "closed" );
		if ( peer.Lobby != 0 )
		{
			SteamApi.LeaveLobby( peer.Lobby );
			SayWhere( 0, 0 );
		}
	}

	//---- every frame

	public override void Tick()
	{
		if ( !available ) return;

		//Steam wants to be looked at, and it is where every answer arrives.
		SteamApi.RunFrame();

		while ( SteamApi.NextCallback( out var message ) )
		{
			try
			{
				Handle( message );
			}
			catch ( Exception e )
			{
				log.Warning( e, $"steam callback {message.Id}: {e.Message}" );
			}
			finally
			{
				SteamApi.FreeCallback();
			}
		}

		SteamPeer[] now;
		lock ( byConnection ) now = peers.ToArray();

		foreach ( var peer in now ) peer.Pump();

		clock = watch.Elapsed.TotalSeconds;

		if ( clock >= nextMeasure )
		{
			nextMeasure = clock + 0.5;

			foreach ( var peer in now ) peer.Measure();
		}

		if ( launchJoin is not null && clock >= launchJoinAt )
		{
			var target = launchJoin;
			launchJoin = null;

			RequestJoin( target );
		}
	}

	private unsafe void Handle( in SteamApi.CallbackMsg message )
	{
		switch ( message.Id )
		{
			//SteamAPICallCompleted_t: which call, which callback it is the answer of, and how big
			case 703:
			{
				var call = *(ulong*)message.Param;
				var kind = *(int*)(message.Param + 8);
				var size = *(uint*)(message.Param + 12);

				(int Expected, Action<byte[]> Done) pending;

				lock ( calls )
				{
					if ( !calls.Remove( call, out pending ) ) return;
				}

				var buffer = new byte[size];
				pending.Done( SteamApi.CallResult( call, kind, buffer ) ? buffer : null );
				return;
			}

			//SteamNetConnectionStatusChangedCallback_t: the connection is the first thing in it
			case 1221:
			{
				OnConnectionChanged( *(uint*)message.Param );
				return;
			}

			//GameRichPresenceJoinRequested_t: a friend's "connect" key, clicked on. A friend, then the string we put there ourselves: "+connect_lobby <id>".
			case 337:
			{
				var connect = System.Runtime.InteropServices.Marshal.PtrToStringUTF8( (nint)( message.Param + 8 ) );

				if ( TryParseConnect( connect, out var lobby ) )
					RequestJoin( $"lobby/{lobby}" );

				return;
			}

			//GameLobbyJoinRequested_t: a friend's invite, or Join Game in the friends list
			case 333:
			{
				RequestJoin( $"lobby/{*(ulong*)message.Param}" );
				return;
			}
		}
	}

	//The lobby a +connect_lobby id string names.
	private static bool TryParseConnect( string connect, out ulong lobby )
	{
		lobby = 0;

		if ( string.IsNullOrWhiteSpace( connect ) ) return false;

		var parts = connect.Split( ' ', StringSplitOptions.RemoveEmptyEntries );

		for ( var i = 0; i < parts.Length - 1; i++ )
		{
			if ( parts[i].Equals( "+connect_lobby", StringComparison.OrdinalIgnoreCase ) && ulong.TryParse( parts[i + 1], out lobby ) )
				return lobby != 0;
		}

		return false;
	}

	private void OnConnectionChanged( uint connection )
	{
		if ( !SteamApi.GetInfo( connection, out var info ) ) return;

		SteamPeer peer;
		SteamListener listener = null;

		lock ( byConnection )
		{
			byConnection.TryGetValue( connection, out peer );

			if ( peer is null && info.ListenSocket != 0 )
				listeners.TryGetValue( info.ListenSocket, out listener );
		}

		//Somebody is asking to join one of our lobbies: connecting, on a socket of ours.
		if ( peer is null )
		{
			if ( info.State == 1 && listener is not null )
			{
				if ( listener.IsFull )
				{
					SteamApi.CloseConnection( connection, 1, "the lobby is full" );
					return;
				}

				SteamApi.Accept( connection );

				peer = new SteamPeer( this, connection, info.Remote, listener );

				lock ( byConnection )
				{
					byConnection[connection] = peer;
					peers.Add( peer );
				}

				listener.Joined( peer );
				return;
			}

			//Nothing of ours, and over: Steam still wants it let go of.
			if ( info.State is 4 or 5 ) SteamApi.CloseConnection( connection, 0, "" );

			return;
		}

		switch ( info.State )
		{
			case 3: //connected
				if ( peer.Listener is not null && !peer.Announced )
				{
					peer.Announced = true;
					peer.Listener.Enqueue( peer );
				}
				break;

			case 4: //closed by the other end
			case 5: //lost - no route, timed out
				peer.End( string.IsNullOrWhiteSpace( info.EndDebug ) ? $"connection ended ({info.EndReason})" : info.EndDebug );

				//Nothing more is coming, but what did is still to be read: the engine lets go
				//of the peer itself once it has, and until then it is not closed from under it.
				lock ( byConnection )
				{
					byConnection.Remove( connection );
					peers.Remove( peer );
				}

				SteamApi.Receive( connection, bytes => peer.Deliver( bytes ) );
				SteamApi.CloseConnection( connection, 0, "" );
				break;
		}
	}

	public override void Shutdown()
	{
		if ( !available ) return;

		SteamPeer[] all;
		SteamListener[] open;

		lock ( byConnection )
		{
			all = peers.ToArray();
			open = listeners.Values.ToArray();
		}

		foreach ( var listener in open ) listener.Dispose();
		foreach ( var peer in all ) peer.Close( 0, "shutting down" );

		SteamApi.ClearRichPresence();

		SteamApi.Shutdown();
	}
}
