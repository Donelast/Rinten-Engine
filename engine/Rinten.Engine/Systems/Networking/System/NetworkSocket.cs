namespace Rinten.Network;

public abstract class NetworkSocket
{
	internal Action<Connection> OnClientConnect;
	internal Action<Connection> OnClientDisconnect;
	internal Action<(Connection previous, Connection current)> OnHostChanged;

	//Whether this socket should be disposed automatically when the network system it belongs to is disconnected.
	internal bool AutoDispose { get; set; } = true;

	internal abstract void Dispose();
	internal abstract void GetIncomingMessages( NetworkSystem.MessageHandler handler );

	//This is called on a worker thread and should handle any threaded processing of messages.
	internal abstract void ProcessMessagesInThread();

	//Called when everything has just been hooked up to the network system.
	internal virtual void Initialize( NetworkSystem networkSystem )
	{
		//If the socket already has connections, we should call OnClientConnect here to add them to the network system
	}

	//ConnectionInfo table has been updated
	internal virtual void OnConnectionInfoUpdated( NetworkSystem networkSystem )
	{

	}

	//Set data about this socket. For example, this might be used to change whether a lobby should be visible for players depending on the game state.
	internal virtual void SetData( string key, string value )
	{

	}

	//Set the name of the server. This will be displayed to other players when they query servers.
	internal virtual void SetServerName( string name )
	{

	}

	//Set the current map name. This will be displayed to other players when they query servers.
	internal virtual void SetMapName( string name )
	{

	}

	//Called once a second
	internal virtual void Tick( NetworkSystem networkSystem )
	{

	}
}

