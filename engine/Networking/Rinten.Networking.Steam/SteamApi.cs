using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Rinten.NetworkModules.Steam;

//Steam flat C API via runtime-loaded function pointers; interfaces fetched by version, newest first
internal static unsafe class SteamApi
{
	static nint library;

	public static nint Pipe;
	public static nint Matchmaking;
	public static nint Sockets;
	public static nint Friends;
	public static nint User;
	public static nint Utils;

	//Core
	static delegate* unmanaged[Cdecl]<byte*, int> initFlat;
	static delegate* unmanaged[Cdecl]<byte> initLegacy;
	static delegate* unmanaged[Cdecl]<void> shutdown;
	static delegate* unmanaged[Cdecl]<int> getHSteamUser;
	static delegate* unmanaged[Cdecl]<int> getHSteamPipe;
	static delegate* unmanaged[Cdecl]<int, byte*, nint> findOrCreateUserInterface;

	//Manual dispatch
	static delegate* unmanaged[Cdecl]<void> manualInit;
	static delegate* unmanaged[Cdecl]<int, void> manualRunFrame;
	static delegate* unmanaged[Cdecl]<int, CallbackMsg*, byte> manualNext;
	static delegate* unmanaged[Cdecl]<int, void> manualFree;
	static delegate* unmanaged[Cdecl]<int, ulong, void*, int, int, byte*, byte> manualResult;

	//User, friends, utils
	static delegate* unmanaged[Cdecl]<nint, ulong> userGetSteamId;
	static delegate* unmanaged[Cdecl]<nint, byte*> friendsGetPersonaName;
	static delegate* unmanaged[Cdecl]<nint, uint> utilsGetAppId;
	static delegate* unmanaged[Cdecl]<nint, byte*, byte*, byte> friendsSetRichPresence;
	static delegate* unmanaged[Cdecl]<nint, void> friendsClearRichPresence;

	//Matchmaking
	static delegate* unmanaged[Cdecl]<nint, int, int, ulong> mmCreateLobby;
	static delegate* unmanaged[Cdecl]<nint, ulong, ulong> mmJoinLobby;
	static delegate* unmanaged[Cdecl]<nint, ulong, void> mmLeaveLobby;
	static delegate* unmanaged[Cdecl]<nint, ulong, byte*, byte*, byte> mmSetLobbyData;
	static delegate* unmanaged[Cdecl]<nint, ulong, byte*, byte*> mmGetLobbyData;
	static delegate* unmanaged[Cdecl]<nint, ulong, int> mmGetLobbyDataCount;
	static delegate* unmanaged[Cdecl]<nint, ulong, int, byte*, int, byte*, int, byte> mmGetLobbyDataByIndex;
	static delegate* unmanaged[Cdecl]<nint, ulong, byte, byte> mmSetLobbyJoinable;
	static delegate* unmanaged[Cdecl]<nint, ulong, int, byte> mmSetLobbyMemberLimit;
	static delegate* unmanaged[Cdecl]<nint, byte*, byte*, int, void> mmFilterString;
	static delegate* unmanaged[Cdecl]<nint, int, void> mmFilterCount;
	static delegate* unmanaged[Cdecl]<nint, int, void> mmFilterDistance;
	static delegate* unmanaged[Cdecl]<nint, ulong> mmRequestLobbyList;
	static delegate* unmanaged[Cdecl]<nint, int, ulong> mmGetLobbyByIndex;
	static delegate* unmanaged[Cdecl]<nint, ulong, int> mmGetNumMembers;
	static delegate* unmanaged[Cdecl]<nint, ulong, int> mmGetMemberLimit;
	static delegate* unmanaged[Cdecl]<nint, ulong, ulong> mmGetOwner;

	//Networking sockets
	static delegate* unmanaged[Cdecl]<nint, int, int, void*, uint> nsListenP2P;
	static delegate* unmanaged[Cdecl]<nint, void*, int, int, void*, uint> nsConnectP2P;
	static delegate* unmanaged[Cdecl]<nint, uint, int> nsAccept;
	static delegate* unmanaged[Cdecl]<nint, uint, int, byte*, byte, byte> nsCloseConnection;
	static delegate* unmanaged[Cdecl]<nint, uint, byte> nsCloseListen;
	static delegate* unmanaged[Cdecl]<nint, uint, void*, uint, int, long*, int> nsSend;
	static delegate* unmanaged[Cdecl]<nint, uint, nint*, int, int> nsReceive;
	static delegate* unmanaged[Cdecl]<nint, uint, byte*, byte> nsGetInfo;
	static delegate* unmanaged[Cdecl]<nint, uint, byte*, int, void*, int> nsGetStatus;
	static delegate* unmanaged[Cdecl]<nint, void> messageRelease;

