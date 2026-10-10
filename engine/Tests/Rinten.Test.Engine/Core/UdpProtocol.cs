using System;
using System.Collections.Generic;
using System.Net;
using Rinten.Network;

namespace EngineTests;

//Our UDP protocol over in-memory peers so tests can drop exactly the packets they choose
[TestClass]
public class UdpProtocolTest
{
	private static readonly IPEndPoint Somewhere = new( IPAddress.Loopback, 27015 );
	private static readonly IPEndPoint Elsewhere = new( IPAddress.Loopback, 27016 );

	//Two peers, and a wire between them that can be told to lose things.
	private sealed class Wire
	{
		public readonly UdpPeer A = new( Elsewhere, 1 );
		public readonly UdpPeer B = new( Somewhere, 1 );

		//Packets to drop, by how many have gone past.
		public readonly HashSet<int> Lose = new();

		private int sent;

		//Move both ends along and carry what they have to say, in both directions - which is what a socket does every frame.
		public void Pump( int times = 1 )
		{
			for ( var i = 0; i < times; i++ )
			{
				Carry( A, B );
				Carry( B, A );

				A.Tick();
				B.Tick();

				Carry( A, B );
				Carry( B, A );
			}
		}

		private void Carry( UdpPeer from, UdpPeer to )
		{
			while ( from.Outgoing.TryDequeue( out var packet ) )
			{
				//Counted here because this wire is standing in for the socket, and it is the socket that knows a packet actually went.
				from.Sent( packet.Length );

				var index = sent++;

				if ( Lose.Contains( index ) ) continue;
				if ( !UdpProtocol.Read( packet, out var header ) ) continue;

				to.Receive( header, packet.AsSpan( UdpProtocol.HeaderSize ) );
			}
		}
	}

	private static byte[] Message( int size, byte seed )
	{
		var data = new byte[size];
		for ( var i = 0; i < size; i++ ) data[i] = (byte)( seed + i );

		return data;
	}

	[TestMethod]
	public void AMessageArrives()
	{
		var wire = new Wire();

		wire.A.Send( Message( 32, 1 ), reliable: true );
		wire.Pump();

		Assert.IsTrue( wire.B.Received.TryDequeue( out var got ), "it should have arrived" );
		CollectionAssert.AreEqual( Message( 32, 1 ), got );
	}

	[TestMethod]
	public void ReliableMessagesArriveInOrder()
	{
		var wire = new Wire();

		for ( var i = 0; i < 8; i++ ) wire.A.Send( Message( 16, (byte)i ), reliable: true );

		wire.Pump();

		for ( var i = 0; i < 8; i++ )
		{
			Assert.IsTrue( wire.B.Received.TryDequeue( out var got ), $"message {i} should have arrived" );
			CollectionAssert.AreEqual( Message( 16, (byte)i ), got, $"message {i} is out of order" );
		}
	}

	//A reliable message that goes missing is said again, and the ones behind it wait rather than overtaking it.
	[TestMethod]
	public void AReliableMessageThatIsLostIsSaidAgain()
	{
		var wire = new Wire();

		//Lose the first packet that goes over the wire.
		wire.Lose.Add( 0 );

		wire.A.Send( Message( 16, 1 ), reliable: true );
		wire.A.Send( Message( 16, 2 ), reliable: true );

		wire.Pump();

		//The second arrived and is holding, because the first is the one that should come out first.
		Assert.IsFalse( wire.B.Received.TryDequeue( out _ ), "the one behind it should be waiting its turn" );

		//Long enough for the sender to give up waiting for an acknowledgement.
		WaitFor( 0.15f );

		wire.A.Tick();
		wire.Pump();

		Assert.IsTrue( wire.B.Received.TryDequeue( out var first ), "it should have been said again" );
		CollectionAssert.AreEqual( Message( 16, 1 ), first );

		Assert.IsTrue( wire.B.Received.TryDequeue( out var second ), "and then the one that was waiting" );
		CollectionAssert.AreEqual( Message( 16, 2 ), second );

		//And once it has been heard, nothing is left to say.
		wire.Pump();
		Assert.AreEqual( 0, wire.A.Unacknowledged, "nothing should still be waiting for an acknowledgement" );
	}

	//Something sent unreliably is wanted now or not at all: what is lost is lost, and what follows does not wait for it.
	[TestMethod]
	public void AnUnreliableMessageThatIsLostStaysLost()
	{
		var wire = new Wire();

		wire.Lose.Add( 0 );

		wire.A.Send( Message( 16, 1 ), reliable: false );
		wire.A.Send( Message( 16, 2 ), reliable: false );

		wire.Pump();

		Assert.IsTrue( wire.B.Received.TryDequeue( out var got ), "the second one should not have waited" );
		CollectionAssert.AreEqual( Message( 16, 2 ), got );

		WaitFor( 0.15f );

		wire.A.Tick();
		wire.Pump();

		Assert.IsFalse( wire.B.Received.TryDequeue( out _ ), "and the first is not coming" );
	}

	//A message too big for one datagram goes in pieces and arrives whole.
	[TestMethod]
	public void ABigMessageGoesInPieces()
	{
		var wire = new Wire();

		//Several packets' worth, and not a round number of them.
		var big = Message( ( UdpProtocol.MaxPayload * 3 ) + 17, 7 );

		wire.A.Send( big, reliable: true );
		wire.Pump();

		Assert.IsTrue( wire.B.Received.TryDequeue( out var got ), "it should have been put back together" );
		CollectionAssert.AreEqual( big, got );
	}

