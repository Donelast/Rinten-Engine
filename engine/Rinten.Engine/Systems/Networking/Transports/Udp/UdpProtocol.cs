using System.Buffers.Binary;

namespace Rinten.Network;

//Packet header: sequence, latest seen remote sequence, and an ack bitfield for the 32 before it
internal static class UdpProtocol
{
	//Twelve hundred bytes, which fits inside the smallest path anybody actually has after tunnels and headers. A packet that has to be fragmented by the network is a packet that is lost whole when any piece of it is.
	/// <summary>How much of a packet is ours to fill.</summary>
	public const int MaxPacket = 1200;

	public const int HeaderSize = 20;

	public static int MaxPayload => MaxPacket - HeaderSize;

	/// <summary>What a packet is for.</summary>
	public enum Kind : byte
	{
		/// <summary>A client asking to be let in.</summary>
		Connect = 0,

		/// <summary>And being let in, with the number it will be known by.</summary>
		Accept = 1,

		/// <summary>Somebody's message, or a piece of one.</summary>
		Payload = 2,

		/// <summary>Going away, said once and not repeated.</summary>
		Disconnect = 3,

		//A connection with nothing going over it is one that cannot tell the difference between quiet and gone. This is how it can.
		/// <summary>Nothing to say, said anyway.</summary>
		Ping = 4,
	}

	[Flags]
	public enum Flags : byte
	{
		None = 0,

		/// <summary>Say it again until it is heard.</summary>
		Reliable = 1,

		/// <summary>One piece of something too big for a packet.</summary>
		Fragment = 2,
	}

	/// <summary>The header every packet starts with.</summary>
	public struct Header
	{
		public Kind Kind;
		public uint Connection;

		public ushort Sequence;

		/// <summary>The newest packet seen coming the other way.</summary>
		public ushort Ack;

		/// <summary>And which of the thirty-two before it arrived.</summary>
		public uint AckBits;

		public Flags Flags;

		/// <summary>Which message this is, or is part of.</summary>
		public ushort Message;

		public ushort Fragment;
		public ushort Fragments;
	}

	public static void Write( Span<byte> into, in Header header )
	{
		into[0] = (byte)header.Kind;
		into[1] = (byte)header.Flags;

		BinaryPrimitives.WriteUInt32LittleEndian( into[2..], header.Connection );
		BinaryPrimitives.WriteUInt16LittleEndian( into[6..], header.Sequence );
		BinaryPrimitives.WriteUInt16LittleEndian( into[8..], header.Ack );
		BinaryPrimitives.WriteUInt32LittleEndian( into[10..], header.AckBits );
		BinaryPrimitives.WriteUInt16LittleEndian( into[14..], header.Message );
		BinaryPrimitives.WriteUInt16LittleEndian( into[16..], header.Fragment );
		BinaryPrimitives.WriteUInt16LittleEndian( into[18..], header.Fragments );
	}

	public static bool Read( ReadOnlySpan<byte> from, out Header header )
	{
		header = default;

		if ( from.Length < HeaderSize ) return false;

		header.Kind = (Kind)from[0];
		header.Flags = (Flags)from[1];

		header.Connection = BinaryPrimitives.ReadUInt32LittleEndian( from[2..] );
		header.Sequence = BinaryPrimitives.ReadUInt16LittleEndian( from[6..] );
		header.Ack = BinaryPrimitives.ReadUInt16LittleEndian( from[8..] );
		header.AckBits = BinaryPrimitives.ReadUInt32LittleEndian( from[10..] );
		header.Message = BinaryPrimitives.ReadUInt16LittleEndian( from[14..] );
		header.Fragment = BinaryPrimitives.ReadUInt16LittleEndian( from[16..] );
		header.Fragments = BinaryPrimitives.ReadUInt16LittleEndian( from[18..] );

		return true;
	}

	/// <summary>Whether one sequence number is newer than another.</summary>
	public static bool Newer( ushort sequence, ushort than )
		=> ( (ushort)( sequence - than ) ) < 0x8000 && sequence != than;
}
