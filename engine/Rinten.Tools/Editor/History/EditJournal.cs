using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Editor;

//Append-only on-disk log of a document's edits, used for both crash safety and undo
internal sealed class EditJournal : IDisposable
{
	//What every one of these files starts with.
	private static readonly byte[] Magic = "RJRN"u8.ToArray();

	//What each record starts with, so a torn one can be told from a real one.
	private const uint RecordMarker = 0x52454344; //"RECD"

	private const uint Version = 1;

	/// <summary>Where this journal is.</summary>
	public string Path { get; }

	/// <summary>The document this journal was built on, by its hash.</summary>
	public ulong BaseHash { get; private set; }

	/// <summary>Whether the journal on the disk was built on the document we have.</summary>
	public bool BaseMatches { get; private set; }

	/// <summary>How many whole records the journal holds.</summary>
	public int Count { get; private set; }

	/// <summary>How many bytes of it there are, header and all.</summary>
	public long Bytes => stream?.Length ?? 0;

	private FileStream stream;

	private EditJournal( string path )
	{
		Path = path;
	}

	//A journal built on a different version of the document is not opened for writing and reports BaseMatches false: what to do about it - show it to somebody, keep it aside, throw it away - is not a decision to make down here.
	/// <summary>Opens the journal for a document, or starts one.</summary>
	public static EditJournal Open( string path, ulong baseHash )
	{
		var journal = new EditJournal( path );

		try
		{
			var directory = System.IO.Path.GetDirectoryName( path );

			if ( !string.IsNullOrEmpty( directory ) ) Directory.CreateDirectory( directory );

			journal.stream = new FileStream( path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read );

			if ( journal.stream.Length == 0 )
			{
				journal.WriteHeader( baseHash );
				journal.BaseMatches = true;
				return journal;
			}

			journal.ReadHeader();
			journal.BaseMatches = journal.BaseHash == baseHash;

			journal.Count = journal.CountRecords();

			return journal;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Could not open the edit journal at {path}" );

			journal.Dispose();

			return null;
		}
	}

	//Pushed to the disk before this returns. A record that is in the operating system's cache and not on the platter is a record that a power cut takes, and the whole point of this file is that a power cut takes nothing.
	/// <summary>Adds an edit to the end.</summary>
	public bool Append( ReadOnlySpan<byte> payload )
	{
		if ( stream is null ) return false;

		try
		{
			stream.Seek( 0, SeekOrigin.End );

			Span<byte> head = stackalloc byte[16];

			Write( head[..4], RecordMarker );
			Write( head[4..8], (uint)payload.Length );
			Write( head[8..12], (uint)(Count + 1) );
			Write( head[12..16], Crc32( payload ) );

			stream.Write( head );
			stream.Write( payload );

			//Out of this process and into the operating system's hands, which is cheap and is what protects a
			//record from the editor crashing. What is not done here is pushing it onto the platter: see Sync.
			stream.Flush();

			Count++;

			SyncSoon();

			return true;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Could not write to the edit journal at {Path}" );

			return false;
		}
	}

	/// <summary>Adds an edit written as text.</summary>
	public bool Append( string payload ) => Append( Encoding.UTF8.GetBytes( payload ?? string.Empty ) );

	//Max time between disk syncs; syncing every record stalled each edit, and crashes lose nothing
	private const double SecondsBetweenSyncs = 1.0;

	private long lastSync;

	//Pushes what has been written onto the disk, but not too often.
	private void SyncSoon()
	{
		var now = Stopwatch.GetTimestamp();

		if ( lastSync != 0 && Stopwatch.GetElapsedTime( lastSync, now ).TotalSeconds < SecondsBetweenSyncs )
			return;

		lastSync = now;

		Sync();
	}

	//Called where the records have to have landed before the next thing happens: a checkpoint that is about to say the file holds them, and the journal being closed.
	/// <summary>Pushes everything written so far onto the disk, and waits for it.</summary>
	public void Sync()
	{
		if ( stream is null ) return;

		try
		{
			stream.Flush( flushToDisk: true );

			lastSync = Stopwatch.GetTimestamp();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Could not flush the edit journal at {Path}" );
		}
	}

