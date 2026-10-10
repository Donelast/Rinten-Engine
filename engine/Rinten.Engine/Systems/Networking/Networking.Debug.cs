using Rinten.Network;

namespace Rinten.Debug;

public static class Networking
{
	/// <summary>Add an empty connection for debugging purposes. This connection cannot receive or send data and it won't be visible to other clients.</summary>
	public static void AddEmptyConnection()
	{
		if ( !Rinten.Networking.IsActive )
			return;

		var connection = new EmptyConnection( Guid.NewGuid() );
		var system = Rinten.Networking.System;

		system.OnConnected( connection );
		system.AddConnection( connection, UserInfo.Local );

		connection.State = Connection.ChannelState.Connected;
	}
}