	/// <summary>A callback Steam has for us: what it is, and the bytes of its struct.</summary>
	[StructLayout( LayoutKind.Sequential )]
	public struct CallbackMsg
	{
		public int User;
		public int Id;
		public byte* Param;
		public int Size;
	}

	public const int SendUnreliable = 0;
	public const int SendReliable = 8;

	static nint Export( string name, bool required = true )
	{
		if ( NativeLibrary.TryGetExport( library, name, out var found ) ) return found;

		if ( required ) throw new EntryPointNotFoundException( $"{name} is not in the Steam library" );

		return 0;
	}

	static string LibraryName =>
		OperatingSystem.IsWindows() ? "steam_api64.dll"
		: OperatingSystem.IsMacOS() ? "libsteam_api.dylib"
		: "libsteam_api.so";

	/// <summary>Find the Steam library, start the API and fetch what is needed of it.</summary>
	public static bool Load( uint appId, out string error )
	{
		error = null;

		try
		{
			if ( !TryLoadLibrary() )
			{
				error = $"{LibraryName} was not found - it comes with the Steamworks SDK and goes beside the game or in net/steam/";
				return false;
			}

			//Under Steam's own launcher this is already set to the real app; run from a folder, it is what tells the client which app to say the game is.
			if ( string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "SteamAppId" ) ) )
			{
				Environment.SetEnvironmentVariable( "SteamAppId", appId.ToString() );
				Environment.SetEnvironmentVariable( "SteamGameId", appId.ToString() );
			}

			initFlat = (delegate* unmanaged[Cdecl]<byte*, int>)Export( "SteamAPI_InitFlat", false );
			initLegacy = (delegate* unmanaged[Cdecl]<byte>)Export( "SteamAPI_Init", false );

			if ( initFlat == null && initLegacy == null )
			{
				error = "the Steam library has neither SteamAPI_InitFlat nor SteamAPI_Init";
				return false;
			}

			shutdown = (delegate* unmanaged[Cdecl]<void>)Export( "SteamAPI_Shutdown" );
			getHSteamUser = (delegate* unmanaged[Cdecl]<int>)Export( "SteamAPI_GetHSteamUser" );
			getHSteamPipe = (delegate* unmanaged[Cdecl]<int>)Export( "SteamAPI_GetHSteamPipe" );
			findOrCreateUserInterface = (delegate* unmanaged[Cdecl]<int, byte*, nint>)Export( "SteamInternal_FindOrCreateUserInterface" );

			manualInit = (delegate* unmanaged[Cdecl]<void>)Export( "SteamAPI_ManualDispatch_Init" );
			manualRunFrame = (delegate* unmanaged[Cdecl]<int, void>)Export( "SteamAPI_ManualDispatch_RunFrame" );
			manualNext = (delegate* unmanaged[Cdecl]<int, CallbackMsg*, byte>)Export( "SteamAPI_ManualDispatch_GetNextCallback" );
			manualFree = (delegate* unmanaged[Cdecl]<int, void>)Export( "SteamAPI_ManualDispatch_FreeLastCallback" );
			manualResult = (delegate* unmanaged[Cdecl]<int, ulong, void*, int, int, byte*, byte>)Export( "SteamAPI_ManualDispatch_GetAPICallResult" );

			if ( !Start( out error ) ) return false;

			//Before anything else asks for a callback: from here on they are ours to collect.
			manualInit();

			Pipe = getHSteamPipe();
			var user = getHSteamUser();

			User = Interface( user, "SteamUser023", "SteamUser022", "SteamUser021", "SteamUser020" );
			Friends = Interface( user, "SteamFriends018", "SteamFriends017", "SteamFriends016", "SteamFriends015" );
			Utils = Interface( user, "SteamUtils010", "SteamUtils009", "SteamUtils008" );
			Matchmaking = Interface( user, "SteamMatchMaking009", "SteamMatchMaking008" );
			Sockets = Interface( user, "SteamNetworkingSockets012", "SteamNetworkingSockets011", "SteamNetworkingSockets010", "SteamNetworkingSockets009", "SteamNetworkingSockets008" );

