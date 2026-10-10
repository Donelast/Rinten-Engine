using System;
using System.Net;
using System.Threading;
using Rinten.Network;

namespace EngineTests;

//Real UDP sockets on localhost: datagrams pass, connects and disconnects are noticed
[TestClass]
public class UdpSocketTest
{
	//Wait for something to become true, or give up.
	private static bool Until( Func<bool> what, Action pump, float seconds = 3.0f )
	{
		var until = DateTime.UtcNow.AddSeconds( seconds );

		while ( DateTime.UtcNow < until )
		{
			pump();

			if ( what() ) return true;

			Thread.Sleep( 5 );
		}

		return what();
	}

	[TestMethod]
	public void TwoSocketsFindEachOther()
	{
		//Port zero: whatever is free, which is the only thing a test may ask for on a machine it does not own.
		using var server = new UdpTransport( 0 );
		using var client = new UdpTransport( 0 );

		UdpPeer arrived = null;
		server.OnConnected = peer => arrived = peer;

		var host = client.Connect( new IPEndPoint( IPAddress.Loopback, server.Port ) );

		Assert.IsTrue( Until( () => arrived is not null, () => { } ), "the server should have noticed" );

		Assert.IsTrue( Until( () => host.Connection != 0, () => { } ),
			"and the client should have been given a number" );

		//Something across, both ways.
		host.Send( "hello"u8.ToArray(), reliable: true );
		client.Flush( host );

		Assert.IsTrue( Until( () => arrived.Received.Count > 0, () => server.Tick() ), "it should have arrived" );

		Assert.IsTrue( arrived.Received.TryDequeue( out var got ) );
		CollectionAssert.AreEqual( "hello"u8.ToArray(), got );

		arrived.Send( "and back"u8.ToArray(), reliable: true );
		server.Flush( arrived );

		Assert.IsTrue( Until( () => host.Received.Count > 0, () => client.Tick() ), "and the answer too" );

		Assert.IsTrue( host.Received.TryDequeue( out var answer ) );
		CollectionAssert.AreEqual( "and back"u8.ToArray(), answer );
	}

	//Something bigger than a datagram, over a real socket - where the pieces can genuinely arrive out of order.
	[TestMethod]
	public void SomethingBigCrossesInOnePiece()
	{
		using var server = new UdpTransport( 0 );
		using var client = new UdpTransport( 0 );

		UdpPeer arrived = null;
		server.OnConnected = peer => arrived = peer;

		var host = client.Connect( new IPEndPoint( IPAddress.Loopback, server.Port ) );

		Assert.IsTrue( Until( () => arrived is not null, () => { } ) );

		var big = new byte[UdpProtocol.MaxPayload * 5 + 123];
		for ( var i = 0; i < big.Length; i++ ) big[i] = (byte)( i * 31 );

		host.Send( big, reliable: true );
		client.Flush( host );

		Assert.IsTrue( Until( () => arrived.Received.Count > 0, () =>
		{
			server.Tick();
			client.Tick();
		} ), "it should have been put back together" );

		Assert.IsTrue( arrived.Received.TryDequeue( out var got ) );
		CollectionAssert.AreEqual( big, got );
	}

	//Saying goodbye is noticed at the other end.
	[TestMethod]
	public void GoingAwayIsNoticed()
	{
		using var server = new UdpTransport( 0 );
		using var client = new UdpTransport( 0 );

		UdpPeer arrived = null;
		UdpPeer left = null;

		server.OnConnected = peer => arrived = peer;
		server.OnDisconnected = peer => left = peer;

		var host = client.Connect( new IPEndPoint( IPAddress.Loopback, server.Port ) );

		Assert.IsTrue( Until( () => arrived is not null, () => { } ) );

		client.Close( host );

		Assert.IsTrue( Until( () => left is not null, () => server.Tick() ), "the server should have been told" );
		Assert.AreSame( arrived, left );
	}