	/// <summary>Every whole record in the journal, oldest first.</summary>
	public List<byte[]> Read()
	{
		var records = new List<byte[]>();

		if ( stream is null ) return records;

		try
		{
			stream.Seek( HeaderLength(), SeekOrigin.Begin );

			while ( true )
			{
				var record = ReadRecord();

				if ( record is null ) break;

				records.Add( record );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Could not read the edit journal at {Path}" );
		}

		return records;
	}

	//Only ever called after that version has been written and flushed. Until then the records are the only place the work exists, and a journal emptied a moment too early is exactly the thing this file is here to prevent.
	/// <summary>Throws the records away and starts again from a new version of the document.</summary>
	public bool Rebase( ulong baseHash )
	{
		if ( stream is null ) return false;

		try
		{
			stream.SetLength( 0 );

			WriteHeader( baseHash );

			Count = 0;
			BaseMatches = true;

			return true;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Could not rebase the edit journal at {Path}" );

			return false;
		}
	}

	public void Dispose()
	{
		//Whatever the last second's records were, onto the disk before the handle goes - see SyncSoon for why they might not be there yet.
		Sync();

		stream?.Dispose();
		stream = null;
	}

	//The file itself.

	private long HeaderLength() => Magic.Length + 4 + 8;

	private void WriteHeader( ulong baseHash )
	{
		BaseHash = baseHash;

		stream.Seek( 0, SeekOrigin.Begin );

		Span<byte> header = stackalloc byte[16];

		Magic.CopyTo( header );
		Write( header[4..8], Version );
		Write( header[8..16], baseHash );

		stream.Write( header );
		stream.Flush( flushToDisk: true );
	}

	private void ReadHeader()
	{
		stream.Seek( 0, SeekOrigin.Begin );

		Span<byte> header = stackalloc byte[16];

		if ( stream.Read( header ) != header.Length )
			throw new InvalidDataException( "the journal is too short to have a header" );

		for ( var i = 0; i < Magic.Length; i++ )
		{
			if ( header[i] != Magic[i] )
				throw new InvalidDataException( "the journal does not start like one" );
		}

		var version = ReadUInt( header[4..8] );

		if ( version != Version )
			throw new InvalidDataException( $"the journal is version {version} and this is version {Version}" );

		BaseHash = ReadULong( header[8..16] );
	}

	//Reads the next record, or nothing where the journal ends.
	private byte[] ReadRecord()
	{
		Span<byte> head = stackalloc byte[16];

		if ( stream.Read( head ) != head.Length ) return null;

		if ( ReadUInt( head[..4] ) != RecordMarker ) return null;

		var length = ReadUInt( head[4..8] );

		//A length that runs past the end of the file is the tail of a write that did not finish.
		if ( stream.Position + length > stream.Length ) return null;

		var payload = new byte[length];

		if ( stream.Read( payload ) != payload.Length ) return null;

		if ( Crc32( payload ) != ReadUInt( head[12..16] ) ) return null;

		return payload;
	}

	private int CountRecords()
	{
		var count = 0;

		stream.Seek( HeaderLength(), SeekOrigin.Begin );

		while ( ReadRecord() is not null ) count++;

		return count;
	}

	private static void Write( Span<byte> into, uint value )
	{
		into[0] = (byte)(value >> 24);
		into[1] = (byte)(value >> 16);
		into[2] = (byte)(value >> 8);
		into[3] = (byte)value;
	}

	private static void Write( Span<byte> into, ulong value )
	{
		for ( var i = 0; i < 8; i++ ) into[i] = (byte)(value >> (56 - (i * 8)));
	}

	private static uint ReadUInt( ReadOnlySpan<byte> from )
		=> ((uint)from[0] << 24) | ((uint)from[1] << 16) | ((uint)from[2] << 8) | from[3];

	private static ulong ReadULong( ReadOnlySpan<byte> from )
	{
		ulong value = 0;

		for ( var i = 0; i < 8; i++ ) value = (value << 8) | from[i];

		return value;
	}

	//The checksum. Its own rather than a library's: this file is read while the editor is starting, before anything else is up.

	private static readonly uint[] CrcTable = BuildCrcTable();

	private static uint[] BuildCrcTable()
	{
		var table = new uint[256];

		for ( uint i = 0; i < 256; i++ )
		{
			var value = i;

			for ( var bit = 0; bit < 8; bit++ )
			{
				value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
			}

			table[i] = value;
		}

		return table;
	}

	private static uint Crc32( ReadOnlySpan<byte> data )
	{
		var crc = 0xFFFFFFFFu;

		foreach ( var b in data )
		{
			crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
		}

		return crc ^ 0xFFFFFFFFu;
	}
}