			if ( User == 0 || Matchmaking == 0 || Sockets == 0 )
			{
				error = "the Steam client is older than this module can talk to (could not get "
					+ $"{( User == 0 ? "SteamUser " : "" )}{( Matchmaking == 0 ? "SteamMatchmaking " : "" )}{( Sockets == 0 ? "SteamNetworkingSockets" : "" )})";
				return false;
			}

			userGetSteamId = (delegate* unmanaged[Cdecl]<nint, ulong>)Export( "SteamAPI_ISteamUser_GetSteamID" );
			friendsGetPersonaName = (delegate* unmanaged[Cdecl]<nint, byte*>)Export( "SteamAPI_ISteamFriends_GetPersonaName" );
			utilsGetAppId = (delegate* unmanaged[Cdecl]<nint, uint>)Export( "SteamAPI_ISteamUtils_GetAppID", false );

			friendsSetRichPresence = (delegate* unmanaged[Cdecl]<nint, byte*, byte*, byte>)Export( "SteamAPI_ISteamFriends_SetRichPresence", false );
			friendsClearRichPresence = (delegate* unmanaged[Cdecl]<nint, void>)Export( "SteamAPI_ISteamFriends_ClearRichPresence", false );

			mmCreateLobby = (delegate* unmanaged[Cdecl]<nint, int, int, ulong>)Export( "SteamAPI_ISteamMatchmaking_CreateLobby" );
			mmJoinLobby = (delegate* unmanaged[Cdecl]<nint, ulong, ulong>)Export( "SteamAPI_ISteamMatchmaking_JoinLobby" );
			mmLeaveLobby = (delegate* unmanaged[Cdecl]<nint, ulong, void>)Export( "SteamAPI_ISteamMatchmaking_LeaveLobby" );
			mmSetLobbyData = (delegate* unmanaged[Cdecl]<nint, ulong, byte*, byte*, byte>)Export( "SteamAPI_ISteamMatchmaking_SetLobbyData" );
			mmGetLobbyData = (delegate* unmanaged[Cdecl]<nint, ulong, byte*, byte*>)Export( "SteamAPI_ISteamMatchmaking_GetLobbyData" );
			mmGetLobbyDataCount = (delegate* unmanaged[Cdecl]<nint, ulong, int>)Export( "SteamAPI_ISteamMatchmaking_GetLobbyDataCount" );
			mmGetLobbyDataByIndex = (delegate* unmanaged[Cdecl]<nint, ulong, int, byte*, int, byte*, int, byte>)Export( "SteamAPI_ISteamMatchmaking_GetLobbyDataByIndex" );
			mmSetLobbyJoinable = (delegate* unmanaged[Cdecl]<nint, ulong, byte, byte>)Export( "SteamAPI_ISteamMatchmaking_SetLobbyJoinable" );
			mmSetLobbyMemberLimit = (delegate* unmanaged[Cdecl]<nint, ulong, int, byte>)Export( "SteamAPI_ISteamMatchmaking_SetLobbyMemberLimit" );
			mmFilterString = (delegate* unmanaged[Cdecl]<nint, byte*, byte*, int, void>)Export( "SteamAPI_ISteamMatchmaking_AddRequestLobbyListStringFilter" );
			mmFilterCount = (delegate* unmanaged[Cdecl]<nint, int, void>)Export( "SteamAPI_ISteamMatchmaking_AddRequestLobbyListResultCountFilter" );
			mmFilterDistance = (delegate* unmanaged[Cdecl]<nint, int, void>)Export( "SteamAPI_ISteamMatchmaking_AddRequestLobbyListDistanceFilter" );
			mmRequestLobbyList = (delegate* unmanaged[Cdecl]<nint, ulong>)Export( "SteamAPI_ISteamMatchmaking_RequestLobbyList" );
			mmGetLobbyByIndex = (delegate* unmanaged[Cdecl]<nint, int, ulong>)Export( "SteamAPI_ISteamMatchmaking_GetLobbyByIndex" );
			mmGetNumMembers = (delegate* unmanaged[Cdecl]<nint, ulong, int>)Export( "SteamAPI_ISteamMatchmaking_GetNumLobbyMembers" );
			mmGetMemberLimit = (delegate* unmanaged[Cdecl]<nint, ulong, int>)Export( "SteamAPI_ISteamMatchmaking_GetLobbyMemberLimit" );
			mmGetOwner = (delegate* unmanaged[Cdecl]<nint, ulong, ulong>)Export( "SteamAPI_ISteamMatchmaking_GetLobbyOwner" );

