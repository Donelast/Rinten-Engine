namespace Rinten.Network;

//A listen socket over our own UDP. What a server opens. Everything about how a connection works is in
//UdpPeer; this is the part the network system talks to - who has arrived, who has gone, and what they said.
internal sealed class UdpSocket : NetworkSocket, IValid
{
	private static readonly Logger log = new( "Udp" );

	private readonly UdpTransport transport;

	private readonly List<UdpChannel> channels = new();
	private readonly object padlock = new();

	private Action queued;

	public UdpSocket( int port )
	{
		transport = new UdpTransport( port );

		transport.OnConnected = peer =>
		{
			var channel = new UdpChannel( transport, peer, isHost: false );

			lock ( padlock ) channels.Add( channel );

			//Handed over on the main thread rather than from the receive loop: everything above here expects to be told about a connection where it can act on one.
			lock ( padlock ) queued += () => OnClientConnect?.Invoke( channel );
		};

		transport.OnDisconnected = peer =>
		{
			UdpChannel going;

			lock ( padlock )
			{
				going = channels.FirstOrDefault( x => x.Peer == peer );
				if ( going is null ) return;

				channels.Remove( going );
				queued += () => OnClientDisconnect?.Invoke( going );
			}
		};

		log.Info( $"listening on udp {transport.Port}" );
	}

	public int Port => transport.Port;

	public bool IsValid => true;

	public IReadOnlyList<UdpChannel> Channels
	{
		get { lock ( padlock ) return channels.ToArray(); }
	}

	internal override void Dispose()
	{
		transport.Dispose();

		lock ( padlock ) channels.Clear();

		GC.SuppressFinalize( this );
	}

	internal override void ProcessMessagesInThread()
	{
		//Saying again what was not acknowledged, and saying something when there is nothing
		//to say. Cheap, and it has to happen whether or not anything is listening this frame.
		transport.Tick();
	}

	internal override void GetIncomingMessages( NetworkSystem.MessageHandler handler )
	{
		Action pending;

		lock ( padlock )
		{
			pending = queued;
			queued = null;
		}

		try
		{
			pending?.Invoke();
		}
		catch ( Exception e )
		{
			log.Warning( e, $"udp: {e.Message}" );
		}

		foreach ( var channel in Channels )
		{
			channel.InternalRecv( handler );
		}
	}
}