	//And one whose middle went missing waits for that piece.
	[TestMethod]
	public void ABigMessageWaitsForThePieceThatWasLost()
	{
		var wire = new Wire();

		wire.Lose.Add( 1 );

		var big = Message( UdpProtocol.MaxPayload * 3, 9 );

		wire.A.Send( big, reliable: true );
		wire.Pump();

		Assert.IsFalse( wire.B.Received.TryDequeue( out _ ), "a piece is missing, so there is nothing to hand over" );

		WaitFor( 0.15f );

		wire.A.Tick();
		wire.Pump();

		Assert.IsTrue( wire.B.Received.TryDequeue( out var got ), "and now it is whole" );
		CollectionAssert.AreEqual( big, got );
	}

	//A late fragment of an unreliable message is dropped; slow because the give-up timeout is 5s
	[TestMethod]
	public void APieceThatComesTooLateIsNotWaitedFor()
	{
		var peer = new UdpPeer( Somewhere, 1 );

		var piece = Message( 64, 3 );

		void Give( ushort fragment )
		{
			var header = new UdpProtocol.Header
			{
				Kind = UdpProtocol.Kind.Payload,
				Sequence = fragment,
				Flags = UdpProtocol.Flags.Fragment,
				Message = 0,
				Fragment = fragment,
				Fragments = 3,
			};

			peer.Receive( header, piece );
		}

		//Two of three, and the one in the middle never comes.
		Give( 0 );
		Give( 2 );

		Assert.IsFalse( peer.Received.TryDequeue( out _ ), "a piece is missing" );

		WaitFor( 5.1f );

		peer.Tick();

		//And now it comes, far too late to be of use to anybody.
		Give( 1 );

		Assert.IsFalse( peer.Received.TryDequeue( out _ ), "it should have been given up on" );
	}

	//Sequence numbers wrap, and a wrapped one is newer rather than sixty-five thousand older.
	[TestMethod]
	public void SequenceNumbersWrapAround()
	{
		Assert.IsTrue( UdpProtocol.Newer( 1, 0 ) );
		Assert.IsFalse( UdpProtocol.Newer( 0, 1 ) );

		Assert.IsTrue( UdpProtocol.Newer( 0, ushort.MaxValue ), "the one after the last is the next" );
		Assert.IsFalse( UdpProtocol.Newer( ushort.MaxValue, 0 ) );

		Assert.IsFalse( UdpProtocol.Newer( 5, 5 ), "the same packet is not newer than itself" );
	}

	[TestMethod]
	public void AHeaderSurvivesBeingWrittenAndRead()
	{
		var written = new UdpProtocol.Header
		{
			Kind = UdpProtocol.Kind.Payload,
			Connection = 0xDEADBEEF,
			Sequence = 4242,
			Ack = 1234,
			AckBits = 0xF0F0F0F0,
			Flags = UdpProtocol.Flags.Reliable | UdpProtocol.Flags.Fragment,
			Message = 77,
			Fragment = 3,
			Fragments = 9,
		};

		var packet = new byte[UdpProtocol.HeaderSize];
		UdpProtocol.Write( packet, written );

		Assert.IsTrue( UdpProtocol.Read( packet, out var read ) );

		Assert.AreEqual( written.Kind, read.Kind );
		Assert.AreEqual( written.Connection, read.Connection );
		Assert.AreEqual( written.Sequence, read.Sequence );
		Assert.AreEqual( written.Ack, read.Ack );
		Assert.AreEqual( written.AckBits, read.AckBits );
		Assert.AreEqual( written.Flags, read.Flags );
		Assert.AreEqual( written.Message, read.Message );
		Assert.AreEqual( written.Fragment, read.Fragment );
		Assert.AreEqual( written.Fragments, read.Fragments );

		Assert.IsFalse( UdpProtocol.Read( new byte[4], out _ ), "half a header is not a packet" );
	}

	//What the overlays are shown: how much went each way, how long a round trip took, and how much of it arrived.
	[TestMethod]
	public void AConnectionSaysWhatItIsDoing()
	{
		var wire = new Wire();

		//Nothing is reported before there is a whole second to report on.
		Assert.AreEqual( 0, wire.A.Stats.Ping );

		for ( var i = 0; i < 20; i++ )
		{
			wire.A.Send( Message( 200, (byte)i ), reliable: true );
			wire.Pump();
		}

		WaitFor( 1.05f );

		wire.A.Tick();
		wire.B.Tick();
		wire.Pump();

		var sending = wire.A.Stats;
		var hearing = wire.B.Stats;

		Assert.IsTrue( sending.OutBytesPerSecond > 0.0f, "the sender should have sent something" );
		Assert.IsTrue( hearing.InBytesPerSecond > 0.0f, "and the far end should have heard it" );

		//And heard all of it: what one end put on the wire is what the other took off it. Compared against each other rather than against a figure,
		//because a rate is over however long the second actually took, and on a machine running the rest of the tests that is not one second.
		Assert.AreEqual( sending.OutBytesPerSecond, hearing.InBytesPerSecond, sending.OutBytesPerSecond * 0.2f,
			$"sent {sending.OutBytesPerSecond}/s, heard {hearing.InBytesPerSecond}/s" );

		//Nothing was lost on a wire that was told to lose nothing.
		Assert.IsTrue( hearing.ConnectionQuality > 0.9f, $"quality should be high - {hearing.ConnectionQuality}" );

		//The round trip on a wire with no time in it is nothing, and nothing is not a missing measurement.
		Assert.IsTrue( sending.Ping >= 0 );
	}

	//Somewhere for a test to wait without waiting on anything real.
	private static void WaitFor( float seconds )
		=> System.Threading.Thread.Sleep( (int)( seconds * 1000.0f ) );
}
