using System.Buffers.Binary;
using System.Text;

namespace Rinten.Audio;

//Reads title/artist/album tags from ID3v2 and Vorbis comments; keys lower case, never throws
internal static class SoundTags
{
	public static Dictionary<string, string> Read( ReadOnlySpan<byte> file )
	{
		var tags = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		try
		{
			if ( file.Length >= 10 && file[0] == 'I' && file[1] == 'D' && file[2] == '3' ) Id3( file, tags );
			else if ( file.Length >= 4 && file[0] == 'f' && file[1] == 'L' && file[2] == 'a' && file[3] == 'C' ) Flac( file, tags );
			else if ( file.Length >= 4 && file[0] == 'O' && file[1] == 'g' && file[2] == 'g' && file[3] == 'S' ) Ogg( file, tags );
		}
		catch ( Exception e ) when ( e is ArgumentException or IndexOutOfRangeException or DecoderFallbackException )
		{
			//A tag that runs off the end of the file is a tag that is not there.
		}

		return tags;
	}

	//-- ID3v2 ------------------------------------------------------------------

	private static readonly (string Frame, string Key)[] Id3Frames =
	[
		("TIT2", "title"), ("TPE1", "artist"), ("TALB", "album"), ("TYER", "date"), ("TDRC", "date"), ("TCON", "genre"),
	];

	private static void Id3( ReadOnlySpan<byte> file, Dictionary<string, string> tags )
	{
		var version = file[3];
		var flags = file[5];
		var size = Synchsafe( file[6..10] );

		var body = file.Slice( 10, Math.Min( size, file.Length - 10 ) );

		//An extended header, which says nothing wanted here.
		if ( ( flags & 0x40 ) != 0 && body.Length >= 4 )
		{
			var extended = version >= 4 ? Synchsafe( body[..4] ) : (int)BinaryPrimitives.ReadUInt32BigEndian( body ) + 4;
			body = body[Math.Min( extended, body.Length )..];
		}

		while ( body.Length >= 10 && body[0] != 0 )
		{
			var id = Encoding.ASCII.GetString( body[..4] );
			var length = version >= 4 ? Synchsafe( body[4..8] ) : (int)BinaryPrimitives.ReadUInt32BigEndian( body[4..8] );

			if ( length <= 0 || 10 + length > body.Length ) break;

			var content = body.Slice( 10, length );

			foreach ( var (frame, key) in Id3Frames )
			{
				if ( frame == id && !tags.ContainsKey( key ) ) tags[key] = Id3Text( content );
			}

			body = body[( 10 + length )..];
		}
	}

	private static int Synchsafe( ReadOnlySpan<byte> b ) => ( b[0] << 21 ) | ( b[1] << 14 ) | ( b[2] << 7 ) | b[3];

	//A text frame: its first byte says how the rest is written.
	private static string Id3Text( ReadOnlySpan<byte> content )
	{
		if ( content.Length < 2 ) return "";

		var text = content[1..];

		var decoded = content[0] switch
		{
			0 => Encoding.Latin1.GetString( text ),
			1 => Utf16( text ),
			2 => Encoding.BigEndianUnicode.GetString( text ),
			_ => Encoding.UTF8.GetString( text ),
		};

		//Several values are separated by noughts; the first is the one wanted.
		return decoded.Split( '\0' )[0].Trim();
	}

	//UTF-16 with its byte order mark saying which way round.
	private static string Utf16( ReadOnlySpan<byte> text )
	{
		if ( text.Length >= 2 && text[0] == 0xFE && text[1] == 0xFF ) return Encoding.BigEndianUnicode.GetString( text[2..] );
		if ( text.Length >= 2 && text[0] == 0xFF && text[1] == 0xFE ) return Encoding.Unicode.GetString( text[2..] );

		return Encoding.Unicode.GetString( text );
	}

	//-- Vorbis comments --------------------------------------------------------

	//A vendor string, then a count of KEY=value strings - all lengths little-endian.
	private static void Comments( ReadOnlySpan<byte> block, Dictionary<string, string> tags )
	{
		var vendor = (int)BinaryPrimitives.ReadUInt32LittleEndian( block );
		block = block[( 4 + vendor )..];

		var count = (int)BinaryPrimitives.ReadUInt32LittleEndian( block );
		block = block[4..];

		for ( var i = 0; i < count && block.Length >= 4; ++i )
		{
			var length = (int)BinaryPrimitives.ReadUInt32LittleEndian( block );
			if ( length < 0 || 4 + length > block.Length ) break;

			var comment = Encoding.UTF8.GetString( block.Slice( 4, length ) );
			block = block[( 4 + length )..];

			var equals = comment.IndexOf( '=' );
			if ( equals <= 0 ) continue;

			var key = comment[..equals].ToLowerInvariant();
			if ( !tags.ContainsKey( key ) ) tags[key] = comment[( equals + 1 )..].Trim();
		}
	}

	//A flac's metadata blocks follow its marker; the comments are block type four.
	private static void Flac( ReadOnlySpan<byte> file, Dictionary<string, string> tags )
	{
		var at = 4;

		while ( at + 4 <= file.Length )
		{
			var header = file[at];
			var length = ( file[at + 1] << 16 ) | ( file[at + 2] << 8 ) | file[at + 3];

			if ( ( header & 0x7F ) == 4 && at + 4 + length <= file.Length )
			{
				Comments( file.Slice( at + 4, length ), tags );
				return;
			}

			if ( ( header & 0x80 ) != 0 ) return;

			at += 4 + length;
		}
	}

	//An ogg's comments are the second packet of the stream, which starts on its second page. Pages are gathered until the packet is whole.
	private static void Ogg( ReadOnlySpan<byte> file, Dictionary<string, string> tags )
	{
		var packet = new List<byte>();
		var at = 0;
		var page = 0;

		while ( at + 27 <= file.Length && page < 16 )
		{
			if ( file[at] != 'O' || file[at + 1] != 'g' || file[at + 2] != 'g' || file[at + 3] != 'S' ) return;

			var segments = file[at + 26];
			var table = file.Slice( at + 27, segments );
			var data = at + 27 + segments;

			var size = 0;
			foreach ( var s in table ) size += s;

			if ( page >= 1 )
			{
				//Up to the end of the first packet on this page: a segment shorter than 255 ends a packet.
				var offset = 0;

				foreach ( var s in table )
				{
					packet.AddRange( file.Slice( data + offset, s ).ToArray() );
					offset += s;

					if ( s < 255 )
					{
						var body = packet.ToArray().AsSpan();

						//Vorbis: 0x03 "vorbis"; Opus: "OpusTags".
						if ( body.Length > 7 && body[0] == 3 ) Comments( body[7..], tags );
						else if ( body.Length > 8 && Encoding.ASCII.GetString( body[..8] ) == "OpusTags" ) Comments( body[8..], tags );

						return;
					}
				}
			}

			at = data + size;
			page++;
		}
	}
}
