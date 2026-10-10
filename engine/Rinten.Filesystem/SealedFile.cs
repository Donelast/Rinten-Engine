using System;
using System.IO;
using System.Text;

namespace Rinten;

/// <summary>Seals shipped assets against casual extraction, not determined attackers; unsealed pass through</summary>
public static class SealedFile
{
	//'RNSL', little end first.
	private const uint Magic = 0x4C534E52;

	private const uint Version = 1;

	//Magic, version, salt, how many bytes are scrambled.
	internal const int HeaderSize = 16;

	//Only the first megabyte is scrambled; enough to break format headers, cheap to unseal
	private const int Scrambles = 1024 * 1024;

	/// <summary>Whether these bytes begin with a seal. Four bytes read, which is what every read in the engine pays for the whole mechanism.</summary>
	public static bool IsSealed( ReadOnlySpan<byte> bytes )
		=> bytes.Length >= HeaderSize &&
			BitConverter.ToUInt32( bytes ) == Magic &&
			BitConverter.ToUInt32( bytes[4..] ) == Version;

	/// <summary>The same bytes, sealed - what an export writes in place of the file.</summary>
	public static byte[] Seal( byte[] plain )
	{
		if ( plain is null ) return null;

		//Already sealed: an export of an export, or a file somebody copied out of a build. Sealing it twice would only cost a reader a second pass.
		if ( IsSealed( plain ) ) return plain;

		var salt = (uint)Random.Shared.Next( 1, int.MaxValue );
		var scrambled = Math.Min( plain.Length, Scrambles );
		var file = new byte[HeaderSize + plain.Length];

		BitConverter.GetBytes( Magic ).CopyTo( file, 0 );
		BitConverter.GetBytes( Version ).CopyTo( file, 4 );
		BitConverter.GetBytes( salt ).CopyTo( file, 8 );
		BitConverter.GetBytes( scrambled ).CopyTo( file, 12 );

		plain.CopyTo( file, HeaderSize );

		Scramble( file.AsSpan( HeaderSize, scrambled ), salt );

		return file;
	}

	/// <summary>The bytes a reader wants: unsealed where they were sealed, and themselves where they were not.</summary>
	public static byte[] Open( byte[] bytes )
	{
		if ( !IsSealed( bytes ) ) return bytes;

		var scrambled = BitConverter.ToInt32( bytes, 12 );
		var salt = BitConverter.ToUInt32( bytes, 8 );
		var length = bytes.Length - HeaderSize;

		if ( scrambled < 0 || scrambled > length ) return bytes;

		var plain = new byte[length];

		Array.Copy( bytes, HeaderSize, plain, 0, length );
		Scramble( plain.AsSpan( 0, scrambled ), salt );

		return plain;
	}

	//Sixteen bytes are read to find out, and a file that is not sealed is wound back and handed over as it was - so an unsealed build reads through this at the cost of one seek.
	/// <summary>The same for a stream: the file's own stream where it is not sealed, and what was in it where it is.</summary>
	public static Stream Open( Stream stream )
	{
		if ( stream is null ) return null;

		var header = new byte[HeaderSize];
		var read = 0;

		try
		{
			while ( read < HeaderSize )
			{
				var got = stream.Read( header, read, HeaderSize - read );
				if ( got <= 0 ) break;

				read += got;
			}
		}
		catch ( IOException )
		{
			return stream;
		}

		if ( read < HeaderSize || !IsSealed( header ) )
		{
			//Not one of ours. Wound back where the stream allows it, and read again from the start where it does not.
			if ( stream.CanSeek )
			{
				stream.Position = 0;
				return stream;
			}

			var rest = new MemoryStream();

			rest.Write( header, 0, read );
			stream.CopyTo( rest );
			stream.Dispose();

			rest.Position = 0;

			return rest;
		}

		var scrambled = BitConverter.ToInt32( header, 12 );
		var salt = BitConverter.ToUInt32( header, 8 );

		var body = new MemoryStream();

		stream.CopyTo( body );
		stream.Dispose();

		var plain = body.GetBuffer();
		var length = (int)body.Length;

		if ( scrambled > 0 && scrambled <= length ) Scramble( plain.AsSpan( 0, scrambled ), salt );

		return new MemoryStream( plain, 0, length, writable: false );
	}

	/// <summary>A file read and opened - what File.ReadAllBytes is where a build may have sealed it.</summary>
	public static byte[] ReadAllBytes( string path ) => Open( File.ReadAllBytes( path ) );

	/// <summary>A file opened for reading - what File.OpenRead is where a build may have sealed it.</summary>
	public static Stream OpenRead( string path ) => Open( File.OpenRead( path ) );

	//Read once either way, and decoded through the same reader the framework's own ReadAllText uses - so a file with a byte order mark, or one written in UTF-16, reads here exactly as it read before.
	/// <summary>A file read and opened as text - what File.ReadAllText is where a build may have sealed it.</summary>
	public static string ReadAllText( string path )
	{
		var plain = Open( File.ReadAllBytes( path ) );

		using var bytes = new MemoryStream( plain, writable: false );
		using var reader = new StreamReader( bytes, Encoding.UTF8, detectEncodingFromByteOrderMarks: true );

		return reader.ReadToEnd();
	}

	//The scramble, both ways - it is its own inverse. A keystream out of the salt and a number written here, eight bytes at a time: about
	//as fast as memory goes, which is the point. It is not cryptography and is not meant to be read as any - see the note above the class.
	private static void Scramble( Span<byte> bytes, uint salt )
	{
		var state = 0x9E3779B97F4A7C15UL ^ ( (ulong)salt * 0xD1B54A32D192ED03UL );

		var whole = bytes.Length / 8 * 8;

		for ( var i = 0; i < whole; i += 8 )
		{
			state ^= state << 13;
			state ^= state >> 7;
			state ^= state << 17;

			var word = BitConverter.ToUInt64( bytes[i..] ) ^ state;

			BitConverter.TryWriteBytes( bytes[i..], word );
		}

		if ( whole == bytes.Length ) return;

		state ^= state << 13;
		state ^= state >> 7;
		state ^= state << 17;

		for ( var i = whole; i < bytes.Length; i++ )
		{
			bytes[i] ^= (byte)( state >> ( ( i - whole ) * 8 ) );
		}
	}
}