			nsListenP2P = (delegate* unmanaged[Cdecl]<nint, int, int, void*, uint>)Export( "SteamAPI_ISteamNetworkingSockets_CreateListenSocketP2P" );
			nsConnectP2P = (delegate* unmanaged[Cdecl]<nint, void*, int, int, void*, uint>)Export( "SteamAPI_ISteamNetworkingSockets_ConnectP2P" );
			nsAccept = (delegate* unmanaged[Cdecl]<nint, uint, int>)Export( "SteamAPI_ISteamNetworkingSockets_AcceptConnection" );
			nsCloseConnection = (delegate* unmanaged[Cdecl]<nint, uint, int, byte*, byte, byte>)Export( "SteamAPI_ISteamNetworkingSockets_CloseConnection" );
			nsCloseListen = (delegate* unmanaged[Cdecl]<nint, uint, byte>)Export( "SteamAPI_ISteamNetworkingSockets_CloseListenSocket" );
			nsSend = (delegate* unmanaged[Cdecl]<nint, uint, void*, uint, int, long*, int>)Export( "SteamAPI_ISteamNetworkingSockets_SendMessageToConnection" );
			nsReceive = (delegate* unmanaged[Cdecl]<nint, uint, nint*, int, int>)Export( "SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection" );
			nsGetInfo = (delegate* unmanaged[Cdecl]<nint, uint, byte*, byte>)Export( "SteamAPI_ISteamNetworkingSockets_GetConnectionInfo" );
			nsGetStatus = (delegate* unmanaged[Cdecl]<nint, uint, byte*, int, void*, int>)Export( "SteamAPI_ISteamNetworkingSockets_GetConnectionRealTimeStatus", false );
			messageRelease = (delegate* unmanaged[Cdecl]<nint, void>)Export( "SteamAPI_SteamNetworkingMessage_t_Release" );

