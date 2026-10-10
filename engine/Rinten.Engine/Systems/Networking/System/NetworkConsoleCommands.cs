using System.Text;
using Rinten.Engine;

namespace Rinten.Network;


internal static class NetworkConsoleCommands
{
	[ConCmd( "host", ConVarFlags.Protected )]
	public static void StartServer()
	{
		if ( Networking.IsActive )
		{
			Log.Warning( "You are already connected to a server." );
			return;
		}

		Networking.CreateLobby( new() );
	}

	[ConCmd( "connect", ConVarFlags.Protected )]
	public static void ConnectToServer( string target )
	{
		if ( Networking.IsActive )
		{
			Log.Warning( "You are already connected to a server." );
			return;
		}

		Networking.Connect( target );
	}

	[ConCmd( "status", ConVarFlags.Protected )]
	public static unsafe void Status()
	{
		if ( Networking.System is null )
		{
			Log.Warning( "Not connected" );
			return;
		}

		var output = new StringBuilder();

		void Section( string title )
		{
			if ( output.Length > 0 ) output.AppendLine();
			output.AppendLine( $"{title}:" );
		}

		void Field( string label, object value )
		{
			output.AppendLine( $"\t{label + ":",-22}\t{value}" );
		}

		var playerCount = Networking.System.ConnectionInfo.All.Values.Count( x => x.State == Connection.ChannelState.Connected );
		output.AppendLine( $"{Networking.ServerName}" );
		output.AppendLine( $"'{Application.GameIdent}' on '{Networking.MapName}'" );
		output.AppendLine( $"{playerCount}/{Networking.MaxPlayers} players" );

		Section( "Socket Information" );
		Field( "Network Id", Connection.Local.Id );
		Field( "Role", Networking.System.IsHost ? "Host" : "Client" );

		int s = 0;
		foreach ( var socket in Networking.System.Sockets )
		{
			output.AppendLine( $"\tSocket {++s}" );
			Field( "Transport", socket );
		}

		if ( Networking.System.Connection is Connection connect )
		{
			output.AppendLine( "\tPrimary Connection" );
			Field( "Name", connect.Name );
			Field( "Id", connect.Id );
			Field( "State", connect.State );
			Field( "Address", connect.Address );
			Field( "Time", connect.Time );
			Field( "Latency", connect.Latency );
			Field( "Messages", $"{connect.MessagesSent} sent / {connect.MessagesRecieved} received" );
		}

		Section( "Player Info" );
		var players = Networking.System.ConnectionInfo.All.Values.ToArray();
		var nameWidth = Math.Max( 24, players.Select( x => x.Name?.Length ?? 0 ).DefaultIfEmpty( 0 ).Max() );
		output.AppendLine( $"\t{"Name".PadRight( nameWidth )}  {"Id",-17}  {"State",-24}  {"Connected For",-13}  Connection Id" );
		foreach ( var info in players )
		{
			var connectedMinutes = (long)Math.Max( 0, (DateTimeOffset.UtcNow - info.ConnectionTime).TotalMinutes );
			output.AppendLine( $"\t{(info.Name ?? "").PadRight( nameWidth )}  {info.SteamId,-17}  {info.State,-24}  {connectedMinutes + "m",-13}  {info.ConnectionId}" );
		}
		if ( players.Length == 0 ) output.AppendLine( "\tNone" );

		Log.Info( output.ToString() );
	}

	[ConCmd( "disconnect", ConVarFlags.Protected )]
	public static void Disconnect()
	{
		IGameInstanceDll.Current.Disconnect();
	}

	[ConCmd( "reconnect", ConVarFlags.Protected )]
	public static void Reconnect()
	{
		if ( string.IsNullOrWhiteSpace( Networking.LastConnectionString ) )
		{
			Log.Warning( "You were never or are not currently connected to a server." );
			return;
		}

		Networking.Connect( Networking.LastConnectionString );
	}
}
