using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Rinten;

/// <summary>Packed resource text (Brotli, our magic header) shipped in builds instead of the source file</summary>
public static class CompiledFile
{
	//'RNRC', little end first.
	private const uint Magic = 0x43524E52;

	private const uint Version = 1;

	private const int HeaderSize = 12;

	/// <summary>Whether these bytes are one of ours.</summary>
	public static bool IsCompiled( ReadOnlySpan<byte> bytes )
		=> bytes.Length >= HeaderSize &&
			BitConverter.ToUInt32( bytes ) == Magic &&
			BitConverter.ToUInt32( bytes[4..] ) == Version;

	/// <summary>Whether the file <paramref name="stream"/> reads is one of ours - out of its header alone.</summary>
	public static bool IsCompiled( Stream stream )
	{
		Span<byte> header = stackalloc byte[HeaderSize];
		return stream.ReadAtLeast( header, HeaderSize, throwOnEndOfStream: false ) == HeaderSize && IsCompiled( header );
	}

	/// <summary>The text packed into a file - what an export writes in place of the source.</summary>
	public static byte[] Pack( string text )
	{
		if ( text is null ) return null;

		var plain = Encoding.UTF8.GetBytes( text );

		using var file = new MemoryStream();

		file.Write( BitConverter.GetBytes( Magic ) );
		file.Write( BitConverter.GetBytes( Version ) );
		file.Write( BitConverter.GetBytes( plain.Length ) );

		//Eleven: this is written once by a build and read by every copy of the game, which is the shape of trade that is worth the seconds.
		var room = new byte[plain.Length + 1024];

		if ( BrotliEncoder.TryCompress( plain, room, out var written, 11, 22 ) )
		{
			file.Write( room, 0, written );
		}
		else
		{
			using var packing = new BrotliStream( file, CompressionLevel.Optimal, leaveOpen: true );

			packing.Write( plain, 0, plain.Length );
		}

		return file.ToArray();
	}

	/// <summary>The text back out of one, or null where these bytes are not one of ours.</summary>
	public static string Unpack( ReadOnlySpan<byte> bytes )
	{
		if ( !IsCompiled( bytes ) ) return null;

		var length = BitConverter.ToInt32( bytes[8..] );

		if ( length < 0 || length > 1 << 30 ) return null;

		var plain = ArrayPool<byte>.Shared.Rent( length );

		try
		{
			var packed = bytes[HeaderSize..].ToArray();

			using var input = new MemoryStream( packed, writable: false );
			using var unpacking = new BrotliStream( input, CompressionMode.Decompress );

			unpacking.ReadExactly( plain, 0, length );

			//A byte order mark, where whatever packed this kept one. Every reader of text in the framework takes it off, so a shader packed
			//with one and read back with it is a different text from the one it was - and a different key for everything compiled out of it.
			var at = length >= 3 && plain[0] == 0xEF && plain[1] == 0xBB && plain[2] == 0xBF ? 3 : 0;

			return Encoding.UTF8.GetString( plain, at, length - at );
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or EndOfStreamException )
		{
			return null;
		}
		finally
		{
			ArrayPool<byte>.Shared.Return( plain );
		}
	}

	/// <summary>Text of a possibly packed file: the source if present, else the compiled file beside it</summary>
	public static string TextOf( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return null;

		try
		{
			//Handed the packed name itself - which is what a build's own lookups find, since that is the only file there.
			//Unpacked, or read as it is where it turns out to be somebody else's compiled file under the same name.
			if ( path.EndsWith( "_c", StringComparison.Ordinal ) )
			{
				var bytes = SealedFile.ReadAllBytes( path );

				if ( Unpack( bytes ) is { } text ) return text;

				//A compiled file that is not one of ours is whatever it always was, and somebody asked for this one by name: a model's graph is written
				//compiled and only compiled, as text, and reading the source beside it instead - there is none - left every animated thing without a graph.
				return SealedFile.ReadAllText( path );
			}

			if ( File.Exists( path ) ) return SealedFile.ReadAllText( path );

			var compiled = path + "_c";

			if ( !File.Exists( compiled ) ) return null;

			return Unpack( SealedFile.ReadAllBytes( compiled ) );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			return null;
		}
	}

	/// <summary>Whether a file is there under its own name or as the compiled one beside it.</summary>
	public static bool Exists( string path )
		=> !string.IsNullOrEmpty( path ) && ( File.Exists( path ) || File.Exists( path + "_c" ) );
}