	//The request to be let in is a datagram like any other, and the one packet nothing else can make up for. So it is asked again.
	[TestMethod]
	public void AConnectRequestThatIsIgnoredIsAskedAgain()
	{
		//A server that hears and says nothing, which is what a lost request looks like from the other side.
		using var deaf = new System.Net.Sockets.UdpClient( 0 );
		var deafPort = ( (IPEndPoint)deaf.Client.LocalEndPoint ).Port;

		using var client = new UdpTransport( 0 );

		var peer = client.Connect( new IPEndPoint( IPAddress.Loopback, deafPort ) );

		var from = new IPEndPoint( IPAddress.Any, 0 );

		deaf.Client.ReceiveTimeout = 3000;

		var first = deaf.Receive( ref from );

		Assert.IsTrue( UdpProtocol.Read( first, out var asked ) );
		Assert.AreEqual( UdpProtocol.Kind.Connect, asked.Kind );

		//Nothing is said back, and it should be asked again.
		Assert.IsTrue( Until( () =>
		{
			if ( deaf.Available <= 0 ) return false;

			var again = deaf.Receive( ref from );

			return UdpProtocol.Read( again, out var header ) && header.Kind == UdpProtocol.Kind.Connect;
		}, () => client.Tick() ), "it should have asked again" );

		//And once it is answered, it stops asking and has a number.
		var accept = new byte[UdpProtocol.HeaderSize];

		UdpProtocol.Write( accept, new UdpProtocol.Header
		{
			Kind = UdpProtocol.Kind.Accept,
			Connection = 7,
		} );

		deaf.Send( accept, accept.Length, from );

		Assert.IsTrue( Until( () => peer.Connection == 7, () => client.Tick() ), "it should have been let in" );
	}

	//A server that closes a connection itself forgets it, the same as one that went away on its own.
	[TestMethod]
	public void AConnectionTheServerClosesIsForgotten()
	{
		var listener = new UdpSocket( 0 );

		using var client = new UdpTransport( 0 );

		try
		{
			client.Connect( new IPEndPoint( IPAddress.Loopback, listener.Port ) );

			Assert.IsTrue( Until( () => listener.Channels.Count > 0, () =>
			{
				listener.ProcessMessagesInThread();
				listener.GetIncomingMessages( _ => { } );
			} ), "the connection should have arrived" );

			var channel = listener.Channels[0];
			channel.Close( 0, "Kicked" );

			listener.GetIncomingMessages( _ => { } );

			Assert.AreEqual( 0, listener.Channels.Count, "and the socket should not still be holding it" );
		}
		finally
		{
			listener.Dispose();
		}
	}

	//What a game is told to connect to, as an address.
	[TestMethod]
	public void AddressesAreReadTheWayAGameWritesThem()
	{
		var withPort = UdpConnection.Parse( "udp://127.0.0.1:27015" );

		Assert.AreEqual( "127.0.0.1", withPort.Address.ToString() );
		Assert.AreEqual( 27015, withPort.Port );

		//Localhost is a name everybody writes and nobody resolves.
		var local = UdpConnection.Parse( "udp://localhost:27016" );

		Assert.AreEqual( "127.0.0.1", local.Address.ToString() );
		Assert.AreEqual( 27016, local.Port );

		//No port is the port a server listens on.
		var plain = UdpConnection.Parse( "127.0.0.1" );

		Assert.AreEqual( Networking.Port, plain.Port );
	}

	//A listen socket hands new connections to whoever is listening.
	[TestMethod]
	public void AListenSocketReportsWhoArrives()
	{
		var listener = new UdpSocket( 0 );

		using var client = new UdpTransport( 0 );

		Connection connected = null;
		listener.OnClientConnect = c => connected = c;

		try
		{
			client.Connect( new IPEndPoint( IPAddress.Loopback, listener.Port ) );

			Assert.IsTrue( Until( () => connected is not null, () =>
			{
				//What the network system does every frame: move the sockets along, then ask them what happened.
				listener.ProcessMessagesInThread();
				listener.GetIncomingMessages( _ => { } );
			} ), "the listener should have handed the connection over" );

			Assert.IsTrue( connected.Address.Contains( "127.0.0.1" ), $"and said where from - {connected.Address}" );
		}
		finally
		{
			listener.Dispose();
		}
	}
}