			return true;
		}
		catch ( Exception e )
		{
			error = e.Message;
			return false;
		}
	}

	static bool Start( out string error )
	{
		error = null;

		if ( initFlat != null )
		{
			var message = stackalloc byte[1024];
			var result = initFlat( message );

			//k_ESteamAPIInitResult_OK
			if ( result == 0 ) return true;

			error = result switch
			{
				1 => "Steam is not running, or the game was started as a different user than the client",
				2 => "the Steam client is too old for this library",
				_ => "Steam did not start",
			};

			var text = Marshal.PtrToStringUTF8( (nint)message );
			if ( !string.IsNullOrWhiteSpace( text ) ) error += $": {text}";

			return false;
		}

		if ( initLegacy() != 0 ) return true;

		error = "Steam is not running, or does not know the app id (is the client open and signed in?)";
		return false;
	}

	static bool TryLoadLibrary()
	{
		var name = LibraryName;

		var places = new[]
		{
			Path.Combine( "net", "steam", name ),
			Path.Combine( AppContext.BaseDirectory, "net", "steam", name ),
			Path.Combine( AppContext.BaseDirectory, name ),
			Path.Combine( Environment.CurrentDirectory, name ),
			name,
		};

		foreach ( var place in places )
		{
			try
			{
				var full = Path.IsPathRooted( place ) || place.Contains( Path.DirectorySeparatorChar ) ? Path.GetFullPath( place ) : place;

				if ( NativeLibrary.TryLoad( full, out library ) ) return true;
			}
			catch ( Exception )
			{
				//Not this one.
			}
		}

		return false;
	}

	static nint Interface( int user, params string[] versions )
	{
		foreach ( var version in versions )
		{
			var bytes = Utf8( version );

			fixed ( byte* p = bytes )
			{
				var found = findOrCreateUserInterface( user, p );
				if ( found != 0 ) return found;
			}
		}

		return 0;
	}

	public static void Shutdown()
	{
		if ( shutdown != null ) shutdown();
	}

	//---- helpers

	public static byte[] Utf8( string text )
	{
		var bytes = new byte[Encoding.UTF8.GetByteCount( text ?? "" ) + 1];
		Encoding.UTF8.GetBytes( text ?? "", 0, text?.Length ?? 0, bytes, 0 );
		return bytes;
	}

	static string Read( byte* text ) => text == null ? null : Marshal.PtrToStringUTF8( (nint)text );

	//---- dispatch

	public static void RunFrame() => manualRunFrame( (int)Pipe );

	public static bool NextCallback( out CallbackMsg message )
	{
		CallbackMsg m;
		var got = manualNext( (int)Pipe, &m ) != 0;
		message = m;
		return got;
	}

	public static void FreeCallback() => manualFree( (int)Pipe );

	public static bool CallResult( ulong call, int expected, byte[] into )
	{
		byte failed;

		fixed ( byte* p = into )
		{
			var ok = manualResult( (int)Pipe, call, p, into.Length, expected, &failed ) != 0;
			return ok && failed == 0;
		}
	}

	//---- identity

	public static ulong SteamId => userGetSteamId( User );

	public static string PersonaName => Friends == 0 ? null : Read( friendsGetPersonaName( Friends ) );

	public static uint AppId => utilsGetAppId == null || Utils == 0 ? 0 : utilsGetAppId( Utils );

	//---- rich presence

	/// <summary>Null removes the key. Steam's limits are 20 keys, 64 bytes of key and 256 of value.</summary>
	public static bool SetRichPresence( string key, string value )
	{
		if ( friendsSetRichPresence == null || Friends == 0 ) return false;

		fixed ( byte* k = Utf8( key ) )
		{
			if ( value is null ) return friendsSetRichPresence( Friends, k, null ) != 0;

			fixed ( byte* v = Utf8( value ) )
				return friendsSetRichPresence( Friends, k, v ) != 0;
		}
	}

	public static void ClearRichPresence()
	{
		if ( friendsClearRichPresence != null && Friends != 0 ) friendsClearRichPresence( Friends );
	}

	//---- matchmaking

	public static ulong CreateLobby( int type, int max ) => mmCreateLobby( Matchmaking, type, max );

	public static ulong JoinLobby( ulong lobby ) => mmJoinLobby( Matchmaking, lobby );

	public static void LeaveLobby( ulong lobby ) => mmLeaveLobby( Matchmaking, lobby );

	public static bool SetLobbyData( ulong lobby, string key, string value )
	{
		fixed ( byte* k = Utf8( key ) )
		fixed ( byte* v = Utf8( value ) )
			return mmSetLobbyData( Matchmaking, lobby, k, v ) != 0;
	}

	public static string GetLobbyData( ulong lobby, string key )
	{
		fixed ( byte* k = Utf8( key ) )
			return Read( mmGetLobbyData( Matchmaking, lobby, k ) );
	}

	public static int GetLobbyDataCount( ulong lobby ) => mmGetLobbyDataCount( Matchmaking, lobby );

	public static bool GetLobbyDataByIndex( ulong lobby, int index, out string key, out string value )
	{
		var k = stackalloc byte[256];
		var v = stackalloc byte[8192];

		var ok = mmGetLobbyDataByIndex( Matchmaking, lobby, index, k, 256, v, 8192 ) != 0;

		key = ok ? Read( k ) : null;
		value = ok ? Read( v ) : null;

		return ok;
	}

	public static void SetLobbyJoinable( ulong lobby, bool joinable ) => mmSetLobbyJoinable( Matchmaking, lobby, joinable ? (byte)1 : (byte)0 );

	public static void SetLobbyMemberLimit( ulong lobby, int max ) => mmSetLobbyMemberLimit( Matchmaking, lobby, max );

	public static void FilterString( string key, string value )
	{
		fixed ( byte* k = Utf8( key ) )
		fixed ( byte* v = Utf8( value ) )
			mmFilterString( Matchmaking, k, v, 0 );
	}

	public static void FilterCount( int count ) => mmFilterCount( Matchmaking, count );

	/// <summary>Worldwide: the default only looks near the player.</summary>
	public static void FilterWorldwide() => mmFilterDistance( Matchmaking, 3 );

	public static ulong RequestLobbyList() => mmRequestLobbyList( Matchmaking );

	public static ulong LobbyByIndex( int index ) => mmGetLobbyByIndex( Matchmaking, index );

	public static int LobbyMembers( ulong lobby ) => mmGetNumMembers( Matchmaking, lobby );

	public static int LobbyLimit( ulong lobby ) => mmGetMemberLimit( Matchmaking, lobby );

	public static ulong LobbyOwner( ulong lobby ) => mmGetOwner( Matchmaking, lobby );

	//---- sockets

	public static uint ListenP2P() => nsListenP2P( Sockets, 0, 0, null );

	/// <summary>A SteamNetworkingIdentity for a Steam account, written by hand: the type, the size, the id.</summary>
	public static uint ConnectP2P( ulong steamId )
	{
		var identity = stackalloc byte[136];

		for ( var i = 0; i < 136; i++ ) identity[i] = 0;

		//k_ESteamNetworkingIdentityType_SteamID, and the eight bytes of it
		*(int*)identity = 16;
		*(int*)(identity + 4) = 8;
		*(ulong*)(identity + 8) = steamId;

		return nsConnectP2P( Sockets, identity, 0, 0, null );
	}

	public static void Accept( uint connection ) => nsAccept( Sockets, connection );

	public static void CloseConnection( uint connection, int reason, string debug )
	{
		//Steam only takes reasons the application owns, 1000 to 1999
		var code = 1000 + Math.Clamp( reason, 0, 999 );

		fixed ( byte* d = Utf8( debug ) )
			nsCloseConnection( Sockets, connection, code, d, 1 );
	}

	public static void CloseListen( uint listen ) => nsCloseListen( Sockets, listen );

	public static bool Send( uint connection, byte[] data, bool reliable )
	{
		fixed ( byte* p = data )
			return nsSend( Sockets, connection, p, (uint)data.Length, reliable ? SendReliable : SendUnreliable, null ) == 1;
	}

	/// <summary>Everything that has arrived on a connection, each as its own array.</summary>
	public static int Receive( uint connection, Action<byte[]> each )
	{
		const int Batch = 64;

		var messages = stackalloc nint[Batch];
		var total = 0;

		while ( true )
		{
			var got = nsReceive( Sockets, connection, messages, Batch );
			if ( got <= 0 ) break;

			for ( var i = 0; i < got; i++ )
			{
				//SteamNetworkingMessage_t starts with the data pointer and its size
				var message = (byte*)messages[i];
				var data = *(byte**)message;
				var size = *(int*)(message + 8);

				var copy = new byte[size];
				Marshal.Copy( (nint)data, copy, 0, size );

				messageRelease( messages[i] );
				each( copy );
			}

			total += got;
			if ( got < Batch ) break;
		}

		return total;
	}

	/// <summary>The state of a connection, and the rest of what Steam knows about it.</summary>
	public struct ConnectionInfo
	{
		public int State;
		public int EndReason;
		public string EndDebug;
		public uint ListenSocket;
		public ulong Remote;
	}

	/// <summary>SteamNetConnectionInfo_t, at the offsets it has with its natural alignment: the remote identity first (136 bytes - a type, a size, then the id), then user data, the listen socket, the address, the relay POPs, the state, the end reason and the end text.</summary>
	public static bool GetInfo( uint connection, out ConnectionInfo info )
	{
		var buffer = stackalloc byte[1024];

		for ( var i = 0; i < 1024; i++ ) buffer[i] = 0;

		info = default;

		if ( nsGetInfo( Sockets, connection, buffer ) == 0 ) return false;

		info.Remote = *(int*)buffer == 16 ? *(ulong*)(buffer + 8) : 0;
		info.ListenSocket = *(uint*)(buffer + 144);
		info.State = *(int*)(buffer + 176);
		info.EndReason = *(int*)(buffer + 180);
		info.EndDebug = Read( buffer + 184 );

		return true;
	}

	/// <summary>Ping and throughput, as Steam measures them.</summary>
	public static bool GetStatus( uint connection, out Rinten.Network.ConnectionStats stats )
	{
		stats = default;

		if ( nsGetStatus == null ) return false;

		var buffer = stackalloc byte[256];

		for ( var i = 0; i < 256; i++ ) buffer[i] = 0;

		if ( nsGetStatus( Sockets, connection, buffer, 0, null ) != 1 ) return false;

		stats = new Rinten.Network.ConnectionStats
		{
			Ping = *(int*)(buffer + 4),
			ConnectionQuality = Math.Clamp( *(float*)(buffer + 8), 0.0f, 1.0f ),
			OutPacketsPerSecond = *(float*)(buffer + 16),
			OutBytesPerSecond = *(float*)(buffer + 20),
			InPacketsPerSecond = *(float*)(buffer + 24),
			InBytesPerSecond = *(float*)(buffer + 28),
			SendRateBytesPerSecond = *(int*)(buffer + 32),
		};

		return true;
	}
}
