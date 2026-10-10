namespace Rinten.Network;

//A listen socket opened through a NetworkProvider.
internal sealed class ProviderSocket : NetworkSocket
{
	private static readonly Logger log = new( "NetProvider" );

	private readonly NetworkProvider provider;
	private readonly NetworkListener listener;
	private readonly List<ProviderChannel> channels = new();

	internal ProviderSocket( NetworkProvider provider, NetworkListener listener )
	{
		this.provider = provider;
		this.listener = listener;
	}

	internal override void Initialize( NetworkSystem networkSystem )
	{
		//What the lobby already knows about itself, for a provider that lists it.
		foreach ( var (key, value) in Networking.ServerData )
			listener.SetData( key, value );

		if ( Networking.ServerName is { } name ) listener.SetServerName( name );
		if ( Networking.MapName is { } map ) listener.SetMapName( map );
	}

	internal override void Dispose()
	{
		lock ( channels ) channels.Clear();

		try
		{
			listener.Dispose();
		}
		catch ( Exception e )
		{
			log.Warning( e, $"{provider.Title}: {e.Message}" );
		}
	}

	internal override void ProcessMessagesInThread() { }

	internal override void SetData( string key, string value ) => listener.SetData( key, value );

	internal override void SetServerName( string name ) => listener.SetServerName( name );

	internal override void SetMapName( string name ) => listener.SetMapName( name );

	internal override void Tick( NetworkSystem networkSystem ) => listener.Tick();

	internal override void GetIncomingMessages( NetworkSystem.MessageHandler handler )
	{
		while ( listener.TryAccept( out var peer ) )
		{
			var channel = new ProviderChannel( peer, isHost: false );

			lock ( channels ) channels.Add( channel );

			OnClientConnect?.Invoke( channel );
		}

		ProviderChannel[] snapshot;
		lock ( channels ) snapshot = channels.ToArray();

		foreach ( var channel in snapshot )
		{
			channel.InternalRecv( handler );

			if ( channel.Peer.IsConnected ) continue;

			lock ( channels ) channels.Remove( channel );

			//Let go of what the provider holds for them - a slot in the lobby, a connection handle.
			channel.Peer.Close( 0, channel.Peer.EndReason ?? "disconnected" );

			OnClientDisconnect?.Invoke( channel );
		}
	}
}
